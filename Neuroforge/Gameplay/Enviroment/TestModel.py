import time
import argparse
import numpy as np
from sb3_contrib import MaskablePPO
from sb3_contrib.common.maskable.utils import get_action_masks

from StrategoEnv import (
    StrategoEnv, BOT, PLAYER, ROWS, COLS,
    PIECE_NAMES, RANK_ENERGY_CORE, print_board,
)

# ==============================================================================
# COLORES ANSI (para terminal, se desactivan si no hay soporte)
# ==============================================================================

try:
    import sys
    USE_COLOR = sys.stdout.isatty()
except Exception:
    USE_COLOR = False

RESET  = "\033[0m"    if USE_COLOR else ""
BOLD   = "\033[1m"    if USE_COLOR else ""
GREEN  = "\033[92m"   if USE_COLOR else ""
RED    = "\033[91m"   if USE_COLOR else ""
YELLOW = "\033[93m"   if USE_COLOR else ""
CYAN   = "\033[96m"   if USE_COLOR else ""
DIM    = "\033[2m"    if USE_COLOR else ""


# ==============================================================================
# RESULTADO DE PARTIDA
# ==============================================================================

WIN  = "WIN"
LOSE = "LOSE"
TIE  = "TIE"


# ==============================================================================
# JUGAR UNA PARTIDA COMPLETA Y DEVOLVER RESULTADO
# ==============================================================================

def play_one_game(
    model: MaskablePPO,
    env: StrategoEnv,
    watch: bool = False,
    delay: float = 0.0,
    max_turns: int = 300,
) -> dict:
    """
    Juega una partida completa con el modelo dado.

    Parametros:
        model        : modelo MaskablePPO ya cargado
        env          : entorno StrategoEnv
        watch        : si True, imprime el tablero en cada turno
        delay        : segundos de pausa entre turnos (solo si watch=True)
        max_turns    : limite de turnos para evitar partidas infinitas

    Retorna un dict con:
        result       : WIN / LOSE / TIE / TIMEOUT
        turns        : numero de turnos jugados
        total_reward : recompensa acumulada del bot
        final_pieces : piezas que quedaron al final
    """

    # Inicializar el entorno
    obs, _ = env.reset()

    total_reward = 0.0
    turn_count   = 0
    result       = "TIMEOUT"

    if watch:
        print(f"\n{'='*50}")
        print(f"  NUEVA PARTIDA")
        print(f"{'='*50}")
        print(f"\n{CYAN}Estado inicial:{RESET}")
        print_board(env.pieces, env.board_layout)
        if delay > 0:
            time.sleep(delay)

    while turn_count < max_turns:
        # Obtener mascara de acciones y elegir accion del bot
        mask      = get_action_masks(env)
        action, _ = model.predict(obs, deterministic=True, action_masks=mask)
        action    = int(action)

        # Decodificar la accion para mostrarla si watch=True
        if watch:
            n_tiles    = ROWS * COLS
            from_idx   = action // n_tiles
            to_idx     = action % n_tiles
            from_pos   = (from_idx // COLS, from_idx % COLS)
            to_pos     = (to_idx   // COLS, to_idx   % COLS)
            piece      = env.pieces.get(from_pos)
            piece_name = PIECE_NAMES.get(piece.rank, f"r{piece.rank}") if piece else "???"
            target     = env.pieces.get(to_pos)

            # Determinar si es movimiento o ataque para el log
            if target is not None and target.owner == PLAYER:
                target_name = PIECE_NAMES.get(target.rank, "???") if target.revealed else "???"
                action_str  = f"{RED}ATACA{RESET} a P:{target_name}"
            else:
                action_str  = "se mueve"

            print(f"{BOLD}Turno {turn_count+1:3d}{RESET} | "
                  f"{CYAN}BOT{RESET}    B:{piece_name:12s} {from_pos} → {to_pos}  {action_str}")

        # Ejecutar el paso en el entorno (incluye turno del bot y del player)
        obs, reward, terminated, truncated, _ = env.step(action)
        total_reward += reward
        turn_count   += 1

        if watch:
            # Imprimir tablero tras cada turno completo (bot + player)
            print_board(env.pieces, env.board_layout)
            if delay > 0:
                time.sleep(delay)

        if terminated or truncated:
            # Determinar resultado segun el estado final del tablero
            bot_has_core    = any(p.rank == RANK_ENERGY_CORE and p.owner == BOT    for p in env.pieces.values())
            player_has_core = any(p.rank == RANK_ENERGY_CORE and p.owner == PLAYER for p in env.pieces.values())

            if bot_has_core and not player_has_core:
                result = WIN
            elif not bot_has_core and player_has_core:
                result = LOSE
            elif not bot_has_core and not player_has_core:
                result = TIE
            else:
                # Ambos tienen core: termino por falta de movimientos
                bot_moves    = env._get_all_moves(BOT)
                player_moves = env._get_all_moves(PLAYER)
                if not bot_moves and not player_moves:
                    result = TIE
                elif not bot_moves:
                    result = LOSE
                elif not player_moves:
                    result = WIN
                else:
                    result = TIE  # No deberia ocurrir nunca
            break

    if watch:
        color = GREEN if result == WIN else (RED if result == LOSE else YELLOW)
        print(f"\n{color}{BOLD}Resultado: {result}{RESET}  |  "
              f"Turnos: {turn_count}  |  Recompensa total: {total_reward:.2f}\n")

    return {
        "result":       result,
        "turns":        turn_count,
        "total_reward": total_reward,
        "final_pieces": dict(env.pieces),
    }


# ==============================================================================
# ESTADISTICAS SOBRE N PARTIDAS
# ==============================================================================

def run_stats(
    model: MaskablePPO,
    n_games: int = 200,
    max_turns: int = 300,
) -> dict:
    """
    Juega n_games partidas y muestra estadisticas de rendimiento.

    Parametros:
        model        : modelo MaskablePPO ya cargado
        n_games      : numero de partidas a jugar
        max_turns    : limite de turnos por partida

    Retorna un dict con contadores y porcentajes.
    """

    env = StrategoEnv()

    wins     = 0
    losses   = 0
    ties     = 0
    timeouts = 0
    total_turns   = 0
    total_rewards = 0.0
    turns_per_result = {WIN: [], LOSE: [], TIE: [], "TIMEOUT": []}

    print(f"\n{BOLD}{'─'*50}{RESET}")
    print(f"{BOLD}  EVALUACION — {n_games} partidas {RESET}")
    print(f"{BOLD}{'─'*50}{RESET}")

    for i in range(n_games):
        game = play_one_game(
            model, env,
            watch=False,
            max_turns=max_turns,
        )
        result = game["result"]
        total_turns   += game["turns"]
        total_rewards += game["total_reward"]
        turns_per_result[result].append(game["turns"])

        if result == WIN:
            wins += 1
        elif result == LOSE:
            losses += 1
        elif result == TIE:
            ties += 1
        else:
            timeouts += 1

        # Barra de progreso simple cada 10%
        if (i + 1) % max(1, n_games // 10) == 0:
            pct = (i + 1) / n_games * 100
            bar = "█" * int(pct / 5) + "░" * (20 - int(pct / 5))
            print(f"  [{bar}] {pct:5.1f}%  W:{wins:4d}  L:{losses:4d}  T:{ties:3d}  TO:{timeouts:3d}", end="\r")

    print()  # nueva linea tras la barra de progreso

    # Calcular medias de turnos por resultado (evitar division por cero)
    avg_turns_win  = np.mean(turns_per_result[WIN])  if turns_per_result[WIN]  else 0.0
    avg_turns_lose = np.mean(turns_per_result[LOSE]) if turns_per_result[LOSE] else 0.0
    avg_turns_tie  = np.mean(turns_per_result[TIE])  if turns_per_result[TIE]  else 0.0

    # Mostrar tabla de resultados
    print(f"\n  {'Resultado':<12} {'Partidas':>8}  {'%':>7}  {'Media turnos':>14}")
    print(f"  {'─'*46}")
    print(f"  {GREEN}{'Victoria':<12}{RESET} {wins:>8}  {wins/n_games*100:>6.1f}%  {avg_turns_win:>14.1f}")
    print(f"  {RED}{'Derrota':<12}{RESET}  {losses:>8}  {losses/n_games*100:>6.1f}%  {avg_turns_lose:>14.1f}")
    print(f"  {YELLOW}{'Empate':<12}{RESET}  {ties:>8}  {ties/n_games*100:>6.1f}%  {avg_turns_tie:>14.1f}")
    if timeouts:
        print(f"  {DIM}{'Timeout':<12}{RESET}  {timeouts:>8}  {timeouts/n_games*100:>6.1f}%")
    print(f"  {'─'*46}")
    print(f"  {'Total partidas':<14}  {n_games:>8}")
    print(f"  {'Media turnos':<14}  {total_turns/n_games:>8.1f}")
    print(f"  {'Media reward':<14}  {total_rewards/n_games:>8.2f}")
    print(f"{BOLD}{'─'*50}{RESET}\n")

    return {
        "wins":      wins,
        "losses":    losses,
        "ties":      ties,
        "timeouts":  timeouts,
        "win_rate":  wins / n_games,
        "avg_turns": total_turns / n_games,
        "avg_reward": total_rewards / n_games,
    }


# ==============================================================================
# MAIN
# ==============================================================================

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Evalua y/o visualiza el bot de Stratego 4x4."
    )

    parser.add_argument(
        "model",
        type=str,
        help="Nombre del modelo (sin .zip)"
    )

    parser.add_argument("--games", type=int, default=200)
    parser.add_argument("--watch", action="store_true")
    parser.add_argument("--delay", type=float, default=0.0)
    parser.add_argument("--max-turns", type=int, default=300)

    args = parser.parse_args()

    model_path = args.model.replace(".zip", "")

    print(f"\nCargando modelo: {model_path}.zip ...")
    model = MaskablePPO.load(model_path)
    print("Modelo cargado.\n")

    if not args.watch:
        run_stats(model, n_games=args.games)
        exit()

    if args.watch:
        env = StrategoEnv()
        play_one_game(
            model, env,
            watch=True,
            delay=args.delay,
            max_turns=args.max_turns,
        )

    run_stats(model, n_games=args.games, max_turns=args.max_turns)
