import math
import os
import glob
from datetime import datetime
import gymnasium as gym
from gymnasium import spaces
import numpy as np
import torch
import torch.nn as nn
from sb3_contrib import MaskablePPO
from sb3_contrib.common.maskable.utils import get_action_masks
from stable_baselines3.common.env_checker import check_env
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor

# ==============================================================================
# LAYOUT DEL TABLERO
# ==============================================================================

PASSABLE          = 0   # Casilla normal, transitable
NO_PASSABLE       = 1   # Casilla intransitable
PLAYER_DEPLOYMENT = 2   # Casilla de despliegue del jugador (solo al inicio, luego es transitable)
BOT_DEPLOYMENT    = 3   # Casilla de despliegue del bot (solo al inicio, luego es transitable)

BOARD = np.array([
    [3, 3, 3, 3],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [2, 2, 2, 2],
], dtype=np.int8)

ROWS, COLS = BOARD.shape

# ==============================================================================
# PIEZA
# ==============================================================================

# Propietarios
PLAYER = 0
BOT    = 1

# Rango de piezas
RANK_TURRET         = -1
RANK_ENERGY_CORE    = 0
RANK_PHANTOM        = 1
RANK_SCOUT          = 2
RANK_SABOTEUR       = 3
RANK_SOLDIER        = 4
RANK_ARMORER        = 5
RANK_COMBAT_UNIT    = 6
RANK_ANDROID        = 7
RANK_MECHA          = 8
RANK_GUARD          = 9
RANK_CORE           = 10

# Nombres por rango (solo para diagnostico, no afecta al entorno)
PIECE_NAMES = {
    RANK_TURRET:      "TURRET",
    RANK_ENERGY_CORE: "ENERGY_CORE",
    RANK_PHANTOM:     "PHANTOM",
    RANK_SCOUT:       "SCOUT",
    RANK_SABOTEUR:    "SABOTEUR",
    RANK_SOLDIER:     "SOLDIER",
    RANK_ARMORER:     "ARMORER",
    RANK_COMBAT_UNIT: "COMBAT_UNIT",
    RANK_ANDROID:     "ANDROID",
    RANK_MECHA:       "MECHA",
    RANK_GUARD:       "GUARD",
    RANK_CORE:        "CORE",
}

# Piezas que no pueden moverse (siguen las reglas normales de combate)
UNMOVABLE_PIECES = {RANK_TURRET, RANK_ENERGY_CORE}

class Piece:
    # Cada pieza conoce su propio rango, propietario y si ha sido revelada para el bot en combate.
    def __init__(self, rank: int, owner: int, revealed: bool = False):
        self.rank     = rank
        self.owner    = owner
        self.revealed = revealed
        self.tile_cooldowns: dict[tuple, int] = {}

    # Restriccion de movimiento: no puede volver a una casilla en la que estuvo en los ultimos 3 turnos (norma del juego para evitar movimientos repetidos).
    def can_return_to(self, pos: tuple, current_turn: int) -> bool:
        self._cleanup_cooldowns(current_turn)
        if pos not in self.tile_cooldowns:
            return True
        return (current_turn - self.tile_cooldowns[pos]) >= 3

    # Registrar que la pieza ha salido de una casilla en un turno determinado (para aplicar cooldown).
    def register_exit(self, pos: tuple, turn: int):
        self.tile_cooldowns[pos] = turn

    # Limpiar cooldowns expirados (mas de 3 turnos) para evitar que el diccionario crezca indefinidamente.
    def _cleanup_cooldowns(self, current_turn: int):
        expired = [p for p, t in self.tile_cooldowns.items() if current_turn - t >= 3]
        for p in expired:
            del self.tile_cooldowns[p]

    # Solo las piezas que no son TURRET ni ENERGY_CORE pueden moverse.
    def can_move(self) -> bool:
        return self.rank not in UNMOVABLE_PIECES

# ==============================================================================
# COMBATE
# ==============================================================================

ATTACKER_DIES = 0
DEFENDER_DIES = 1
BOTH_DIE      = 2

# Devuelve el resultado del combate entre atacante y defensor
def resolve_combat(attacker: Piece, defender: Piece) -> int:
    # Ambos quedan revelados tras el combate
    attacker.revealed = True
    defender.revealed = True

    # Torreta destruye todo excepto al saboteador
    if defender.rank == RANK_TURRET:
        return DEFENDER_DIES if attacker.rank == RANK_SABOTEUR else ATTACKER_DIES

    # Phantom derrota al CORE si es el que ataca
    if attacker.rank == RANK_PHANTOM and defender.rank == RANK_CORE:
        return DEFENDER_DIES

    # Regla general: la pieza de mayor rango gana
    if attacker.rank > defender.rank:
        return DEFENDER_DIES
    if attacker.rank < defender.rank:
        return ATTACKER_DIES

    # Si hay empate ambas piezas mueren
    return BOTH_DIE

def combat_reward(rank: int, is_turret: bool = False) -> float:
    if is_turret:
        return 0.25  # Valor fijo moderado
    # log2(rango+1) normalizado por log2(MAX_RANK+1) → siempre entre 0 y 1
    return round(math.log2(rank + 1) / math.log2(MAX_RANK + 1), 3)

# ==============================================================================
# MOVIMIENTO
# ==============================================================================

DIRECTIONS = [(-1, 0), (1, 0), (0, -1), (0, 1)]
DIR_NAMES  = ["N", "S", "O", "E"]

# Verifica si una casilla es transitable dado el tablero y las coordenadas (dentro del tablero y no es de tipo NO_PASSABLE).
def is_passable(board_layout: np.ndarray, r: int, c: int) -> bool:
    if r < 0 or r >= ROWS or c < 0 or c >= COLS:
        return False
    return board_layout[r, c] != NO_PASSABLE

# Verifica si una pieza puede moverse de una casilla a otra, considerando las reglas de movimiento, el tablero, las piezas presentes y el turno actual.
def can_move(piece: Piece, from_pos: tuple, to_pos: tuple, board_layout: np.ndarray, pieces: dict, turn: int) -> bool:

    # La pieza tiene que ser capaz de moverse
    if not piece.can_move():
        return False

    r1, c1 = from_pos
    r2, c2 = to_pos

    # No puede moverse a si mismo
    if from_pos == to_pos:
        return False

    # La casilla destino tiene que ser transitable
    if not is_passable(board_layout, r2, c2):
        return False

    target_piece = pieces.get(to_pos)
    is_attack = target_piece is not None    # Si hay una pieza en la casilla destino, se considera un ataque

    # No puede atacar a piezas propias
    if target_piece and target_piece.owner == piece.owner:
        return False

    # Restriccion de cooldown solo para movimientos no atacantes
    if not is_attack and not piece.can_return_to(to_pos, turn):
        return False

    # Verificar si el movimiento es correcto segun el tipo de pieza:

    # En caso de mover un Scout verificar que el camino sea valido
    if piece.rank == RANK_SCOUT:
        return _is_scout_path_valid(from_pos, to_pos, board_layout, pieces, piece.owner)

    # Movimiento normal: 1 casilla en las 4 direcciones
    return abs(r1 - r2) + abs(c1 - c2) == 1

# Para el Scout, se verifica que el movimiento sea estrictamente horizontal o vertical,
# que todas las casillas intermedias sean transitable y no estén bloqueadas por piezas aliadas,
# y que no intente pasar por encima de piezas enemigas (solo puede atacar a la pieza enemiga si es la casilla destino).
def _is_scout_path_valid(from_pos, to_pos, board_layout, pieces, owner) -> bool:
    r1, c1 = from_pos
    r2, c2 = to_pos

    # Solo horizontal o vertical
    if r1 != r2 and c1 != c2:
        return False

    # Direccion (normalizada a -1, 0 o 1)
    dr = int(np.sign(r2 - r1))
    dc = int(np.sign(c2 - c1))

    # Numero de pasos a recorrer
    steps = max(abs(r2 - r1), abs(c2 - c1))

    for step in range(1, steps + 1):
        r = r1 + step * dr
        c = c1 + step * dc

        # Si la casilla no es transitable, el camino es invalido
        if not is_passable(board_layout, r, c):
            return False

        occupant = pieces.get((r, c))
        if occupant:
            # Si hay pieza aliada bloqueando el camino: invalido
            if occupant.owner == owner:
                return False
            # Si hay pieza enemiga no es la casilla objetivo: no puede pasar
            if (r, c) != to_pos:
                return False

            # Es la casilla objetivo y hay enemigo: ataque valido
            return True

        # Si llegamos a la casilla objetivo sin encontrar bloqueos, el movimiento es valido
        if (r, c) == to_pos:
            return True

    return False # Fallback, no deberia ocurrir

# Dada una pieza, su posicion actual, el layout del tablero, las piezas presentes y el turno, devuelve una lista de todas las posiciones a las que esa pieza puede moverse legalmente.
def get_all_valid_moves(piece: Piece, from_pos: tuple, board_layout: np.ndarray, pieces: dict, turn: int) -> list:
    valid = []

    if piece.rank == RANK_SCOUT:
        for r in range(ROWS):
            for c in range(COLS):
                to = (r, c)
                if to != from_pos and can_move(piece, from_pos, to, board_layout, pieces, turn):
                    valid.append(to)
    else:
        for dr, dc in DIRECTIONS:
            to = (from_pos[0] + dr, from_pos[1] + dc)
            if can_move(piece, from_pos, to, board_layout, pieces, turn):
                valid.append(to)
    return valid

# ===================================================================================== #
# ------------------------------------------------------------------------------------- #
# ----------------------------------- E N T O R N O ----------------------------------- #
# ------------------------------------------------------------------------------------- #
# ===================================================================================== #

# ==============================================================================
# CANALES DE OBSERVACION (9 canales)
#
# Canal 0: piezas propias del BOT móviles        - rango normalizado (0,1]
# Canal 1: piezas enemigas reveladas             - rango normalizado (0,1]
# Canal 2: piezas enemigas ocultas               - 1.0
# Canal 3: casillas intransitables               - 1.0
# Canal 4: TURRET propia                         - 1.0
# Canal 5: TURRET enemiga revelada               - 1.0
# Canal 6: ENERGY_CORE propio                    - 1.0
# Canal 7: ENERGY_CORE enemigo localizado        - 1.0  (solo tras haberlo visto en combate)
# Canal 8: zona de despliegue enemiga            - 1.0  (donde puede estar el Core oculto)
# ==============================================================================

N_CHANNELS = 9
MAX_RANK   = 10.0

# ==============================================================================
# EXTRACTOR CNN PERSONALIZADO
# ==============================================================================

# Define la interpretacion del modelo sobre el tablero antes de tomar decisiones
class StrategoCNN(BaseFeaturesExtractor):
    def __init__(self, observation_space: spaces.Box, features_dim: int = 128):
        # Registrar el espacio de observación y el tamaño final de features
        super().__init__(observation_space, features_dim)
        n_channels = observation_space.shape[0]

        # BLOQUE CNN (extracción de patrones espaciales)
        self.cnn = nn.Sequential(
            # Conv1: detecta patrones simples (vecindad 2x2)
            nn.Conv2d(n_channels, 32, kernel_size=2, stride=1, padding=1),
            nn.ReLU(),
            # Conv2: combina patrones en otros más complejos
            nn.Conv2d(32, 64, kernel_size=2, stride=1, padding=0),
            nn.ReLU(),
            # Convierte (C, H, W) → vector plano
            nn.Flatten(),
        )

        # CALCULAR TAMAÑO DE SALIDA DE LA CNN
        with torch.no_grad():
            sample       = torch.zeros(1, *observation_space.shape)
            cnn_out_size = self.cnn(sample).shape[1]

        # CAPA FINAL (MLP)
        self.linear = nn.Sequential(
            nn.Linear(cnn_out_size, features_dim),
            nn.ReLU(),
        )

    # FORWARD (flujo de datos) tablero → CNN → vector → capa linear → features
    def forward(self, obs: torch.Tensor) -> torch.Tensor:
        return self.linear(self.cnn(obs))

# ==============================================================================
# CONSTANTES DE RECOMPENSA
# ==============================================================================

R_WIN                   =  1.0  # Destruir el ENERGY_CORE rival
R_LOSE                  = -1.0 # Perder el ENERGY_CORE propio
R_NO_MOVES_WIN          =  1.0 # El rival se queda sin movimientos
R_NO_MOVES_LOSE         = -1.0 # El bot se queda sin movimientos
R_MOVE                  = -0.002 # Coste por turno (incentiva terminar rapido)
R_PIECE_SCALE           =  5.0  # Escala para recompensas de combate
R_TIE                   =  0.0  # Recompensa por empate
R_TURRET_VALUE          =  2.0  # Valor de rango asignado a la TURRET
ILLEGAL_MOVE_PENALTY    = -1.0 # Penalización por intentar un movimiento ilegal (MaskablePPO debería evitarlo)

# ==============================================================================
# ENTORNO GYM
# ==============================================================================
class StrategoEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(self):
        super().__init__()
        n_tiles = ROWS * COLS

        # Cada acción es un movimiento de una casilla origen a una destino: from_tile * n_tiles + to_tile
        self.action_space = spaces.Discrete(n_tiles * n_tiles)

        self.observation_space = spaces.Box(
            low=0.0, high=1.0,
            shape=(N_CHANNELS, ROWS, COLS),
            dtype=np.float32
        )

        self.board_layout = BOARD.copy()
        self.pieces: dict[tuple, Piece] = {}
        self.turn = 0
        self.opponent_model = None  # None = aleatorio, modelo = self-play
        self.reset()

    # Reinicia el entorno a un estado inicial con despliegue aleatorio de piezas dentro de las zonas de despliegue, y devuelve la observación inicial.
    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self.pieces = {}
        self.turn   = 0

        # Despliegue ALEATORIO en cada reset para evitar que el modelo memorice
        # posiciones fijas y para prevenir convergencia degenerada (ep_len=1).
        bot_tiles    = [(r, c) for r in range(ROWS) for c in range(COLS)
                        if self.board_layout[r, c] == BOT_DEPLOYMENT]
        player_tiles = [(r, c) for r in range(ROWS) for c in range(COLS)
                        if self.board_layout[r, c] == PLAYER_DEPLOYMENT]

        np.random.shuffle(bot_tiles)
        np.random.shuffle(player_tiles)

        # Ejercitos
        bot_army = [
            Piece(RANK_ENERGY_CORE, BOT, revealed=True),
            Piece(RANK_TURRET,      BOT, revealed=True),
            Piece(RANK_SABOTEUR,    BOT, revealed=True),
            Piece(RANK_SCOUT,       BOT, revealed=True),
        ]
        for i, piece in enumerate(bot_army):
            self.pieces[bot_tiles[i]] = piece

        player_army = [
            Piece(RANK_ENERGY_CORE, PLAYER, revealed=False),
            Piece(RANK_TURRET,      PLAYER, revealed=False),
            Piece(RANK_SABOTEUR,    PLAYER, revealed=False),
            Piece(RANK_SCOUT,       PLAYER, revealed=False),
        ]
        for i, piece in enumerate(player_army):
            self.pieces[player_tiles[i]] = piece

        return self._get_obs(), {}

    def action_masks(self) -> np.ndarray:
        """
        Devuelve una mascara booleana de acciones legales para MaskablePPO.
        True  = accion legal.
        False = accion ilegal (MaskablePPO nunca la elegira).

        IMPORTANTE: MaskablePPO requiere que siempre haya al menos una accion habilitada o lanzara un error Simplex().
        Si el bot no tiene movimientos el episodio deberia haber terminado antes, pero usamos un fallback robusto
        que busca cualquier movimiento adyacente pasable en lugar de habilitar la accion 0 arbitrariamente
        (que puede apuntar a una casilla invalida).
        """

        n_tiles = ROWS * COLS
        mask = np.zeros(self.action_space.n, dtype=bool)    # Inicialmente todas las acciones son ilegales

        # Primero, habilitamos solo las acciones legales reales: movimientos de piezas del bot a casillas válidas según las reglas del juego.
        for from_idx in range(n_tiles):
            from_pos = (from_idx // COLS, from_idx % COLS)
            piece    = self.pieces.get(from_pos)
            if piece is None or piece.owner != BOT:
                continue
            for to_pos in get_all_valid_moves(piece, from_pos, self.board_layout, self.pieces, self.turn):
                to_idx = to_pos[0] * COLS + to_pos[1]
                mask[from_idx * n_tiles + to_idx] = True

        # Fallback: buscar cualquier movimiento adyacente pasable de cualquier pieza del bot.
        if not mask.any():
            for from_idx in range(n_tiles):
                from_pos = (from_idx // COLS, from_idx % COLS)
                piece    = self.pieces.get(from_pos)
                if piece is None or piece.owner != BOT or not piece.can_move():
                    continue
                for dr, dc in DIRECTIONS:
                    r2, c2 = from_pos[0] + dr, from_pos[1] + dc
                    if is_passable(self.board_layout, r2, c2):
                        to_idx = r2 * COLS + c2
                        mask[from_idx * n_tiles + to_idx] = True
                        return mask
            mask[0] = True

        return mask

    def step(self, action: int):
        """
        Avance de un turno completo:
        1. El BOT ejecuta su acción
        2. ¿Terminó la partida? → retorna
        3. El PLAYER elige y ejecuta su movimiento
        4. ¿Terminó la partida? → retorna con recompensa final
        """

        reward     = 0.0
        terminated = False
        truncated  = False

        # Comprobar si la partida ha terminado antes del turno del bot
        terminated, end_reward = self._check_game_over()
        if terminated:
            return self._get_obs(), reward, terminated, truncated, {}

        n_tiles  = ROWS * COLS
        from_idx = int(action) // n_tiles
        to_idx   = int(action) % n_tiles
        from_pos = (from_idx // COLS, from_idx % COLS)
        to_pos   = (to_idx   // COLS, to_idx   % COLS)

        piece = self.pieces.get(from_pos)

        # --- TURNO DEL BOT ---

        # Con MaskablePPO no deberiamos llegar aqui con una accion ilegal.
        if piece is None or piece.owner != BOT:
            return self._get_obs(), ILLEGAL_MOVE_PENALTY, True, False, {}
        elif not can_move(piece, from_pos, to_pos, self.board_layout, self.pieces, self.turn):
            return self._get_obs(), ILLEGAL_MOVE_PENALTY, True, False, {}
        else:
            # Ejecutar el movimiento del bot y obtener recompensa inmediata por ese movimiento (incluye combate si lo hay)
            reward, terminated = self._execute_move(BOT, from_pos, to_pos)

        # Comprobar fin de la partida tras turno del bot (puede haber terminado por
        # falta de movimientos del player aunque _execute_move no lo detecte).
        if terminated:
            return self._get_obs(), reward, True, False, {}

        # --- TURNO DEL PLAYER ---
        # El jugador elige su movimiento y lo ejecuta, obteniendo recompensa negativa para el bot (ventaja del jugador).
        self.turn += 1
        player_moves = self._get_all_moves(PLAYER)
        if not player_moves:
            terminated, end_reward = self._check_game_over()
            reward += end_reward
            return self._get_obs(), reward, True, False, {}
        
        pf, pt = self._choose_opponent_move(player_moves)
        p_reward, p_terminated = self._execute_move(PLAYER, pf, pt)
        reward += -p_reward

        # Si el movimiento del jugador terminó la partida, retornamos con la recompensa acumulada.
        if p_terminated:
            return self._get_obs(), reward, True, False, {}

        # Comprobar fin de la partida tras turno del jugador
        terminated, end_reward = self._check_game_over()
        reward += end_reward

        self.turn += 1
        return self._get_obs(), reward, terminated, truncated, {}

    def _execute_move(self, actor: int, from_pos: tuple, to_pos: tuple):
        """
        Ejecuta un movimiento y devuelve (recompensa_para_bot, terminado).
        Cuando actor=PLAYER, las recompensas positivas indican ventaja del jugador
        (que se invierten en step() para calcular la recompensa final del bot).
        """
        piece      = self.pieces[from_pos]
        target     = self.pieces.get(to_pos)
        reward     = R_MOVE
        terminated = False

        # Si no hay pieza en el destino, se mueve la pieza y se aplica cooldown para volver a la casilla.
        if target is None:
            piece.register_exit(from_pos, self.turn)
            self.pieces[to_pos] = piece
            del self.pieces[from_pos]

        # Si hay una pieza enemiga, se resuelve el combate
        else:
            result = resolve_combat(piece, target)
            attacker_is_bot = (actor == BOT)

            # El resultado del combate determina qué piezas mueren y qué recompensas se otorgan
            if result == DEFENDER_DIES:
                if target.rank == RANK_ENERGY_CORE:
                    reward     = R_WIN if attacker_is_bot else R_LOSE
                    terminated = True
                else:
                    reward = combat_reward(target.rank, is_turret=(target.rank == RANK_TURRET))
                    if not attacker_is_bot:
                        reward = -reward

                piece.register_exit(from_pos, self.turn)
                del self.pieces[from_pos]
                if to_pos in self.pieces:
                    del self.pieces[to_pos]
                self.pieces[to_pos] = piece

            elif result == ATTACKER_DIES:
                if piece.rank == RANK_ENERGY_CORE:
                    reward     = R_LOSE if attacker_is_bot else R_WIN
                    terminated = True
                else:
                    reward = combat_reward(piece.rank, is_turret=(piece.rank == RANK_TURRET))
                    if not attacker_is_bot:
                        reward = -reward

                piece.register_exit(from_pos, self.turn) # No es necesario, solo por consistencia
                if from_pos in self.pieces:
                    del self.pieces[from_pos]

            else:   # Empate: ambas piezas mueren
                reward = R_TIE

                piece.register_exit(from_pos, self.turn) # No es necesario, solo por consistencia
                if from_pos in self.pieces:
                    del self.pieces[from_pos]
                if to_pos in self.pieces:
                    del self.pieces[to_pos]

        return reward, terminated

    def _check_game_over(self) -> tuple[bool, float]:
        """
        Comprueba si la partida ha terminado. Condiciones de victoria:
        1. ENERGY_CORE destruido -> pierde ese bando.
        2. Un bando sin movimientos posibles -> ese bando pierde.
        Si ambos bandos se quedan sin movimientos a la vez -> empate (reward 0).
        """

        bot_has_core    = any(p.rank == RANK_ENERGY_CORE and p.owner == BOT    for p in self.pieces.values())
        player_has_core = any(p.rank == RANK_ENERGY_CORE and p.owner == PLAYER for p in self.pieces.values())

        if not bot_has_core:
            return True, R_LOSE
        if not player_has_core:
            return True, R_WIN

        bot_has_moves    = bool(self._get_all_moves(BOT))
        player_has_moves = bool(self._get_all_moves(PLAYER))

        if not bot_has_moves and not player_has_moves:
            return True, R_TIE
        if not bot_has_moves:
            return True, R_NO_MOVES_LOSE
        if not player_has_moves:
            return True, R_NO_MOVES_WIN

        return False, 0.0

    def _get_all_moves(self, owner: int) -> list:
        """
        Devuelve una lista de todos los movimientos legales para el propietario.
        """

        moves = []
        for pos, piece in list(self.pieces.items()):
            if piece.owner != owner:
                continue
            for to in get_all_valid_moves(piece, pos, self.board_layout, self.pieces, self.turn):
                moves.append((pos, to))
        return moves

    def _get_obs(self) -> np.ndarray:
        """
        Construye la observacion actual del entorno desde la perspectiva del BOT.

        Canal 0: piezas propias moviles          - rango normalizado
        Canal 1: piezas enemigas reveladas        - rango normalizado
        Canal 2: piezas enemigas ocultas          - 1.0
        Canal 3: casillas intransitables          - 1.0
        Canal 4: TURRET propia                   - 1.0
        Canal 5: TURRET enemiga revelada          - 1.0
        Canal 6: ENERGY_CORE propio              - 1.0
        Canal 7: ENERGY_CORE enemigo localizado  - 1.0 (solo si fue revelado en combate)
        Canal 8: zona de despliegue enemiga      - 1.0 (busqueda: el Core esta en alguna de estas casillas)
        """
        obs = np.zeros((N_CHANNELS, ROWS, COLS), dtype=np.float32)

        # Canal 3: casillas intransitables (estatico, no cambia durante la partida)
        for r in range(ROWS):
            for c in range(COLS):
                if self.board_layout[r, c] == NO_PASSABLE:
                    obs[3, r, c] = 1.0

        # Canal 8: zona de despliegue enemiga
        # El bot sabe que el Core rival comenzó en esta zona, aunque no sepa exactamente dónde.
        # A medida que ataca piezas ocultas en esa zona y no aparece el Core, puede inferir
        # por descarte dónde está. Esto le da información estructural sin revelar la posición exacta.
        for r in range(ROWS):
            for c in range(COLS):
                if self.board_layout[r, c] == PLAYER_DEPLOYMENT:
                    obs[8, r, c] = 1.0

        for (r, c), piece in self.pieces.items():
            # Piezas del bot
            if piece.owner == BOT:
                if piece.rank == RANK_TURRET:
                    obs[4, r, c] = 1.0                      # Canal 4: TURRET propia
                elif piece.rank == RANK_ENERGY_CORE:
                    obs[6, r, c] = 1.0                      # Canal 6: ENERGY_CORE propio
                else:
                    obs[0, r, c] = piece.rank / MAX_RANK    # Canal 0: piezas moviles propias

            # Piezas del jugador
            else:
                if piece.rank == RANK_ENERGY_CORE:
                    if piece.revealed:
                        obs[7, r, c] = 1.0                  # Canal 7: Core localizado (fue revelado)
                    # Si no está revelado: no se muestra en ningún canal individual,
                    # pero el canal 8 ya indica que la zona de despliegue existe.
                    # El bot debe inferirlo por descarte.
                    else:
                        obs[2, r, c] = 1.0                  # Sigue siendo una pieza oculta desconocida
                elif not piece.revealed:
                    obs[2, r, c] = 1.0                      # Canal 2: pieza enemiga oculta
                elif piece.rank == RANK_TURRET:
                    obs[5, r, c] = 1.0                      # Canal 5: TURRET enemiga revelada
                else:
                    obs[1, r, c] = piece.rank / MAX_RANK    # Canal 1: pieza revelada con rango conocido

        return obs

    def _choose_opponent_move(self, player_moves: list) -> tuple:
        """
        Elige el movimiento del jugador oponente.
        Si hay modelo de self-play, lo usa; si no, elige aleatoriamente.
        """

        if self.opponent_model is None:
            return player_moves[np.random.randint(len(player_moves))]

        obs_player = self._get_obs_as_player()
        mask       = self._get_opponent_masks()
        action, _  = self.opponent_model.predict(
            obs_player, deterministic=False, action_masks=mask
        )

        n_tiles  = ROWS * COLS
        from_idx = int(action) // n_tiles
        to_idx   = int(action) % n_tiles

        # El oponente ve el tablero girado (filas invertidas)
        from_r = (ROWS - 1) - (from_idx // COLS)
        from_c = from_idx % COLS
        to_r   = (ROWS - 1) - (to_idx   // COLS)
        to_c   = to_idx % COLS
        from_pos = (from_r, from_c)
        to_pos   = (to_r, to_c)

        piece = self.pieces.get(from_pos)
        if (piece is not None and piece.owner == PLAYER
                and can_move(piece, from_pos, to_pos, self.board_layout, self.pieces, self.turn)):
            return from_pos, to_pos

        # Fallback aleatorio si la accion del modelo no es valida
        return player_moves[np.random.randint(len(player_moves))]

    def _get_obs_as_player(self) -> np.ndarray:
        """
        Observacion girada verticalmente para el oponente.
        Aplica la misma logica de ocultacion que _get_obs pero desde la perspectiva del PLAYER:
        el jugador tampoco conoce la posicion del ENERGY_CORE del bot hasta combatir con él.
        """
        obs = np.zeros((N_CHANNELS, ROWS, COLS), dtype=np.float32)

        for r in range(ROWS):
            r_flip = (ROWS - 1) - r
            for c in range(COLS):
                if self.board_layout[r, c] == NO_PASSABLE:
                    obs[3, r_flip, c] = 1.0

        # Canal 8 girado: zona de despliegue del BOT (enemigo del jugador)
        for r in range(ROWS):
            r_flip = (ROWS - 1) - r
            for c in range(COLS):
                if self.board_layout[r, c] == BOT_DEPLOYMENT:
                    obs[8, r_flip, c] = 1.0

        for (r, c), piece in self.pieces.items():
            r_flip = (ROWS - 1) - r
            if piece.owner == PLAYER:
                if piece.rank == RANK_TURRET:
                    obs[4, r_flip, c] = 1.0
                elif piece.rank == RANK_ENERGY_CORE:
                    obs[6, r_flip, c] = 1.0
                else:
                    obs[0, r_flip, c] = piece.rank / MAX_RANK
            else:  # BOT = enemigo del jugador
                if piece.rank == RANK_ENERGY_CORE:
                    if piece.revealed:
                        obs[7, r_flip, c] = 1.0             # Core localizado
                    else:
                        obs[2, r_flip, c] = 1.0             # Oculto: pieza desconocida
                elif not piece.revealed:
                    obs[2, r_flip, c] = 1.0
                elif piece.rank == RANK_TURRET:
                    obs[5, r_flip, c] = 1.0
                else:
                    obs[1, r_flip, c] = piece.rank / MAX_RANK

        return obs

    def _get_opponent_masks(self) -> np.ndarray:
        """Mascaras legales para el jugador desde perspectiva girada."""

        n_tiles = ROWS * COLS
        mask    = np.zeros(self.action_space.n, dtype=bool)

        for from_idx in range(n_tiles):
            # Deshacer el giro para encontrar la posicion real
            from_r_real = (ROWS - 1) - (from_idx // COLS)
            from_c      = from_idx % COLS
            from_pos    = (from_r_real, from_c)
            piece       = self.pieces.get(from_pos)
            if piece is None or piece.owner != PLAYER:
                continue
            for to_pos in get_all_valid_moves(piece, from_pos, self.board_layout, self.pieces, self.turn):
                to_r_flip = (ROWS - 1) - to_pos[0]
                to_idx    = to_r_flip * COLS + to_pos[1]
                mask[from_idx * n_tiles + to_idx] = True

        if not mask.any():
            mask[0] = True

        return mask


# ==============================================================================
# UTILIDADES DE DIAGNOSTICO
# ==============================================================================

def print_board(pieces, board_layout):
    """Imprime el tablero en ASCII."""
    symbols = {}
    for (r, c), p in pieces.items():
        if p.owner == BOT:
            name = PIECE_NAMES.get(p.rank, f"r{p.rank}")
            sym  = f"B:{name[:4]}"
        else:
            sym = "P:????" if not p.revealed else f"P:{PIECE_NAMES.get(p.rank, f'r{p.rank}')[:4]}"
        symbols[(r, c)] = sym

    header = "    " + "  ".join(f" C{c} " for c in range(COLS))
    print(header)
    for r in range(ROWS):
        row = f"R{r}  "
        for c in range(COLS):
            if board_layout[r, c] == NO_PASSABLE:
                row += "[####]  "
            elif (r, c) in symbols:
                row += f"[{symbols[(r,c)]}]  "
            else:
                row += "[    ]  "
        print(row)
    print()


def random_reset(env: StrategoEnv) -> np.ndarray:
    """
    Reinicia el entorno con posiciones ALEATORIAS dentro de las zonas de despliegue.
    Sirve para comprobar que el bot generaliza y no ha memorizado posiciones fijas.
    """
    env.pieces = {}
    env.turn   = 0

    bot_tiles    = [(r, c) for r in range(ROWS) for c in range(COLS)
                    if env.board_layout[r, c] == BOT_DEPLOYMENT]
    player_tiles = [(r, c) for r in range(ROWS) for c in range(COLS)
                    if env.board_layout[r, c] == PLAYER_DEPLOYMENT]

    np.random.shuffle(bot_tiles)
    np.random.shuffle(player_tiles)

    bot_army = [
        Piece(RANK_ENERGY_CORE, BOT, revealed=True),
        Piece(RANK_TURRET,      BOT, revealed=True),
        Piece(RANK_SABOTEUR,    BOT, revealed=True),
        Piece(RANK_SCOUT,       BOT, revealed=True),
    ]
    for i, piece in enumerate(bot_army):
        env.pieces[bot_tiles[i]] = piece

    player_army = [
        Piece(RANK_ENERGY_CORE, PLAYER, revealed=False),
        Piece(RANK_TURRET,      PLAYER, revealed=False),
        Piece(RANK_SABOTEUR,    PLAYER, revealed=False),
        Piece(RANK_SCOUT,       PLAYER, revealed=False),
    ]
    for i, piece in enumerate(player_army):
        env.pieces[player_tiles[i]] = piece

    return env._get_obs()


# ==============================================================================
# SELF-PLAY
# ==============================================================================

MODEL_BASE = "ppo_stratego_4x4"

def find_latest_model() -> str | None:
    timestamped = sorted(glob.glob(f"{MODEL_BASE}_v*.zip"), reverse=True)
    if timestamped:
        return timestamped[0].replace(".zip", "")
    if os.path.exists(f"{MODEL_BASE}.zip"):
        return MODEL_BASE
    return None


# ==============================================================================
# ENTRENAMIENTO
# ==============================================================================

TOTAL_TIMESTEPS = 400_000

if __name__ == "__main__":

    env = StrategoEnv()

    print("Verificando entorno...")
    check_env(env, warn=True)
    print("Entorno OK.\n")

    latest = find_latest_model()
    if latest:
        print(f"Modelo encontrado: {latest}.zip")
        print("Modo: SELF-PLAY (el oponente usa el modelo anterior)\n")
        env.opponent_model = MaskablePPO.load(latest)
    else:
        print("Sin modelo previo. Modo: oponente ALEATORIO\n")

    policy_kwargs = dict(
        features_extractor_class  = StrategoCNN,
        features_extractor_kwargs = dict(features_dim=128),
        normalize_images          = False,
    )

    model = MaskablePPO(
        policy        = "CnnPolicy",
        env           = env,
        policy_kwargs = policy_kwargs,
        learning_rate = 3e-4,
        n_steps       = 1024,
        batch_size    = 64,
        n_epochs      = 10,
        gamma         = 0.99,
        gae_lambda    = 0.95,
        clip_range    = 0.2,
        ent_coef      = 0.01,   # Entropia: incentiva explorar
        verbose       = 1,
    )

    print(f"Iniciando entrenamiento ({TOTAL_TIMESTEPS} pasos)...")
    model.learn(total_timesteps=TOTAL_TIMESTEPS)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    save_name = f"{MODEL_BASE}_v{timestamp}"
    model.save(save_name)
    print(f"\nModelo guardado en {save_name}.zip")