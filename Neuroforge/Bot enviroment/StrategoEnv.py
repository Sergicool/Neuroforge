import os
import glob
import math
from datetime import datetime

import numpy as np
import gymnasium as gym
from gymnasium import spaces

from sb3_contrib import MaskablePPO
from stable_baselines3.common.env_checker import check_env

import torch as th
import torch.nn as nn
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor

P  = 0   # PASSABLE
NP = 1   # NO_PASSABLE
PD = 2   # PLAYER_DEPLOYMENT
BD = 3   # BOT_DEPLOYMENT

BOARD = np.array([
    [BD, BD, BD, BD, BD, BD, BD, BD, BD, BD],
    [BD, BD, BD, BD, BD, BD, BD, BD, BD, BD],
    [BD, BD, BD, BD, BD, BD, BD, BD, BD, BD],
    [BD, BD, BD, BD, BD, BD, BD, BD, BD, BD],
    [ P,  P, NP, NP,  P,  P, NP, NP,  P,  P],
    [ P,  P, NP, NP,  P,  P, NP, NP,  P,  P],
    [PD, PD, PD, PD, PD, PD, PD, PD, PD, PD],
    [PD, PD, PD, PD, PD, PD, PD, PD, PD, PD],
    [PD, PD, PD, PD, PD, PD, PD, PD, PD, PD],
    [PD, PD, PD, PD, PD, PD, PD, PD, PD, PD],
], dtype=np.int8)

ROWS, COLS = BOARD.shape

PASSABLE          = 0
NO_PASSABLE       = 1
PLAYER_DEPLOYMENT = 2
BOT_DEPLOYMENT    = 3

# =============================================================================
# PIEZAS — rangos y cantidades
# =============================================================================

PLAYER = 0
BOT    = 1

RANK_ENERGY_CORE = 0
RANK_TURRET      = 0
RANK_PHANTOM     = 1
RANK_SCOUT       = 2
RANK_SABOTEUR    = 3
RANK_SOLDIER     = 4
RANK_CYBORG      = 5
RANK_CANINE      = 6
RANK_SENTINEL    = 7
RANK_MECHA       = 8
RANK_NOVA        = 9
RANK_WAR_MACHINE = 10

MAX_RANK = float(RANK_WAR_MACHINE)

PIECE_TYPE_ENERGY_CORE = "ENERGY_CORE"
PIECE_TYPE_TURRET      = "TURRET"
PIECE_TYPE_PHANTOM     = "PHANTOM"
PIECE_TYPE_SCOUT       = "SCOUT"
PIECE_TYPE_SABOTEUR    = "SABOTEUR"
PIECE_TYPE_SOLDIER     = "SOLDIER"
PIECE_TYPE_CYBORG      = "CYBORG"
PIECE_TYPE_CANINE      = "CANINE"
PIECE_TYPE_SENTINEL    = "SENTINEL"
PIECE_TYPE_MECHA       = "MECHA"
PIECE_TYPE_NOVA        = "NOVA"
PIECE_TYPE_WAR_MACHINE = "WAR_MACHINE"

# Composicion del ejercito: (tipo, rango, puede_moverse, cantidad)
ARMY_COMPOSITION = [
    (PIECE_TYPE_ENERGY_CORE, RANK_ENERGY_CORE, False, 1),
    (PIECE_TYPE_TURRET,      RANK_TURRET,      False, 6),
    (PIECE_TYPE_WAR_MACHINE, RANK_WAR_MACHINE, True,  1),
    (PIECE_TYPE_NOVA,        RANK_NOVA,        True,  1),
    (PIECE_TYPE_MECHA,       RANK_MECHA,       True,  2),
    (PIECE_TYPE_SENTINEL,    RANK_SENTINEL,    True,  3),
    (PIECE_TYPE_CANINE,      RANK_CANINE,      True,  4),
    (PIECE_TYPE_CYBORG,      RANK_CYBORG,      True,  4),
    (PIECE_TYPE_SOLDIER,     RANK_SOLDIER,     True,  4),
    (PIECE_TYPE_SABOTEUR,    RANK_SABOTEUR,    True,  5),
    (PIECE_TYPE_SCOUT,       RANK_SCOUT,       True,  8),
    (PIECE_TYPE_PHANTOM,     RANK_PHANTOM,     True,  1),
]
# Total = 40 piezas
ARMY_SIZE = sum(count for _, _, _, count in ARMY_COMPOSITION)

# =============================================================================
# PIEZA
# =============================================================================

class Piece:
    def __init__(self, piece_type: str, rank: int, owner: int,
                 can_move: bool, revealed: bool = False):
        self.piece_type     = piece_type
        self.rank           = rank
        self.owner          = owner
        self.can_move       = can_move
        self.revealed       = revealed
        self.scout_revealed = False

        # Historial: lista de (from_pos, to_pos), maximo 3 entradas
        self._move_history: list = []

    def register_move(self, from_pos: tuple, to_pos: tuple):
        self._move_history.append((from_pos, to_pos))
        if len(self._move_history) > 3:
            self._move_history.pop(0)

    def is_oscillating(self, from_pos: tuple, to_pos: tuple) -> bool:
        """
        True si este movimiento seria la 4a repeticion de la oscilacion A<->B.
        Patron en historial: A->B, B->A, A->B  +  intento actual B->A.
        """
        if len(self._move_history) < 3:
            return False
        h = self._move_history
        tile_a = h[0][0]
        tile_b = h[0][1]
        return (
            h[0] == (tile_a, tile_b) and
            h[1] == (tile_b, tile_a) and
            h[2] == (tile_a, tile_b) and
            from_pos == tile_b and
            to_pos   == tile_a
        )

# =============================================================================
# COMBATE
# =============================================================================

ATTACKER_DIES = 0
DEFENDER_DIES = 1
BOTH_DIE      = 2

def resolve_combat(attacker: Piece, defender: Piece) -> int:
    """
      1. TURRET defensora destruye todo, SALVO al SABOTEUR.
      2. PHANTOM atacando a WAR_MACHINE siempre gana.
      3. Regla general: mayor rango gana.
      4. Mismo rango: empate, ambas eliminadas.
    """
    attacker.revealed = True
    defender.revealed = True

    if defender.piece_type == PIECE_TYPE_TURRET:
        return (DEFENDER_DIES if attacker.piece_type == PIECE_TYPE_SABOTEUR
                else ATTACKER_DIES)

    if (attacker.piece_type == PIECE_TYPE_PHANTOM and
            defender.piece_type == PIECE_TYPE_WAR_MACHINE):
        return DEFENDER_DIES

    if attacker.rank > defender.rank:
        return DEFENDER_DIES
    if attacker.rank < defender.rank:
        return ATTACKER_DIES
    return BOTH_DIE

def combat_reward(piece: Piece, is_turret: bool = False) -> float:
    """Recompensa por capturar una pieza, normalizada a (0, 1]."""
    if is_turret:
        return 0.25
    if piece.rank == 0:
        return 0.01
    return round(math.log2(piece.rank + 1) / math.log2(MAX_RANK + 1), 3)

# =============================================================================
# MOVIMIENTO
# =============================================================================

DIRECTIONS = [(-1, 0), (1, 0), (0, -1), (0, 1)]

def is_passable(r: int, c: int) -> bool:
    if r < 0 or r >= ROWS or c < 0 or c >= COLS:
        return False
    return BOARD[r, c] != NO_PASSABLE


def can_move(piece: Piece, from_pos: tuple, to_pos: tuple, pieces: dict) -> bool:
    """Comprueba si el movimiento es legal."""
    if not piece.can_move:
        return False
    if from_pos == to_pos:
        return False
    r2, c2 = to_pos
    if not is_passable(r2, c2):
        return False
    target    = pieces.get(to_pos)
    is_attack = target is not None
    if target is not None and target.owner == piece.owner:
        return False
    if not is_attack and piece.is_oscillating(from_pos, to_pos):
        return False
    if piece.piece_type == PIECE_TYPE_SCOUT:
        return _scout_path_valid(from_pos, to_pos, pieces, piece.owner)
    r1, c1 = from_pos
    return abs(r1 - r2) + abs(c1 - c2) == 1


def _scout_path_valid(from_pos, to_pos, pieces, owner) -> bool:
    """Comprueba si el movimiento del scout es valido."""
    r1, c1 = from_pos
    r2, c2 = to_pos
    if r1 != r2 and c1 != c2:
        return False
    dr    = int(np.sign(r2 - r1))
    dc    = int(np.sign(c2 - c1))
    steps = max(abs(r2 - r1), abs(c2 - c1))
    for step in range(1, steps + 1):
        r = r1 + step * dr
        c = c1 + step * dc
        if not is_passable(r, c):
            return False
        occupant = pieces.get((r, c))
        if occupant is not None:
            if occupant.owner == owner:
                return False
            if (r, c) != to_pos:
                return False
            return True
        if (r, c) == to_pos:
            return True
    return False


def get_all_valid_moves(piece: Piece, from_pos: tuple, pieces: dict) -> list:
    valid = []
    if piece.piece_type == PIECE_TYPE_SCOUT:
        for r in range(ROWS):
            for c in range(COLS):
                to = (r, c)
                if to != from_pos and can_move(piece, from_pos, to, pieces):
                    valid.append(to)
    else:
        for dr, dc in DIRECTIONS:
            to = (from_pos[0] + dr, from_pos[1] + dc)
            if can_move(piece, from_pos, to, pieces):
                valid.append(to)
    return valid

# ============================================================================= #
#                                  ENVIROMENT                                   #
# ============================================================================= #

# =============================================================================
# CANALES DE OBSERVACION — 3
# =============================================================================
# Canal 0 – PIEZAS (float en [-1, 1])
#       +rank/MAX  → pieza del BOT              (rango > 0)
#       -rank/MAX  → pieza del PLAYER revelada  (rango > 0)
#       +0.01      → BOT con rango 0            (TURRET o ENERGY_CORE propio)
#       -0.01      → PLAYER revelado rango 0
#       -0.05      → pieza del PLAYER oculta
#        0.0       → casilla vacia o intransitable

# Canal 1 – OCUPACION / TRANSITABILIDAD (float en {-1, 0, 1})
#       -1.0 → NO_PASSABLE
#        0.0 → vacia y transitable
#       +1.0 → ocupada

# Canal 2 – MOVILIDAD BOT (float en {0, 1})
#        1.0 → pieza BOT con al menos un movimiento legal disponible
#        0.0 → resto
# =============================================================================

N_CHANNELS       = 3
HIDDEN_ENEMY_VAL = -0.05    # TODO Revisar valor
RANK0_BOT_VAL    = +0.01    # TODO Revisar valor
RANK0_PLAYER_VAL = -0.01    # TODO Revisar valor

def build_obs(pieces: dict) -> np.ndarray:
    """Observacion desde perspectiva del BOT (tablero invertido verticalmente)."""
    obs = np.zeros((N_CHANNELS, ROWS, COLS), dtype=np.float32)
    for r in range(ROWS):
        for c in range(COLS):
            if BOARD[r, c] == NO_PASSABLE:
                obs[1, r, c] = -1.0
    for (r, c), piece in pieces.items():
        obs[1, r, c] = 1.0
        if piece.owner == BOT:
            obs[0, r, c] = RANK0_BOT_VAL if piece.rank == 0 else piece.rank / MAX_RANK
        else:
            is_known = piece.revealed or piece.scout_revealed
            if is_known:
                obs[0, r, c] = RANK0_PLAYER_VAL if piece.rank == 0 else -(piece.rank / MAX_RANK)
            else:
                obs[0, r, c] = HIDDEN_ENEMY_VAL
    for (r, c), piece in pieces.items():
        if piece.owner == BOT and piece.can_move:
            if get_all_valid_moves(piece, (r, c), pieces):
                obs[2, r, c] = 1.0
    return obs

def build_obs_as_player(pieces: dict) -> np.ndarray:
    """Observacion desde perspectiva del PLAYER (tablero invertido verticalmente)."""
    obs = np.zeros((N_CHANNELS, ROWS, COLS), dtype=np.float32)
    for r in range(ROWS):
        r_flip = (ROWS - 1) - r
        for c in range(COLS):
            if BOARD[r, c] == NO_PASSABLE:
                obs[1, r_flip, c] = -1.0
    for (r, c), piece in pieces.items():
        r_flip = (ROWS - 1) - r
        obs[1, r_flip, c] = 1.0
        if piece.owner == PLAYER:
            obs[0, r_flip, c] = RANK0_BOT_VAL if piece.rank == 0 else piece.rank / MAX_RANK
        else:
            is_known = piece.revealed or piece.scout_revealed
            if is_known:
                obs[0, r_flip, c] = RANK0_PLAYER_VAL if piece.rank == 0 else -(piece.rank / MAX_RANK)
            else:
                obs[0, r_flip, c] = HIDDEN_ENEMY_VAL
    for (r, c), piece in pieces.items():
        r_flip = (ROWS - 1) - r
        if piece.owner == PLAYER and piece.can_move:
            if get_all_valid_moves(piece, (r, c), pieces):
                obs[2, r_flip, c] = 1.0
    return obs

# =============================================================================
# RECOMPENSAS
# =============================================================================

R_WIN           =  1.0      # TODO Revisar valor
R_LOSE          = -1.0      # TODO Revisar valor
R_NO_MOVES_WIN  =  1.0      # TODO Revisar valor
R_NO_MOVES_LOSE = -1.0      # TODO Revisar valor
R_MOVE          = -0.002    # TODO Revisar valor
R_TIE           =  0.0      # TODO Revisar valor
ILLEGAL_PENALTY = -1.0      # TODO Revisar valor

# =============================================================================
# ENTORNO GYM
# =============================================================================

class StrategoEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(self):
        super().__init__()
        n_tiles = ROWS * COLS  # 100

        self.action_space = spaces.Discrete(n_tiles * n_tiles)  # 10000 acciones
        self.observation_space = spaces.Box(
            low=-1.0, high=1.0,
            shape=(N_CHANNELS, ROWS, COLS),
            dtype=np.float32,
        )

        self.pieces: dict = {}
        self.turn           = 0
        self.opponent_model = None
        self.reset()

    # ---- RESET ---------------------------------------------------------------

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self.pieces = {}
        self.turn   = 0
        self._deploy_random()
        return build_obs(self.pieces), {}

    def _build_army(self) -> list:
        army = []
        for ptype, rank, movable, count in ARMY_COMPOSITION:
            for _ in range(count):
                army.append((ptype, rank, movable))
        return army

    def _deploy_random(self):
        bot_tiles    = [(r, c) for r in range(ROWS) for c in range(COLS) if BOARD[r, c] == BOT_DEPLOYMENT]
        player_tiles = [(r, c) for r in range(ROWS) for c in range(COLS) if BOARD[r, c] == PLAYER_DEPLOYMENT]
        np.random.shuffle(bot_tiles)
        np.random.shuffle(player_tiles)
        for i, (ptype, rank, movable) in enumerate(self._build_army()):
            self.pieces[bot_tiles[i]]    = Piece(ptype, rank, BOT,    movable, revealed=True)
            self.pieces[player_tiles[i]] = Piece(ptype, rank, PLAYER, movable, revealed=False)

    # ---- MASCARAS ------------------------------------------------------------

    def action_masks(self) -> np.ndarray:
        n_tiles = ROWS * COLS
        mask    = np.zeros(self.action_space.n, dtype=bool)
        for from_idx in range(n_tiles):
            from_pos = (from_idx // COLS, from_idx % COLS)
            piece    = self.pieces.get(from_pos)
            if piece is None or piece.owner != BOT:
                continue
            for to_pos in get_all_valid_moves(piece, from_pos, self.pieces):
                mask[from_idx * n_tiles + to_pos[0] * COLS + to_pos[1]] = True
        if not mask.any():
            for from_idx in range(n_tiles):
                from_pos = (from_idx // COLS, from_idx % COLS)
                piece    = self.pieces.get(from_pos)
                if piece is None or piece.owner != BOT or not piece.can_move:
                    continue
                for dr, dc in DIRECTIONS:
                    r2, c2 = from_pos[0] + dr, from_pos[1] + dc
                    if is_passable(r2, c2):
                        mask[from_idx * n_tiles + r2 * COLS + c2] = True
                        return mask
            mask[0] = True
        return mask

    # ---- STEP ----------------------------------------------------------------

    def step(self, action: int):
        reward     = 0.0
        terminated = False

        terminated, end_r = self._check_game_over()
        if terminated:
            return build_obs(self.pieces), end_r, True, False, {}

        n_tiles  = ROWS * COLS
        from_idx = int(action) // n_tiles
        to_idx   = int(action) % n_tiles
        from_pos = (from_idx // COLS, from_idx % COLS)
        to_pos   = (to_idx   // COLS, to_idx   % COLS)

        piece = self.pieces.get(from_pos)
        if piece is None or piece.owner != BOT:
            return build_obs(self.pieces), ILLEGAL_PENALTY, True, False, {}
        if not can_move(piece, from_pos, to_pos, self.pieces):
            return build_obs(self.pieces), ILLEGAL_PENALTY, True, False, {}

        bot_r, terminated = self._execute_move(BOT, from_pos, to_pos)
        reward += bot_r
        self.turn += 1
        if terminated:
            return build_obs(self.pieces), reward, True, False, {}

        player_moves = self._get_all_moves(PLAYER)
        if not player_moves:
            terminated, end_r = self._check_game_over()
            return build_obs(self.pieces), reward + end_r, True, False, {}

        pf, pt = self._choose_opponent_move(player_moves)
        p_r, p_terminated = self._execute_move(PLAYER, pf, pt)
        reward += -p_r
        self.turn += 1
        if p_terminated:
            return build_obs(self.pieces), reward, True, False, {}

        terminated, end_r = self._check_game_over()
        reward += end_r
        return build_obs(self.pieces), reward, terminated, False, {}

    # ---- EXECUTE MOVE --------------------------------------------------------

    def _execute_move(self, actor: int, from_pos: tuple, to_pos: tuple):
        piece      = self.pieces[from_pos]
        target     = self.pieces.get(to_pos)
        reward     = R_MOVE
        terminated = False

        if target is None:
            r1, c1 = from_pos
            r2, c2 = to_pos
            if piece.piece_type == PIECE_TYPE_SCOUT and abs(r1-r2) + abs(c1-c2) > 1:
                piece.scout_revealed = True  # Scout largo: identidad publica
            piece.register_move(from_pos, to_pos)
            self.pieces[to_pos] = piece
            del self.pieces[from_pos]

        else:
            result          = resolve_combat(piece, target)
            attacker_is_bot = (actor == BOT)

            if result == DEFENDER_DIES:
                if target.piece_type == PIECE_TYPE_ENERGY_CORE:
                    reward, terminated = (R_WIN if attacker_is_bot else R_LOSE), True
                else:
                    r = combat_reward(target, is_turret=(target.piece_type == PIECE_TYPE_TURRET))
                    reward = r if attacker_is_bot else -r
                piece.register_move(from_pos, to_pos)
                del self.pieces[from_pos]
                self.pieces.pop(to_pos, None)
                self.pieces[to_pos] = piece

            elif result == ATTACKER_DIES:
                if piece.piece_type == PIECE_TYPE_ENERGY_CORE:
                    reward, terminated = (R_LOSE if attacker_is_bot else R_WIN), True
                else:
                    r = combat_reward(piece, is_turret=(piece.piece_type == PIECE_TYPE_TURRET))
                    reward = -r if attacker_is_bot else r
                self.pieces.pop(from_pos, None)

            else:  # BOTH_DIE
                reward = R_TIE
                self.pieces.pop(from_pos, None)
                self.pieces.pop(to_pos, None)

        return reward, terminated

    # ---- GAME OVER -----------------------------------------------------------

    def _check_game_over(self):
        bot_has_core    = any(p.piece_type == PIECE_TYPE_ENERGY_CORE and p.owner == BOT    for p in self.pieces.values())
        player_has_core = any(p.piece_type == PIECE_TYPE_ENERGY_CORE and p.owner == PLAYER for p in self.pieces.values())
        if not bot_has_core:    return True, R_LOSE
        if not player_has_core: return True, R_WIN
        bot_moves    = bool(self._get_all_moves(BOT))
        player_moves = bool(self._get_all_moves(PLAYER))
        if not bot_moves and not player_moves: return True, R_TIE
        if not bot_moves:    return True, R_NO_MOVES_LOSE
        if not player_moves: return True, R_NO_MOVES_WIN
        return False, 0.0

    def _get_all_moves(self, owner: int) -> list:
        moves = []
        for pos, piece in list(self.pieces.items()):
            if piece.owner != owner: continue
            for to in get_all_valid_moves(piece, pos, self.pieces):
                moves.append((pos, to))
        return moves

    # ---- OPONENTE ------------------------------------------------------------

    def _choose_opponent_move(self, player_moves: list) -> tuple:
        if self.opponent_model is None:
            return player_moves[np.random.randint(len(player_moves))]
        obs_player = build_obs_as_player(self.pieces)
        mask       = self._get_opponent_masks()
        action, _  = self.opponent_model.predict(obs_player, deterministic=False, action_masks=mask)
        n_tiles  = ROWS * COLS
        from_idx = int(action) // n_tiles
        to_idx   = int(action) % n_tiles
        from_r = (ROWS - 1) - (from_idx // COLS)
        from_c = from_idx % COLS
        to_r   = (ROWS - 1) - (to_idx   // COLS)
        to_c   = to_idx % COLS
        fp, tp = (from_r, from_c), (to_r, to_c)
        p = self.pieces.get(fp)
        if p is not None and p.owner == PLAYER and can_move(p, fp, tp, self.pieces):
            return fp, tp
        return player_moves[np.random.randint(len(player_moves))]

    def _get_opponent_masks(self) -> np.ndarray:
        n_tiles = ROWS * COLS
        mask    = np.zeros(self.action_space.n, dtype=bool)
        for from_idx in range(n_tiles):
            from_r_real = (ROWS - 1) - (from_idx // COLS)
            from_c      = from_idx % COLS
            from_pos    = (from_r_real, from_c)
            piece       = self.pieces.get(from_pos)
            if piece is None or piece.owner != PLAYER: continue
            for to_pos in get_all_valid_moves(piece, from_pos, self.pieces):
                to_r_flip = (ROWS - 1) - to_pos[0]
                to_idx    = to_r_flip * COLS + to_pos[1]
                mask[from_idx * n_tiles + to_idx] = True
        if not mask.any():
            mask[0] = True
        return mask

# =============================================================================
# Politica Cnn personalizada
# =============================================================================
class StrategoCNNExtractor(BaseFeaturesExtractor):
    def __init__(self, observation_space: spaces.Box, features_dim: int = 128):
        # Asignamos la dimensión de salida que irá a las capas densas de la política
        super().__init__(observation_space, features_dim)
        
        n_input_channels = observation_space.shape[0]
        
        # Diseñamos una red pequeña y sin saltos (strides=1) para mantener el tamaño 10x10
        self.cnn = nn.Sequential(
            nn.Conv2d(n_input_channels, 32, kernel_size=3, stride=1, padding=1),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=3, stride=1, padding=1),
            nn.ReLU(),
            nn.Flatten(),
        )
        
        # Calculamos dinámicamente el tamaño de salida del aplanado (Flatten)
        with th.no_grad():
            sample_tensor = th.as_tensor(observation_space.sample()[None]).float()
            n_flatten = self.cnn(sample_tensor).shape[1]
            
        # Una capa lineal final para proyectar al espacio de características solicitado
        self.linear = nn.Sequential(
            nn.Linear(n_flatten, features_dim),
            nn.ReLU()
        )

    def forward(self, observations: th.Tensor) -> th.Tensor:
        return self.linear(self.cnn(observations))

# =============================================================================
# SELF-PLAY Y ENTRENAMIENTO
# =============================================================================

MODEL_BASE      = "ppo_neuroforge"
TOTAL_TIMESTEPS = 100_000

def find_latest_model():
    candidates = sorted(glob.glob(f"{MODEL_BASE}_v*.zip"), reverse=True)
    if candidates:
        return candidates[0].replace(".zip", "")
    if os.path.exists(f"{MODEL_BASE}.zip"):
        return MODEL_BASE
    return None


if __name__ == "__main__":
    env = StrategoEnv()

    print("Verificando entorno...")
    check_env(env, warn=False)
    print("Entorno OK.\n")

    latest = find_latest_model()
    if latest:
        print(f"Modelo encontrado: {latest}.zip")
        print("Modo: SELF-PLAY (oponente = modelo anterior)\n")
        env.opponent_model = MaskablePPO.load(latest)
    else:
        print("Sin modelo previo → primera iteracion con oponente ALEATORIO\n")

    policy_kwargs = dict(
        features_extractor_class=StrategoCNNExtractor,
        features_extractor_kwargs=dict(features_dim=128),
        normalize_images=False # Evita intentar forzar el escalado
    )

    model = MaskablePPO(
        policy        = "CnnPolicy",
        env           = env,
        policy_kwargs = policy_kwargs,
        learning_rate = 3e-4,   # TODO Revisar valor
        n_steps       = 2048,   # TODO Revisar valor    
        batch_size    = 256,    # TODO Revisar valor
        n_epochs      = 10,     # TODO Revisar valor
        gamma         = 0.99,   # TODO Revisar valor
        gae_lambda    = 0.95,   # TODO Revisar valor
        clip_range    = 0.2,    # TODO Revisar valor
        ent_coef      = 0.01,   # TODO Revisar valor
        verbose       = 1,
    )

    print(f"Iniciando entrenamiento ({TOTAL_TIMESTEPS:,} pasos)...")
    model.learn(total_timesteps=TOTAL_TIMESTEPS)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    save_name = f"{MODEL_BASE}_v{timestamp}"
    model.save(save_name)
    print(f"\nModelo guardado: {save_name}.zip")

# =============================================================================
# DIAGNOSTICO
# =============================================================================

def print_board(pieces: dict):
    print("     " + "    ".join(f"C{c}" for c in range(COLS)))
    for r in range(ROWS):
        row = f"R{r:02d}  "
        for c in range(COLS):
            if BOARD[r, c] == NO_PASSABLE:
                row += "[####]  "
            elif (r, c) in pieces:
                p   = pieces[(r, c)]
                own = "B" if p.owner == BOT else "P"
                vis = "" if (p.revealed or p.scout_revealed) else "?"
                row += f"[{own}:{p.piece_type[:4]}{vis}]  "
            else:
                row += "[    ]  "
        print(row)
    print()