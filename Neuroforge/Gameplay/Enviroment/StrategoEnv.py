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
# CONSTANTES
# ==============================================================================

PASSABLE          = 0
NO_PASSABLE       = 1
PLAYER_DEPLOYMENT = 2
BOT_DEPLOYMENT    = 3

PLAYER = 0
BOT    = 1

RANK_ENERGY_CORE = 0
RANK_TURRET      = -1
RANK_PHANTOM     = 1
RANK_SCOUT       = 2
RANK_SABOTEUR    = 3
RANK_CORE        = 10

PIECE_NAMES = {
    RANK_TURRET:      "TURRET",
    RANK_ENERGY_CORE: "ENERGY_CORE",
    RANK_PHANTOM:     "PHANTOM",
    RANK_SCOUT:       "SCOUT",
    RANK_SABOTEUR:    "SABOTEUR",
    4:  "SOLDIER",
    5:  "ARMORER",
    6:  "COMBAT_UNIT",
    7:  "ANDROID",
    8:  "MECHA",
    9:  "GUARD",
    10: "CORE",
}

# ==============================================================================
# LAYOUT DEL TABLERO 4x4 DE PRUEBA
# (Cuando pases al 10x10, cambia esto por tu layout real)
# ==============================================================================

BOARD_4x4 = np.array([
    [3, 3, 3, 3],
    [0, 0, 1, 0],
    [0, 1, 0, 0],
    [2, 2, 2, 2],
], dtype=np.int8)

ROWS, COLS = BOARD_4x4.shape

# ==============================================================================
# CANALES DE OBSERVACION (8 canales, uno por concepto)
#
# Canal 0: piezas propias del BOT          - rango normalizado [0, 1]
# Canal 1: piezas enemigas reveladas       - rango normalizado [0, 1]
# Canal 2: piezas enemigas ocultas         - 1.0
# Canal 3: casillas intransitables         - 1.0
# Canal 4: TURRET propia                   - 1.0
# Canal 5: TURRET enemiga revelada         - 1.0
# Canal 6: ENERGY_CORE propio              - 1.0
# Canal 7: ENERGY_CORE enemigo revelado    - 1.0
# ==============================================================================

N_CHANNELS = 8
MAX_RANK   = 10.0
DIRECTIONS = [(-1, 0), (1, 0), (0, -1), (0, 1)]
DIR_NAMES  = ["N", "S", "O", "E"]

# ==============================================================================
# EXTRACTOR CNN PERSONALIZADO
# ==============================================================================

class StrategoCNN(BaseFeaturesExtractor):
    def __init__(self, observation_space: spaces.Box, features_dim: int = 64):
        super().__init__(observation_space, features_dim)

        n_channels = observation_space.shape[0]

        self.cnn = nn.Sequential(
            nn.Conv2d(n_channels, 32, kernel_size=2, stride=1, padding=0),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=2, stride=1, padding=0),
            nn.ReLU(),
            nn.Flatten(),
        )

        with torch.no_grad():
            sample = torch.zeros(1, *observation_space.shape)
            cnn_out_size = self.cnn(sample).shape[1]

        self.linear = nn.Sequential(
            nn.Linear(cnn_out_size, features_dim),
            nn.ReLU(),
        )

    def forward(self, obs: torch.Tensor) -> torch.Tensor:
        return self.linear(self.cnn(obs))

# ==============================================================================
# PIEZA
# ==============================================================================

class Piece:
    def __init__(self, rank: int, owner: int, revealed: bool = False):
        self.rank     = rank
        self.owner    = owner
        self.revealed = revealed
        self.tile_cooldowns: dict[tuple, int] = {}

    def can_return_to(self, pos: tuple, current_turn: int) -> bool:
        self._cleanup_cooldowns(current_turn)
        if pos not in self.tile_cooldowns:
            return True
        return (current_turn - self.tile_cooldowns[pos]) >= 3

    def register_exit(self, pos: tuple, turn: int):
        self.tile_cooldowns[pos] = turn

    def _cleanup_cooldowns(self, current_turn: int):
        expired = [p for p, t in self.tile_cooldowns.items() if current_turn - t >= 3]
        for p in expired:
            del self.tile_cooldowns[p]

    def can_move(self) -> bool:
        return self.rank not in (RANK_ENERGY_CORE, RANK_TURRET)


# ==============================================================================
# COMBATE
# ==============================================================================

ATTACKER_DIES = 0
DEFENDER_DIES = 1
BOTH_DIE      = 2

def resolve_combat(attacker: Piece, defender: Piece) -> int:
    attacker.revealed = True
    defender.revealed = True

    if defender.rank == RANK_TURRET:
        return DEFENDER_DIES if attacker.rank == RANK_SABOTEUR else ATTACKER_DIES

    if attacker.rank == RANK_PHANTOM and defender.rank == RANK_CORE:
        return DEFENDER_DIES

    if attacker.rank > defender.rank:
        return DEFENDER_DIES
    if attacker.rank < defender.rank:
        return ATTACKER_DIES
    return BOTH_DIE


# ==============================================================================
# MOVIMIENTO
# ==============================================================================

def is_passable(board_layout: np.ndarray, r: int, c: int) -> bool:
    if r < 0 or r >= ROWS or c < 0 or c >= COLS:
        return False
    return board_layout[r, c] != NO_PASSABLE


def can_move(piece: Piece, from_pos: tuple, to_pos: tuple,
             board_layout: np.ndarray, pieces: dict, turn: int) -> bool:
    if not piece.can_move():
        return False

    r1, c1 = from_pos
    r2, c2 = to_pos

    if not is_passable(board_layout, r2, c2):
        return False

    target_piece = pieces.get(to_pos)
    if target_piece and target_piece.owner == piece.owner:
        return False

    is_attack = target_piece is not None and target_piece.owner != piece.owner
    if not is_attack and not piece.can_return_to(to_pos, turn):
        return False

    if piece.rank == RANK_SCOUT:
        return _is_scout_path_valid(from_pos, to_pos, board_layout, pieces, piece.owner)

    return abs(r1 - r2) + abs(c1 - c2) == 1


def _is_scout_path_valid(from_pos, to_pos, board_layout, pieces, owner) -> bool:
    r1, c1 = from_pos
    r2, c2 = to_pos

    if r1 != r2 and c1 != c2:
        return False

    dr = int(np.sign(r2 - r1))
    dc = int(np.sign(c2 - c1))

    r, c = r1 + dr, c1 + dc
    while True:
        if not is_passable(board_layout, r, c):
            return False
        occupant = pieces.get((r, c))
        if occupant:
            if occupant.owner == owner:
                return False
            if (r, c) != to_pos:
                return False
            return True
        if (r, c) == to_pos:
            return True
        r += dr
        c += dc


def get_all_valid_moves(piece: Piece, from_pos: tuple,
                        board_layout: np.ndarray, pieces: dict, turn: int) -> list:
    valid = []
    if piece.rank == RANK_SCOUT:
        for r in range(ROWS):
            for c in range(COLS):
                if (r, c) != from_pos and can_move(piece, from_pos, (r, c), board_layout, pieces, turn):
                    valid.append((r, c))
    else:
        for dr, dc in DIRECTIONS:
            to = (from_pos[0] + dr, from_pos[1] + dc)
            if can_move(piece, from_pos, to, board_layout, pieces, turn):
                valid.append(to)
    return valid


# ==============================================================================
# ENTORNO
# ==============================================================================

class StrategoEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(self):
        super().__init__()

        # Accion = (casilla_origen, casilla_destino): permite al Scout moverse multiples casillas.
        # Total: 16*16=256 para 4x4, 100*100=10000 para 10x10.
        self.action_space = spaces.Discrete(ROWS * COLS * ROWS * COLS)
        self.observation_space = spaces.Box(
            low=0.0, high=1.0,
            shape=(N_CHANNELS, ROWS, COLS),
            dtype=np.float32
        )

        self.board_layout = BOARD_4x4.copy()
        self.pieces: dict[tuple, Piece] = {}
        self.turn = 0
        self.opponent_model = None  # None = aleatorio, modelo = self-play
        self.reset()

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self.pieces = {}
        self.turn   = 0

        self.pieces[(0, 0)] = Piece(RANK_ENERGY_CORE, BOT,    revealed=True)
        self.pieces[(0, 1)] = Piece(RANK_TURRET,      BOT,    revealed=True)
        self.pieces[(0, 2)] = Piece(RANK_SABOTEUR,    BOT,    revealed=True)
        self.pieces[(0, 3)] = Piece(RANK_SCOUT,       BOT,    revealed=True)

        self.pieces[(3, 3)] = Piece(RANK_ENERGY_CORE, PLAYER, revealed=False)
        self.pieces[(3, 2)] = Piece(RANK_TURRET,      PLAYER, revealed=False)
        self.pieces[(3, 1)] = Piece(RANK_SABOTEUR,    PLAYER, revealed=False)
        self.pieces[(3, 0)] = Piece(RANK_SCOUT,       PLAYER, revealed=False)

        return self._get_obs(), {}

    def action_masks(self) -> np.ndarray:
        """
        True  = accion legal.
        False = accion ilegal, MaskablePPO la ignora completamente.
        """
        n_tiles = ROWS * COLS
        mask = np.zeros(self.action_space.n, dtype=bool)
        for from_idx in range(n_tiles):
            from_pos = (from_idx // COLS, from_idx % COLS)
            piece    = self.pieces.get(from_pos)
            if piece is None or piece.owner != BOT:
                continue
            for to_pos in get_all_valid_moves(piece, from_pos, self.board_layout, self.pieces, self.turn):
                to_idx = to_pos[0] * COLS + to_pos[1]
                mask[from_idx * n_tiles + to_idx] = True
        if not mask.any():
            mask[:] = True
        return mask

    def step(self, action: int):
        reward     = 0.0
        terminated = False
        truncated  = False

        # Decodificar: action = from_idx * n_tiles + to_idx
        n_tiles  = ROWS * COLS
        from_idx = action // n_tiles
        to_idx   = action % n_tiles
        from_pos = (from_idx // COLS, from_idx % COLS)
        to_pos   = (to_idx   // COLS, to_idx   % COLS)

        piece = self.pieces.get(from_pos)

        if piece is None or piece.owner != BOT:
            reward = -0.5
        elif not can_move(piece, from_pos, to_pos, self.board_layout, self.pieces, self.turn):
            reward = -0.5
        else:
            reward, terminated = self._execute_move(BOT, from_pos, to_pos)

        if not terminated:
            terminated, end_reward = self._check_game_over()
            if terminated:
                reward += end_reward

        if not terminated:
            self.turn += 1
            player_moves = self._get_all_moves(PLAYER)

            if not player_moves:
                terminated = True
                reward    += 10.0
            else:
                pf, pt = self._choose_opponent_move(player_moves)
                p_reward, p_terminated = self._execute_move(PLAYER, pf, pt)
                reward += -p_reward
                if p_terminated:
                    terminated = True

        if not terminated:
            terminated, end_reward = self._check_game_over()
            if terminated:
                reward += end_reward

        if not terminated:
            self.turn += 1
            if not self._get_all_moves(BOT):
                terminated = True
                reward    -= 10.0

        return self._get_obs(), reward, terminated, truncated, {}

    def _choose_opponent_move(self, player_moves: list) -> tuple:
        """
        Elige el movimiento del jugador (oponente).
        - Si opponent_model es None: movimiento aleatorio.
        - Si opponent_model es un modelo: self-play, el modelo elige
          desde la perspectiva del jugador (tablero invertido).
        """
        if self.opponent_model is None:
            return player_moves[np.random.randint(len(player_moves))]

        # Self-play: construir observacion desde la perspectiva del JUGADOR
        # (intercambia los canales BOT <-> PLAYER y gira el tablero verticalmente)
        obs_bot = self._get_obs()
        obs_player = self._get_obs_as_player()

        # Calcular mascara de acciones legales para el jugador
        mask = self._get_opponent_masks()

        action, _ = self.opponent_model.predict(
            obs_player, deterministic=False, action_masks=mask
        )

        # Decodificar accion desde perspectiva del jugador (tablero girado)
        n_tiles  = ROWS * COLS
        from_idx = int(action) // n_tiles
        to_idx   = int(action) % n_tiles
        # Invertir filas (el jugador ve el tablero al reves)
        from_r = (ROWS - 1) - (from_idx // COLS)
        from_c = from_idx % COLS
        to_r   = (ROWS - 1) - (to_idx   // COLS)
        to_c   = to_idx % COLS
        from_pos = (from_r, from_c)
        to_pos   = (to_r,   to_c)

        # Verificar que la accion es legal; si no, caer en aleatorio
        piece = self.pieces.get(from_pos)
        if (piece is not None and piece.owner == PLAYER and
                can_move(piece, from_pos, to_pos, self.board_layout, self.pieces, self.turn)):
            return from_pos, to_pos

        return player_moves[np.random.randint(len(player_moves))]

    def _get_obs_as_player(self) -> np.ndarray:
        """
        Observacion desde la perspectiva del JUGADOR:
        - Intercambia canales BOT <-> PLAYER
        - Gira el tablero verticalmente (el jugador "ve" desde abajo)
        Esto permite usar el mismo modelo entrenado como BOT para jugar como PLAYER.
        """
        obs = np.zeros((N_CHANNELS, ROWS, COLS), dtype=np.float32)

        for r in range(ROWS):
            r_flip = (ROWS - 1) - r
            for c in range(COLS):
                if self.board_layout[r, c] == NO_PASSABLE:
                    obs[3, r_flip, c] = 1.0

        for (r, c), piece in self.pieces.items():
            r_flip = (ROWS - 1) - r
            if piece.owner == PLAYER:
                # Las piezas del JUGADOR se convierten en "propias" del modelo
                if piece.rank == RANK_TURRET:
                    obs[4, r_flip, c] = 1.0
                elif piece.rank == RANK_ENERGY_CORE:
                    obs[6, r_flip, c] = 1.0
                else:
                    obs[0, r_flip, c] = piece.rank / MAX_RANK
            else:
                # Las piezas del BOT se convierten en "enemigas"
                if not piece.revealed:
                    obs[2, r_flip, c] = 1.0
                elif piece.rank == RANK_TURRET:
                    obs[5, r_flip, c] = 1.0
                elif piece.rank == RANK_ENERGY_CORE:
                    obs[7, r_flip, c] = 1.0
                else:
                    obs[1, r_flip, c] = piece.rank / MAX_RANK
        return obs

    def _get_opponent_masks(self) -> np.ndarray:
        """Mascara de acciones legales para el JUGADOR (tablero girado)."""
        n_tiles = ROWS * COLS
        mask = np.zeros(self.action_space.n, dtype=bool)

        for from_idx in range(n_tiles):
            # Invertir fila para pasar de coordenadas giradas a reales
            from_r_flip = from_idx // COLS
            from_c      = from_idx % COLS
            from_r_real = (ROWS - 1) - from_r_flip
            from_pos    = (from_r_real, from_c)

            piece = self.pieces.get(from_pos)
            if piece is None or piece.owner != PLAYER:
                continue

            for to_pos in get_all_valid_moves(piece, from_pos, self.board_layout, self.pieces, self.turn):
                to_r_flip = (ROWS - 1) - to_pos[0]
                to_idx    = to_r_flip * COLS + to_pos[1]
                mask[from_idx * n_tiles + to_idx] = True

        if not mask.any():
            mask[:] = True
        return mask

    def _execute_move(self, actor: int, from_pos: tuple, to_pos: tuple):
        piece      = self.pieces[from_pos]
        target     = self.pieces.get(to_pos)
        reward     = -0.01
        terminated = False

        if target is None:
            piece.register_exit(from_pos, self.turn)
            self.pieces[to_pos] = piece
            del self.pieces[from_pos]
        else:
            result          = resolve_combat(piece, target)
            attacker_is_bot = (actor == BOT)

            if result == DEFENDER_DIES:
                rank_value = abs(target.rank) if target.rank != RANK_TURRET else 3
                reward = rank_value / MAX_RANK if attacker_is_bot else -rank_value / MAX_RANK
                if target.rank == RANK_ENERGY_CORE:
                    reward     = 10.0 if attacker_is_bot else -10.0
                    terminated = True
                piece.register_exit(from_pos, self.turn)
                del self.pieces[from_pos]
                del self.pieces[to_pos]
                self.pieces[to_pos] = piece

            elif result == ATTACKER_DIES:
                rank_value = abs(piece.rank) if piece.rank != RANK_TURRET else 3
                reward = -rank_value / MAX_RANK if attacker_is_bot else rank_value / MAX_RANK
                if piece.rank == RANK_ENERGY_CORE:
                    reward     = -10.0 if attacker_is_bot else 10.0
                    terminated = True
                del self.pieces[from_pos]

            else:  # BOTH_DIE
                reward = 0.0
                del self.pieces[from_pos]
                del self.pieces[to_pos]

        return reward, terminated

    def _check_game_over(self) -> tuple[bool, float]:
        bot_has_core    = any(p.rank == RANK_ENERGY_CORE and p.owner == BOT    for p in self.pieces.values())
        player_has_core = any(p.rank == RANK_ENERGY_CORE and p.owner == PLAYER for p in self.pieces.values())
        if not bot_has_core:
            return True, -10.0
        if not player_has_core:
            return True,  10.0
        return False, 0.0

    def _get_all_moves(self, owner: int) -> list:
        moves = []
        for pos, piece in list(self.pieces.items()):
            if piece.owner != owner:
                continue
            for to in get_all_valid_moves(piece, pos, self.board_layout, self.pieces, self.turn):
                moves.append((pos, to))
        return moves

    def _get_obs(self) -> np.ndarray:
        obs = np.zeros((N_CHANNELS, ROWS, COLS), dtype=np.float32)

        for r in range(ROWS):
            for c in range(COLS):
                if self.board_layout[r, c] == NO_PASSABLE:
                    obs[3, r, c] = 1.0

        for (r, c), piece in self.pieces.items():
            if piece.owner == BOT:
                if piece.rank == RANK_TURRET:
                    obs[4, r, c] = 1.0
                elif piece.rank == RANK_ENERGY_CORE:
                    obs[6, r, c] = 1.0
                else:
                    obs[0, r, c] = piece.rank / MAX_RANK
            else:
                if not piece.revealed:
                    obs[2, r, c] = 1.0
                elif piece.rank == RANK_TURRET:
                    obs[5, r, c] = 1.0
                elif piece.rank == RANK_ENERGY_CORE:
                    obs[7, r, c] = 1.0
                else:
                    obs[1, r, c] = piece.rank / MAX_RANK

        return obs


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
        Piece(RANK_SABOTEUR,    BOT, revealed=True),
        Piece(RANK_SCOUT,       BOT, revealed=True),
    ]
    for i, piece in enumerate(bot_army):
        env.pieces[bot_tiles[i]] = piece

    player_army = [
        Piece(RANK_ENERGY_CORE, PLAYER, revealed=False),
        Piece(4,                PLAYER, revealed=False),
        Piece(RANK_SCOUT,       PLAYER, revealed=False),
    ]
    for i, piece in enumerate(player_army):
        env.pieces[player_tiles[i]] = piece

    return env._get_obs()


# ==============================================================================
# ENTRENAMIENTO Y TESTS
# ==============================================================================

# Nombre base del modelo
MODEL_BASE = "ppo_stratego_4x4"

def find_latest_model() -> str | None:
    """
    Busca el modelo mas reciente disponible.
    Prioridad: versiones con timestamp > modelo base.
    Devuelve la ruta sin extension, o None si no hay ninguno.
    """
    # Buscar versiones con timestamp primero
    timestamped = sorted(glob.glob(f"{MODEL_BASE}_v*.zip"), reverse=True)
    if timestamped:
        return timestamped[0].replace(".zip", "")
    # Caer en el modelo base si existe
    if os.path.exists(f"{MODEL_BASE}.zip"):
        return MODEL_BASE
    return None


if __name__ == "__main__":

    env = StrategoEnv()

    print("Verificando entorno...")
    check_env(env, warn=True)
    print("Entorno OK.\n")

    # ------------------------------------------------------------------
    # Decidir modo de entrenamiento
    # ------------------------------------------------------------------
    latest = find_latest_model()

    if latest:
        print(f"Modelo encontrado: {latest}.zip")
        print("Modo: SELF-PLAY — el oponente usara el modelo anterior.\n")
        opponent = MaskablePPO.load(latest)
        env.opponent_model = opponent
    else:
        print("No se encontro ningun modelo guardado.")
        print("Modo: ALEATORIO — el oponente movera al azar.\n")

    # ------------------------------------------------------------------
    # Crear y entrenar el nuevo modelo
    # ------------------------------------------------------------------
    policy_kwargs = dict(
        features_extractor_class  = StrategoCNN,
        features_extractor_kwargs = dict(features_dim=64),
        normalize_images          = False,
    )

    model = MaskablePPO(
        policy        = "CnnPolicy",
        env           = env,
        policy_kwargs = policy_kwargs,
        learning_rate = 3e-4,
        n_steps       = 512,
        batch_size    = 64,
        gamma         = 0.95,
        verbose       = 1,
        # tensorboard_log = "./tb_logs/",
    )

    print("Iniciando entrenamiento...")
    model.learn(total_timesteps=1_000_000)

    # Guardar con timestamp para no sobreescribir el anterior
    timestamp   = datetime.now().strftime("%Y%m%d_%H%M%S")
    save_name   = f"{MODEL_BASE}_v{timestamp}"
    model.save(save_name)
    print(f"\nModelo guardado en {save_name}.zip")
    if latest:
        print(f"Modelo anterior conservado en {latest}.zip\n")