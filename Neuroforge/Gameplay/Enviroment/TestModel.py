from sb3_contrib import MaskablePPO
from sb3_contrib.common.maskable.utils import get_action_masks
from StrategoEnv import (
    StrategoEnv, Piece, can_move, random_reset,
    ROWS, COLS, DIRECTIONS, DIR_NAMES, PIECE_NAMES,
    BOT, PLAYER, NO_PASSABLE, BOT_DEPLOYMENT, PLAYER_DEPLOYMENT,
    RANK_ENERGY_CORE, RANK_TURRET, RANK_SCOUT, RANK_SABOTEUR,
    resolve_combat, ATTACKER_DIES, DEFENDER_DIES, BOTH_DIE,
)
import numpy as np

# ==============================================================================
# UTILIDADES
# ==============================================================================

def print_board(pieces, board_layout, last_move=None):
    """
    Imprime el tablero en ASCII.
    last_move: ((r1,c1), (r2,c2)) para resaltar con * la casilla de destino.
    """
    symbols = {}
    for (r, c), p in pieces.items():
        name = PIECE_NAMES.get(p.rank, f"r{p.rank}")[:4]
        if p.owner == BOT:
            sym = f"B:{name}"
        else:
            sym = "P:????" if not p.revealed else f"P:{name}"
        symbols[(r, c)] = sym

    highlight = last_move[1] if last_move else None

    print("      " + "    ".join(f"C{c}" for c in range(COLS)))
    for r in range(ROWS):
        row = f"  R{r}  "
        for c in range(COLS):
            marker = "*" if (r, c) == highlight else " "
            if board_layout[r, c] == NO_PASSABLE:
                row += f"[####]{marker} "
            elif (r, c) in symbols:
                row += f"[{symbols[(r,c)]}]{marker} "
            else:
                row += f"[    ]{marker} "
        print(row)
    print()


def decode_action(action, pieces, board_layout, pieces_state, turn):
    """Decodifica una accion numerica y devuelve info legible."""
    n_tiles  = ROWS * COLS
    from_idx = int(action) // n_tiles
    to_idx   = int(action) % n_tiles
    from_pos = (from_idx // COLS, from_idx % COLS)
    to_pos   = (to_idx   // COLS, to_idx   % COLS)

    piece = pieces.get(from_pos)
    if piece and piece.owner == BOT:
        name   = PIECE_NAMES.get(piece.rank, f"rank{piece.rank}")
        valid  = can_move(piece, from_pos, to_pos, board_layout, pieces_state, turn)
        target = pieces.get(to_pos)
        act    = "ATACA" if target else "MUEVE"
        return from_pos, to_pos, name, act, valid
    else:
        return from_pos, to_pos, "???", "ILEGAL", False


# ==============================================================================
# ENTORNO EXTENDIDO CON LOG DE MOVIMIENTOS DEL JUGADOR
# ==============================================================================

class StrategoEnvVerbose(StrategoEnv):
    """
    Igual que StrategoEnv pero guarda un log de lo que hace el jugador
    en cada step para que podamos imprimirlo.
    """
    def __init__(self):
        super().__init__()
        self.player_log = []  # Lista de strings describiendo el turno del jugador

    def reset(self, seed=None, options=None):
        self.player_log = []
        return super().reset(seed=seed, options=options)

    def step(self, action: int):
        self.player_log = []

        # Reproducir la logica de step() pero capturando el turno del jugador
        reward     = 0.0
        terminated = False
        truncated  = False

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
                self.player_log.append("JUGADOR no tiene movimientos posibles.")
            else:
                pf, pt = player_moves[np.random.randint(len(player_moves))]
                p_piece  = self.pieces[pf]
                p_target = self.pieces.get(pt)
                p_name   = PIECE_NAMES.get(p_piece.rank, f"rank{p_piece.rank}")
                p_act    = "ATACA" if p_target else "MUEVE"

                # Guardar resultado antes de ejecutar para el log
                from StrategoEnv import resolve_combat as _resolve
                _result = _resolve(self.pieces[pf], p_target) if p_target else None

                p_reward, p_terminated = self._execute_move(PLAYER, pf, pt)

                # Describir lo que hizo usando el resultado del combate directamente
                if p_act == "ATACA" and p_target:
                    t_name  = PIECE_NAMES.get(p_target.rank, f"rank{p_target.rank}")
                    if _result == DEFENDER_DIES:
                        resultado = f"elimina {t_name} del BOT"
                    elif _result == ATTACKER_DIES:
                        resultado = f"pierde contra {t_name} del BOT"
                    else:
                        resultado = f"empate con {t_name} del BOT (ambos mueren)"
                    self.player_log.append(
                        f"JUGADOR {p_act} {p_name} {pf} -> {pt} | {resultado}"
                    )
                else:
                    self.player_log.append(
                        f"JUGADOR {p_act} {p_name} {pf} -> {pt}"
                    )

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


# ==============================================================================
# FUNCIONES DE TEST
# ==============================================================================

def play_game_verbose(model, env, label="", max_steps=60):
    """Juega una partida completa mostrando todos los movimientos."""
    obs, _ = env.reset()
    done   = False
    step   = 0

    print(f"\n{'='*55}")
    print(f"  {label}")
    print(f"{'='*55}")
    print("\nEstado inicial:")
    print_board(env.pieces, env.board_layout)

    while not done and step < max_steps:
        masks = env.action_masks()
        action, _ = model.predict(obs, deterministic=True, action_masks=masks)

        # Info del movimiento del BOT antes de ejecutar
        from_pos, to_pos, name, act, valid = decode_action(
            action, env.pieces, env.board_layout, env.pieces, env.turn
        )

        # Capturar target antes de ejecutar (puede desaparecer)
        target_before = env.pieces.get(to_pos)

        obs, reward, terminated, truncated, _ = env.step(action)
        done  = terminated or truncated
        step += 1

        # Imprimir turno del BOT
        if act == "ILEGAL":
            print(f"  Turno {step:2d} BOT  | ACCION ILEGAL desde {from_pos}")
        elif act == "ATACA" and target_before:
            t_name = PIECE_NAMES.get(target_before.rank, f"rank{target_before.rank}")
            # Ver si el bot sigue en to_pos (gano), from_pos desaparecio (perdio), etc.
            bot_survived = to_pos in env.pieces and env.pieces[to_pos].owner == BOT
            enemy_survived = to_pos in env.pieces and env.pieces[to_pos].owner == PLAYER
            if bot_survived:
                resultado = f"elimina {t_name} del JUGADOR"
            elif enemy_survived:
                resultado = f"pierde contra {t_name} del JUGADOR"
            else:
                resultado = f"empate con {t_name} (ambos mueren)"
            print(f"  Turno {step:2d} BOT  | ATACA  {name} {from_pos} -> {to_pos} | {resultado}")
        else:
            print(f"  Turno {step:2d} BOT  | MUEVE  {name} {from_pos} -> {to_pos}")

        # Imprimir turno del JUGADOR
        for log_line in env.player_log:
            print(f"         JUG  | {log_line}")

        # Tablero tras ambos turnos (con * en la ultima casilla del bot)
        print_board(env.pieces, env.board_layout, last_move=(from_pos, to_pos))

        if done:
            if reward > 5:
                print(f"  >>> FIN: BOT GANA (reward={reward:.2f})")
            elif reward < -5:
                print(f"  >>> FIN: BOT PIERDE (reward={reward:.2f})")
            else:
                print(f"  >>> FIN: Sin resolver (reward={reward:.2f})")

    if not done:
        print(f"  >>> Limite de {max_steps} pasos alcanzado.")


def run_stats(model, n_games=200, random_deploy=False):
    """
    Ejecuta n_games partidas silenciosamente y devuelve estadisticas.
    random_deploy=True usa posiciones aleatorias.
    """
    env    = StrategoEnvVerbose()
    wins   = 0
    losses = 0
    draws  = 0
    step_counts = []

    for _ in range(n_games):
        if random_deploy:
            obs = random_reset(env)
        else:
            obs, _ = env.reset()

        done, steps = False, 0
        while not done and steps < 200:
            masks = env.action_masks()
            action, _ = model.predict(obs, deterministic=True, action_masks=masks)
            obs, reward, terminated, truncated, _ = env.step(action)
            done  = terminated or truncated
            steps += 1

        step_counts.append(steps)
        if reward > 5:
            wins += 1
        elif reward < -5:
            losses += 1
        else:
            draws += 1

    label = "aleatorio" if random_deploy else "fijo"
    print(f"\nEstadisticas ({n_games} partidas, despliegue {label}):")
    print(f"  Victorias : {wins:3d} ({100*wins/n_games:.0f}%)")
    print(f"  Derrotas  : {losses:3d} ({100*losses/n_games:.0f}%)")
    print(f"  Sin res.  : {draws:3d} ({100*draws/n_games:.0f}%)")
    print(f"  Pasos med.: {np.mean(step_counts):.1f}")
    return wins, losses, draws


# ==============================================================================
# MAIN
# ==============================================================================

if __name__ == "__main__":

    # Cargar el modelo guardado — no hace falta reentrenar
    print("Cargando modelo...")
    model = MaskablePPO.load("ppo_stratego_4x4")
    print("Modelo cargado.\n")

    env_verbose = StrategoEnvVerbose()

    # --- 3 partidas detalladas con despliegue fijo ---
    for i in range(3):
        play_game_verbose(model, env_verbose, label=f"Partida {i+1} — despliegue fijo")

    # --- 2 partidas detalladas con despliegue aleatorio ---
    for i in range(2):
        obs = random_reset(env_verbose)
        # Jugar directamente desde la obs del random_reset
        done, step = False, 0
        print(f"\n{'='*55}")
        print(f"  Partida con despliegue aleatorio {i+1}")
        print(f"{'='*55}")
        print("\nEstado inicial:")
        print_board(env_verbose.pieces, env_verbose.board_layout)

        while not done and step < 60:
            masks = env_verbose.action_masks()
            action, _ = model.predict(obs, deterministic=True, action_masks=masks)
            from_pos, to_pos, name, act, valid = decode_action(
                action, env_verbose.pieces, env_verbose.board_layout,
                env_verbose.pieces, env_verbose.turn
            )
            target_before = env_verbose.pieces.get(to_pos)
            obs, reward, terminated, truncated, _ = env_verbose.step(action)
            done = terminated or truncated
            step += 1

            if act == "ILEGAL":
                print(f"  Turno {step:2d} BOT  | ACCION ILEGAL desde {from_pos}")
            elif act == "ATACA" and target_before:
                t_name = PIECE_NAMES.get(target_before.rank, f"rank{target_before.rank}")
                bot_survived = to_pos in env_verbose.pieces and env_verbose.pieces[to_pos].owner == BOT
                enemy_survived = to_pos in env_verbose.pieces and env_verbose.pieces[to_pos].owner == PLAYER
                if bot_survived:
                    resultado = f"elimina {t_name}"
                elif enemy_survived:
                    resultado = f"pierde contra {t_name}"
                else:
                    resultado = f"empate (ambos mueren)"
                print(f"  Turno {step:2d} BOT  | ATACA  {name} {from_pos} -> {to_pos} | {resultado}")
            else:
                print(f"  Turno {step:2d} BOT  | MUEVE  {name} {from_pos} -> {to_pos}")

            for log_line in env_verbose.player_log:
                print(f"         JUG  | {log_line}")

            print_board(env_verbose.pieces, env_verbose.board_layout, last_move=(from_pos, to_pos))

            if done:
                if reward > 5:
                    print(f"  >>> FIN: BOT GANA (reward={reward:.2f})")
                elif reward < -5:
                    print(f"  >>> FIN: BOT PIERDE (reward={reward:.2f})")
                else:
                    print(f"  >>> FIN: Sin resolver (reward={reward:.2f})")

    # --- Estadisticas globales ---
    run_stats(model, n_games=200, random_deploy=False)
    run_stats(model, n_games=200, random_deploy=True)