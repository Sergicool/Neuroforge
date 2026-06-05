import os
import glob
import math
from datetime import datetime

import numpy as np
import gymnasium as gym
from gymnasium import spaces

from sb3_contrib import MaskablePPO
from stable_baselines3.common.env_checker import check_env

import optuna
from optuna.pruners import MedianPruner

import torch as th
import torch.nn as nn
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor

# =============================================================================
# TABLERO
# =============================================================================

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

# Dimensiones del tablero
ROWS, COLS = BOARD.shape

# =============================================================================
# PIEZAS  (fiel a PiecesData.cs)
# =============================================================================

PLAYER = 0
BOT    = 1

# Strings de tipos de piezas
ENERGY_CORE = "ENERGY_CORE"
TURRET      = "TURRET"
WAR_MACHINE = "WAR_MACHINE"
NOVA        = "NOVA"
MECHA       = "MECHA"
SENTINEL    = "SENTINEL"
CANINE      = "CANINE"
CYBORG      = "CYBORG"
SOLDIER     = "SOLDIER"
SABOTEUR    = "SABOTEUR"
SCOUT       = "SCOUT"
PHANTOM     = "PHANTOM"

ARMY = [
    (ENERGY_CORE,  0, False,  1),
    (TURRET,       0, False,  6),
    (WAR_MACHINE, 10, True,   1),
    (NOVA,         9, True,   1),
    (MECHA,        8, True,   2),
    (SENTINEL,     7, True,   3),
    (CANINE,       6, True,   4),
    (CYBORG,       5, True,   4),
    (SOLDIER,      4, True,   4),
    (SABOTEUR,     3, True,   5),
    (SCOUT,        2, True,   8),
    (PHANTOM,      1, True,   1),
]

# Total de piezas
ARMY_SIZE = sum(c for _, _, _, c in ARMY)

# =============================================================================
# CLASE PIEZA
# =============================================================================

class Piece:
    """Replica los campos relevantes de Piece.cs para el entrenamiento."""

    # FIX: Añadido 'revealed_to_player' para separar el conocimiento de cada bando.
    # Antes se usaba 'revealed_to_bot' para ambos lados en build_obs_as_player,
    # lo que era conceptualmente incorrecto y podía causar bugs con lógica asimétrica.
    __slots__ = ("piece_type", "rank", "owner", "can_move",
                 "revealed_to_bot", "revealed_to_player",
                 "_history", "_turn_history")

    def __init__(self, piece_type: str, rank: int, owner: int, can_move: bool):
        self.piece_type       = piece_type
        self.rank             = rank
        self.owner            = owner
        self.can_move         = can_move
        # Las piezas del BOT son siempre conocidas por el BOT.
        # Las piezas del PLAYER son siempre conocidas por el PLAYER.
        self.revealed_to_bot    = (owner == BOT)
        self.revealed_to_player = (owner == PLAYER)
        self._history: list     = []
        self._turn_history: list = []

    def register_move(self, from_pos: tuple, to_pos: tuple, current_turn: int):
        self._history.append((from_pos, to_pos))
        self._turn_history.append(current_turn)
        if len(self._history) > 3:
            self._history.pop(0)
            self._turn_history.pop(0)

    def is_oscillating(self, from_pos: tuple, to_pos: tuple, current_turn: int) -> bool:
        if len(self._history) < 3:
            return False
        
        # Verificar si los movimientos fueron en turnos consecutivos
        if self._turn_history[-1] != current_turn - 1 or self._turn_history[-2] != current_turn - 2:
            return False

        h = self._history
        a0, b0 = h[0][0], h[0][1]
        return (
            h[0] == (a0, b0) and
            h[1] == (b0, a0) and
            h[2] == (a0, b0) and
            from_pos == b0 and to_pos == a0
        )

# =============================================================================
# COMBATE
# =============================================================================

ATTACKER_DIES = 0
DEFENDER_DIES = 1
BOTH_DIE      = 2

def resolve_combat(attacker: Piece, defender: Piece) -> int:
    # TURRET pierde solo contra SABOTEUR
    if defender.piece_type == TURRET:
        return DEFENDER_DIES if attacker.piece_type == SABOTEUR else ATTACKER_DIES

    # PHANTOM gana si ataca a WAR_MACHINE
    if attacker.piece_type == PHANTOM and defender.piece_type == WAR_MACHINE:
        return DEFENDER_DIES

    # Resolucion por rango
    if attacker.rank > defender.rank: return DEFENDER_DIES
    if attacker.rank < defender.rank: return ATTACKER_DIES

    # Empate
    return BOTH_DIE

# =============================================================================
# MOVIMIENTO
# =============================================================================

DIRS = [(-1, 0), (1, 0), (0, -1), (0, 1)]

def is_passable(row: int, col: int) -> bool:
    return 0 <= row < ROWS and 0 <= col < COLS and BOARD[row, col] != NP

def can_move(piece: Piece, from_pos: tuple, to_pos: tuple, pieces: dict, current_turn: int) -> bool:
    # Mismo origen-destino
    if not piece.can_move or from_pos == to_pos:
        return False
    
    r2, c2 = to_pos
    # No es transitable
    if not is_passable(r2, c2):
        return False
    
    target = pieces.get(to_pos)
    # No hay una pieza aliada
    if target is not None and target.owner == piece.owner:
        return False
    
    # Anti-oscilación (Incluyendo ataques)
    if piece.is_oscillating(from_pos, to_pos, current_turn):
        return False
    
    # Movimiento de scout
    if piece.piece_type == SCOUT:
        return _scout_valid(from_pos, to_pos, pieces, piece.owner)
    
    r1, c1 = from_pos
    # Movimiento ortogonal
    return abs(r1 - r2) + abs(c1 - c2) == 1

def _scout_valid(from_pos, to_pos, pieces, owner) -> bool:
    r1, c1 = from_pos
    r2, c2 = to_pos
    
    # Movimiento en linea recta obligatorio
    if r1 != r2 and c1 != c2:
        return False
    
    dr = int(np.sign(r2 - r1))
    dc = int(np.sign(c2 - c1))
    
    dist = abs(r2 - r1) + abs(c2 - c1)
    r, c = r1 + dr, c1 + dc
    
    for _ in range(dist):
        if not is_passable(r, c):
            return False
            
        occ = pieces.get((r, c))
        if occ is not None:
            if occ.owner == owner:   
                return False   # Aliado bloquea el camino o el destino
            if (r, c) != to_pos:     
                return False   # Enemigo en medio
            return True        # Enemigo en destino
            
        if (r, c) == to_pos:
            return True        # Destino libre
            
        r += dr
        c += dc

    return False

def valid_moves(piece: Piece, pos: tuple, pieces: dict, current_turn: int) -> list:
    moves = []
    if piece.piece_type == SCOUT:
        for r in range(ROWS):
            for c in range(COLS):
                to = (r, c)
                if to != pos and can_move(piece, pos, to, pieces, current_turn):
                    moves.append(to)
    else:
        for dr, dc in DIRS:
            to = (pos[0] + dr, pos[1] + dc)
            if can_move(piece, pos, to, pieces, current_turn):
                moves.append(to) 
    return moves

# =============================================================================
# ESPACIOS DE OBSERVACIÓN — 3 canales:
# 
# Canal 0: Identidad y Rango de piezas
#    0.0    -> casilla vacía o pieza desconocida. (El canal 1 deberia solventar la confusión)
#   [1-10]  -> rango de la pieza del BOT
#    11     -> TURRET del BOT
#    12     -> ENERGY_CORE del BOT
#  [-1,-10] -> rango de la pieza del PLAYER revelada
#   -11     -> TURRET del PLAYER revelada
#   -12     -> ENERGY_CORE del PLAYER revelada
# 
# Canal 1: Transitabilidad
#   -1.0     -> NO_PASSABLE
#    0.0     -> casilla transitable vacía
#    0.5     -> casilla ocupada por pieza ENEMIGA OCULTA
#    1.0     -> casilla ocupada por pieza CONOCIDA
# 
# Canal 2: Movilidad del BOT
#    1.0     -> Pieza del BOT con al menos un movimiento legal en el turno
#    0.0     -> Resto
# =============================================================================

# Numero de canales
N_CH = 3

# Valores para piezas especiales
_OBS_TURRET = 11.0
_OBS_ENERGY = 12.0

# Factor de normalización para acotar el Canal 0 en [-1.0, 1.0]
_NORM = 12.0

def _piece_obs_value(piece: Piece, known: bool) -> float:
    # Pieza del BOT
    if piece.owner == BOT:
        if piece.piece_type == TURRET:      return  _OBS_TURRET / _NORM
        if piece.piece_type == ENERGY_CORE: return  _OBS_ENERGY / _NORM
        return piece.rank / _NORM
    
    # Pieza desconocida del PLAYER
    if not known:
        return 0.0 
    
    # Pieza conocida del PLAYER
    if piece.piece_type == TURRET:      return -_OBS_TURRET / _NORM
    if piece.piece_type == ENERGY_CORE: return -_OBS_ENERGY / _NORM
    return -(piece.rank / _NORM)

# Genera el campo de observación para el BOT
def build_obs(pieces: dict, current_turn: int) -> np.ndarray:
    obs = np.zeros((N_CH, ROWS, COLS), dtype=np.float32)

    # Casillas: Canal 1
    for r in range(ROWS):
        for c in range(COLS):
            if BOARD[r, c] == NP:
                obs[1, r, c] = -1.0

    # Piezas
    for (r, c), piece in pieces.items():
        # Canal 0
        obs[0, r, c] = _piece_obs_value(piece, piece.revealed_to_bot)
        
        # Canal 1
        if piece.owner == PLAYER and not piece.revealed_to_bot:
            obs[1, r, c] = 0.5  # "Sé que hay un enemigo, pero no sé qué es"
        else:
            obs[1, r, c] = 1.0  # Pieza conocida (propia o rival descubierta)
            
        # Canal 2
        if piece.owner == BOT and piece.can_move and valid_moves(piece, (r, c), pieces, current_turn):
            obs[2, r, c] = 1.0

    return obs

# Genera el campo de observación desde la perspectiva del PLAYER.
# El tablero se voltea verticalmente para que el PLAYER siempre vea su zona en las filas 0-1.
def build_obs_as_player(pieces: dict, current_turn: int) -> np.ndarray:
    obs = np.zeros((N_CH, ROWS, COLS), dtype=np.float32)

    # Canal 1: casillas no transitables (con flip de perspectiva)
    for r in range(ROWS):
        rf = (ROWS - 1) - r
        for c in range(COLS):
            if BOARD[r, c] == NP:
                obs[1, rf, c] = -1.0

    # Piezas (valores invertidos respecto a los que ve el BOT)
    for (r, c), piece in pieces.items():
        rf = (ROWS - 1) - r

        if piece.owner == PLAYER:
            # Las piezas propias del PLAYER siempre son conocidas por él
            if piece.piece_type == TURRET:        obs[0, rf, c] =  _OBS_TURRET / _NORM
            elif piece.piece_type == ENERGY_CORE: obs[0, rf, c] =  _OBS_ENERGY / _NORM
            else:                                 obs[0, rf, c] =  piece.rank / _NORM
            obs[1, rf, c] = 1.0
            
            # Canal 2: piezas del PLAYER con movimientos legales
            if piece.can_move and valid_moves(piece, (r, c), pieces, current_turn):
                obs[2, rf, c] = 1.0

        else:
            # FIX: Usamos revealed_to_player para saber qué piezas del BOT
            # ha descubierto el PLAYER (antes se usaba revealed_to_bot por error).
            if piece.revealed_to_player:
                if piece.piece_type == TURRET:        obs[0, rf, c] = -_OBS_TURRET / _NORM
                elif piece.piece_type == ENERGY_CORE: obs[0, rf, c] = -_OBS_ENERGY / _NORM
                else:                                 obs[0, rf, c] = -(piece.rank / _NORM)
                obs[1, rf, c] = 1.0
            else:
                obs[0, rf, c] = 0.0
                obs[1, rf, c] = 0.5  # "Hay un enemigo aquí, pero no sé qué es"

    return obs

# =============================================================================
# RECOMPENSAS
# =============================================================================

R_WIN            =  10.0
R_LOSE           = -10.0
R_TIE            =   0.0
R_NO_MOVES_WIN   =  10.0
R_NO_MOVES_LOSE  = -10.0
 
# Penalización pequeña por cada paso sin hacer nada relevante.
# Fuerza al BOT a avanzar en lugar de quedarse quieto.
R_STEP_PENALTY = -0.005

# Límite de turnos antes de forzar empate (evita partidas infinitas)
MAX_TURNS = 800

def _combat_reward(piece: Piece) -> float:
    # Recompensa proporcional al rango o tipo de la pieza capturada.
    if piece.piece_type == ENERGY_CORE: return 0.0 # Gestionado por R_WIN/R_LOSE
    if piece.piece_type == TURRET:      return 0.3
    return round(math.log2(piece.rank + 1) / math.log2(11), 3)

# =============================================================================
# ENTORNO GYM
# =============================================================================

class NeuroForgeEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(self):
        super().__init__()
        n = ROWS * COLS
        self.action_space      = spaces.Discrete(n * n)
        self.observation_space = spaces.Box(
            low=-1.0, high=1.0,
            shape=(N_CH, ROWS, COLS),
            dtype=np.float32,
        )
        self.pieces: dict       = {}
        self.turn: int          = 0
        self.opponent_model     = None
        self.reset()
 
    # ------------------------------------------------------------------
    # RESET
    # ------------------------------------------------------------------
 
    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self.pieces = {}
        self.turn   = 0
        self._deploy()
        return build_obs(self.pieces, self.turn), {}
 
    def _army_list(self):
        army = []
        for ptype, rank, movable, count in ARMY:
            for _ in range(count):
                army.append((ptype, rank, movable))
        return army
 
    def _deploy(self):
        bot_tiles    = [(r, c) for r in range(ROWS) for c in range(COLS) if BOARD[r, c] == BD]
        player_tiles = [(r, c) for r in range(ROWS) for c in range(COLS) if BOARD[r, c] == PD]
        np.random.shuffle(bot_tiles)
        np.random.shuffle(player_tiles)
        for i, (ptype, rank, movable) in enumerate(self._army_list()):
            self.pieces[bot_tiles[i]]    = Piece(ptype, rank, BOT,    movable)
            self.pieces[player_tiles[i]] = Piece(ptype, rank, PLAYER, movable)
 
    # ------------------------------------------------------------------
    # MÁSCARAS DE ACCIÓN
    # ------------------------------------------------------------------
 
    def action_masks(self) -> np.ndarray:
        n = ROWS * COLS
        mask = np.zeros(self.action_space.n, dtype=bool)
        for fi in range(n):
            fp = (fi // COLS, fi % COLS)
            piece = self.pieces.get(fp)
            if piece is None or piece.owner != BOT or not piece.can_move:
                continue
            for tp in valid_moves(piece, fp, self.pieces, self.turn):
                mask[fi * n + tp[0] * COLS + tp[1]] = True

        return mask
 
    # ------------------------------------------------------------------
    # STEP
    # ------------------------------------------------------------------
 
    def step(self, action: int):
        n = ROWS * COLS
        fi = int(action) // n
        ti = int(action) % n
        fp = (fi // COLS, fi % COLS)
        tp = (ti // COLS, ti % COLS)
 
        # Control para check_env (evita KeyErrors si se ejecutan acciones no enmascaradas)
        piece = self.pieces.get(fp)
        if piece is None or piece.owner != BOT or not can_move(piece, fp, tp, self.pieces, self.turn):
            return build_obs(self.pieces, self.turn), -5.0, True, False, {}

        # 1. Ejecutar turno del BOT
        reward, done = self._execute(BOT, fp, tp)
        self.turn += 1
        if done:
            return build_obs(self.pieces, self.turn), reward, True, False, {}
 
        # 2. Comprobar estado del juego tras movimiento del BOT
        ended, end_r = self._check_end()
        if ended:
            return build_obs(self.pieces, self.turn), reward + end_r, True, False, {}
 
        # 3. Turno del PLAYER (Oponente)
        player_moves = self._all_moves(PLAYER)
        if not player_moves:
            return build_obs(self.pieces, self.turn), reward + R_NO_MOVES_WIN, True, False, {}
 
        pf, pt = self._choose_opponent(player_moves)
        p_reward, p_done = self._execute(PLAYER, pf, pt)
        
        # El reward del PLAYER se invierte para el BOT.
        # Nota: si ambos capturan piezas en el mismo step, los rewards parciales
        # se compensan. Esto es intencional: el BOT recibe señal neta del intercambio.
        reward += -p_reward
        self.turn += 1
        if p_done:
            return build_obs(self.pieces, self.turn), reward, True, False, {}
 
        # 4. Comprobación final
        ended, end_r = self._check_end()

        # Penalización por paso + límite de turnos
        if self.turn >= MAX_TURNS:
            return build_obs(self.pieces, self.turn), reward + R_TIE, True, False, {}
        
        return build_obs(self.pieces, self.turn), reward + end_r + R_STEP_PENALTY, ended, False, {}
 
    # ------------------------------------------------------------------
    # EJECUCIÓN DE MOVIMIENTO
    # ------------------------------------------------------------------
 
    def _execute(self, actor: int, fp: tuple, tp: tuple):
        piece  = self.pieces[fp]
        target = self.pieces.get(tp)
 
        if target is None:
            # Movimiento simple
            r1, c1 = fp; r2, c2 = tp
            if piece.piece_type == SCOUT and abs(r1-r2)+abs(c1-c2) > 1:
                # El Scout revela su identidad al moverse largas distancias
                piece.revealed_to_bot    = True
                piece.revealed_to_player = True
            piece.register_move(fp, tp, self.turn)
            self.pieces[tp] = piece
            del self.pieces[fp]
            return 0.0, False
 
        # Combate: ambas piezas quedan reveladas para los dos bandos
        result = resolve_combat(piece, target)
        piece.revealed_to_bot    = True
        piece.revealed_to_player = True
        target.revealed_to_bot   = True
        target.revealed_to_player = True
 
        if result == DEFENDER_DIES:
            if target.piece_type == ENERGY_CORE:
                del self.pieces[fp]; del self.pieces[tp]
                return (R_WIN if target.owner == PLAYER else R_LOSE), True
            
            r = _combat_reward(target)
            reward = r if target.owner == PLAYER else -r
            del self.pieces[tp]
            piece.register_move(fp, tp, self.turn)
            self.pieces[tp] = piece
            del self.pieces[fp]
            return reward, False
 
        if result == ATTACKER_DIES:
            # Nota: ENERGY_CORE tiene can_move=False, así que nunca debería ser atacante.
            # Esta rama es defensiva por si la lógica cambia en el futuro.
            if piece.piece_type == ENERGY_CORE:
                del self.pieces[fp]
                return (R_LOSE if piece.owner == BOT else R_WIN), True
            
            r = _combat_reward(piece)
            reward = -r if piece.owner == BOT else r
            del self.pieces[fp]
            return reward, False
 
        # BOTH_DIE
        r_att = _combat_reward(piece)
        r_def = _combat_reward(target)
        reward = (r_def - r_att) if piece.owner == BOT else (r_att - r_def)
        del self.pieces[fp]; del self.pieces[tp]
        return reward, False
 
    # ------------------------------------------------------------------
    # FIN DE PARTIDA
    # ------------------------------------------------------------------
 
    def _check_end(self):
        bot_core    = any(p.piece_type == ENERGY_CORE and p.owner == BOT    for p in self.pieces.values())
        player_core = any(p.piece_type == ENERGY_CORE and p.owner == PLAYER for p in self.pieces.values())
        
        if not bot_core:    return True, R_LOSE
        if not player_core: return True, R_WIN
        
        bm = bool(self._all_moves(BOT))
        pm = bool(self._all_moves(PLAYER))
        
        if not bm and not pm: return True, R_TIE
        if not bm:  return True, R_NO_MOVES_LOSE
        if not pm:  return True, R_NO_MOVES_WIN
        return False, 0.0
 
    def _all_moves(self, owner: int) -> list:
        moves = []
        for pos, piece in list(self.pieces.items()):
            if piece.owner != owner: continue
            for to in valid_moves(piece, pos, self.pieces, self.turn):
                moves.append((pos, to))
        return moves
 
    # ------------------------------------------------------------------
    # OPONENTE
    # ------------------------------------------------------------------
 
    def _choose_opponent(self, player_moves: list) -> tuple:
        if self.opponent_model is None:
            return player_moves[np.random.randint(len(player_moves))]
 
        obs  = build_obs_as_player(self.pieces, self.turn)
        mask = self._opponent_masks()
        
        action, _ = self.opponent_model.predict(obs, deterministic=False, action_masks=mask)
 
        n = ROWS * COLS
        fi = int(action) // n
        ti = int(action) % n
        
        # Corregimos el flip de perspectiva
        fp = ((ROWS - 1) - (fi // COLS), fi % COLS)
        tp = ((ROWS - 1) - (ti // COLS), ti % COLS)
        
        p = self.pieces.get(fp)
        if p is not None and p.owner == PLAYER and can_move(p, fp, tp, self.pieces, self.turn):
            return fp, tp
            
        return player_moves[np.random.randint(len(player_moves))]
 
    def _opponent_masks(self) -> np.ndarray:
        n = ROWS * COLS
        mask = np.zeros(self.action_space.n, dtype=bool)
        
        for fi in range(n):
            r_player = fi // COLS
            c_player = fi % COLS
            r_real = (ROWS - 1) - r_player
            c_real = c_player
            
            piece = self.pieces.get((r_real, c_real))
            if piece is None or piece.owner != PLAYER: 
                continue
                
            for tp_real in valid_moves(piece, (r_real, c_real), self.pieces, self.turn):
                tp_r_player = (ROWS - 1) - tp_real[0]
                tp_c_player = tp_real[1]
                ti = tp_r_player * COLS + tp_c_player
                mask[fi * n + ti] = True
                
        return mask

# =============================================================================
# CNN PERSONALIZADA
# =============================================================================

class CustomCNN(BaseFeaturesExtractor):
    def __init__(self, observation_space: spaces.Box, features_dim: int = 128):
        super().__init__(observation_space, features_dim)
        in_ch = observation_space.shape[0]

        self.cnn = nn.Sequential(
            nn.Conv2d(in_ch, 32, kernel_size=3, stride=1, padding=1), nn.ReLU(),
            nn.Conv2d(32,    64, kernel_size=3, stride=1, padding=1), nn.ReLU(),
            nn.Flatten(),
        )
        with th.no_grad():
            sample = th.as_tensor(observation_space.sample()[None]).float()
            n_flat = self.cnn(sample).shape[1]

        self.linear = nn.Sequential(
            nn.Linear(n_flat, features_dim),
            nn.ReLU(),
        )

    def forward(self, observations: th.Tensor) -> th.Tensor:
        return self.linear(self.cnn(observations))

# =============================================================================
# ENTRENAMIENTO
# =============================================================================

MODEL_BASE = "neuroforge_bot"

def find_latest():
    candidates = sorted(glob.glob(f"{MODEL_BASE}_v*.zip"), reverse=True)
    if candidates:
        return candidates[0].replace(".zip", "")
    if os.path.exists(f"{MODEL_BASE}.zip"):
        return MODEL_BASE
    return None

def find_recent_models(n=5):
    candidates = sorted(glob.glob(f"{MODEL_BASE}_v*.zip"), reverse=True)
    return [c.replace(".zip", "") for c in candidates[:n]]

# ------------------------------------------------------------------
# MODO 1: BÚSQUEDA DE HIPERPARÁMETROS (ejecutar una vez al principio)
# ------------------------------------------------------------------

def objective(trial):
    # Sugiere hiperparámetros para probar en esta iteración (trial)
    params = {
        "learning_rate": trial.suggest_float("learning_rate", 1e-5, 1e-3, log=True),
        "n_steps":       trial.suggest_categorical("n_steps", [512, 1024, 2048]),
        "batch_size":    trial.suggest_categorical("batch_size", [128, 256]),
        "n_epochs":      trial.suggest_int("n_epochs", 3, 10),
        "ent_coef":      trial.suggest_float("ent_coef", 0.01, 0.15),
        "gamma":         trial.suggest_float("gamma", 0.93, 0.999),
        "clip_range":    trial.suggest_float("clip_range", 0.1, 0.3),
    }

    # Descarta combinaciones inválidas
    if params["batch_size"] >= params["n_steps"]:
        raise optuna.exceptions.TrialPruned()

    # Inicializa el entorno y carga el último modelo guardado como rival (Self-Play)
    env = NeuroForgeEnv()
    latest = find_latest()
    if latest:
        env.opponent_model = MaskablePPO.load(latest)

    # Instancia el agente PPO combinando la CNN personalizada y los parámetros sugeridos
    model = MaskablePPO(
        policy        = "CnnPolicy",
        env           = env,
        policy_kwargs = dict(
            features_extractor_class=CustomCNN,
            features_extractor_kwargs=dict(features_dim=128),
            normalize_images=False,
        ),
        verbose=0,
        **params,
    )

    # Entrena el agente de forma silenciosa durante el número de pasos asignado
    model.learn(total_timesteps=200_000)

    # Control de seguridad: penaliza si el entrenamiento terminó sin completar episodios
    n_eps = len(model.ep_info_buffer)
    if n_eps < 20:
        return float("-inf")

    # Retorna el reward medio de los últimos episodios; Optuna buscará maximizar este valor
    victorias = sum(1 for ep in model.ep_info_buffer if ep["r"] > 5.0)
    return victorias / n_eps


def run_search(n_trials=30):
    # Configura el estudio en SQLite: busca maximizar y usa un pruner para cortar trials malos
    study = optuna.create_study(
        direction    = "maximize",
        pruner       = MedianPruner(n_startup_trials=5, n_warmup_steps=5_000),
        storage      = "sqlite:///neuroforge_optuna.db",
        study_name   = "neuroforge",
        load_if_exists = True,
    )
    
    # Lanza la optimización ejecutando la función 'objective' el número de veces indicado
    study.optimize(objective, n_trials=n_trials, show_progress_bar=True)

    # Muestra en consola la mejor combinación ganadora encontrada
    print("\nMejores parametros:")
    for k, v in study.best_params.items():
        print(f"  {k}: {v}")

    return study.best_params

# ------------------------------------------------------------------
# MODO 2: ENTRENAMIENTO REAL con los mejores parámetros encontrados
# ------------------------------------------------------------------

def run_training(params: dict, total_timesteps=500_000):
    env = NeuroForgeEnv()

    # Mezcla oponentes recientes para romper ciclos de self-play
    recent = find_recent_models(n=5)
    if recent:
        chosen = recent[np.random.randint(len(recent))]
        print(f"Oponente: {chosen}.zip")
        env.opponent_model = MaskablePPO.load(chosen)
    else:
        print("Sin modelo previo → oponente ALEATORIO")

    model = MaskablePPO(
        policy        = "CnnPolicy",
        env           = env,
        policy_kwargs = dict(
            features_extractor_class=CustomCNN,
            features_extractor_kwargs=dict(features_dim=128),
            normalize_images=False,
        ),
        verbose          = 1,
        tensorboard_log  = "./logs/",
        **params,
    )

    print(f"\nEntrenando {total_timesteps:,} pasos...")
    model.learn(total_timesteps=total_timesteps)

    ts   = datetime.now().strftime("%Y%m%d_%H%M%S")
    name = f"{MODEL_BASE}_v{ts}"
    model.save(name)
    print(f"Guardado: {name}.zip")


# ------------------------------------------------------------------
# PUNTO DE ENTRADA — cambia MODE según lo que quieras hacer
# ------------------------------------------------------------------

# "search"  → busca hiperparámetros óptimos (primera vez)
# "train"   → entrena con los parámetros que de BEST_PARAMS
MODE = "train"

# Cambiar con los resultados de "search" antes de cambiar a "train"
BEST_PARAMS = {
    "learning_rate": 9.87e-05,
    "n_steps":       512,
    "batch_size":    128,
    "n_epochs":      5,
    "ent_coef":      0.032,
    "gamma":         0.9585,
    "clip_range":    0.197,
}

def test_random_episode():
    env = NeuroForgeEnv()
    obs, _ = env.reset()
    total_reward = 0
    steps = 0
    done = False
    
    while not done:
        mask = env.action_masks()
        valid = np.where(mask)[0]
        action = np.random.choice(valid)
        obs, reward, done, truncated, info = env.step(action)
        total_reward += reward
        steps += 1
    
    print(f"Episodio terminado en {steps} pasos | Reward total: {total_reward:.3f}")

if __name__ == "__main__":
    th.distributions.Distribution.set_default_validate_args(False)
    env = NeuroForgeEnv()
    print("Verificando entorno...")
    check_env(env, warn=False)
    print("OK\n")

    if MODE == "search":
        best = run_search(n_trials=15)
        print("\nPoner estos valores en BEST_PARAMS y cambiar MODE a 'train'")

    elif MODE == "train":
        run_training(BEST_PARAMS, total_timesteps=1_000_000)

    # Ejecutar varios para ver varianza
    # for i in range(10):
    #     test_random_episode()

# Ver gráficas:      tensorboard --logdir=./logs/
# Ver búsqueda:      optuna-dashboard sqlite:///neuroforge_optuna.db

# (.venv) PS D:\Personal\Ingenieria Informatica\TFG\Neuroforge\Bot enviroment> python3 .\StrategoEnv.py
# Verificando entorno...
# OK

# [I 2026-06-04 10:03:06,250] A new study created in RDB with name: neuroforge
# [I 2026-06-04 10:42:32,767] Trial 0 finished with value: 0.38 and parameters: {'learning_rate': 1.967022427507304e-05, 'n_steps': 1024, 'batch_size': 128, 'n_epochs': 6, 'ent_coef': 0.054876540541665776, 'gamma': 0.9361063756299983, 'clip_range': 0.18047982353484598}. Best is trial 1 with value: 0.41.
# [I 2026-06-04 11:19:07,181] Trial 3 finished with value: 0.4 and parameters: {'learning_rate': 0.00014556590930535267, 'n_steps': 512, 'batch_size': 256, 'n_epochs': 4, 'ent_coef': 0.08196827126110513, 'gamma': 0.965706422942382, 'clip_range': 0.13498864952570414}. Best is trial 1 with value: 0.41.
# [I 2026-06-04 11:55:01,908] Trial 5 finished with value: 0.42 and parameters: {'learning_rate': 0.0007601511753290967, 'n_steps': 512, 'batch_size': 256, 'n_epochs': 3, 'ent_coef': 0.03137591840031607, 'gamma': 0.9726045784535009, 'clip_range': 0.2951216983129995}. Best is trial 5 with value: 0.42.
# [I 2026-06-04 12:43:58,072] Trial 6 finished with value: 0.41 and parameters: {'learning_rate': 1.9896463667793014e-05, 'n_steps': 512, 'batch_size': 256, 'n_epochs': 8, 'ent_coef': 0.11390836380704479, 'gamma': 0.9361641117031282, 'clip_range': 0.14351950580918824}. Best is trial 5 with value: 0.42.
# [I 2026-06-04 13:24:19,738] Trial 9 finished with value: 0.42 and parameters: {'learning_rate': 6.618138336241744e-05, 'n_steps': 2048, 'batch_size': 256, 'n_epochs': 5, 'ent_coef': 0.08595392336559181, 'gamma': 0.9739649819784092, 'clip_range': 0.1742394399803218}. Best is trial 5 with value: 0.42.
# [I 2026-06-04 13:53:23,808] Trial 10 finished with value: 0.23 and parameters: {'learning_rate': 3.923518071232497e-05, 'n_steps': 512, 'batch_size': 256, 'n_epochs': 3, 'ent_coef': 0.09506023719146738, 'gamma': 0.9750207788622351, 'clip_range': 0.11491775487784711}. Best is trial 5 with value: 0.42.
# [I 2026-06-04 14:20:21,281] Trial 12 finished with value: 0.37 and parameters: {'learning_rate': 0.00021358968086722678, 'n_steps': 2048, 'batch_size': 128, 'n_epochs': 3, 'ent_coef': 0.13907002810262167, 'gamma': 0.9943079050607497, 'clip_range': 0.2997251372372041}. Best is trial 5 with value: 0.42.
# [I 2026-06-04 14:57:51,362] Trial 14 finished with value: 0.33 and parameters: {'learning_rate': 6.882553360663938e-05, 'n_steps': 2048, 'batch_size': 256, 'n_epochs': 5, 'ent_coef': 0.010235458554293267, 'gamma': 0.9968003527912013, 'clip_range': 0.24343840799263725}. Best is trial 5 with value: 0.42.
# [I 2026-06-04 15:42:53,984] Trial 16 finished with value: 0.39 and parameters: {'learning_rate': 0.0004566997491930216, 'n_steps': 1024, 'batch_size': 256, 'n_epochs': 8, 'ent_coef': 0.05473415012938855, 'gamma': 0.984245335899831, 'clip_range': 0.2591786726655145}. Best is trial 15 with value: 0.45.
# [I 2026-06-04 16:12:09,411] Trial 19 finished with value: 0.46 and parameters: {'learning_rate': 8.242302717132607e-05, 'n_steps': 512, 'batch_size': 128, 'n_epochs': 4, 'ent_coef': 0.03988569597120354, 'gamma': 0.956923243372468, 'clip_range': 0.23965717151685384}. Best is trial 19 with value: 0.46.
# (.venv) PS D:\Personal\Ingenieria Informatica\TFG\Neuroforge\Bot enviroment> python3 .\StrategoEnv.py
# Verificando entorno...
# OK

# Mejores parametros:
#   learning_rate: 9.87213749402318e-05
#   n_steps: 512
#   batch_size: 128
#   n_epochs: 5
#   ent_coef: 0.03205739602247292
#   gamma: 0.958507367819306
#   clip_range: 0.19693260042319186