import time
import numpy as np
from sb3_contrib import MaskablePPO
# Importamos el entorno y la función de pintar desde tu script original
from StrategoEnv import StrategoEnv, print_board, BOT, PLAYER

def jugar_partida_manual(model_path=None, vs_self=False, max_turns=200):
    """
    Carga un bot entrenado y visualiza la partida turno a turno.
    - model_path: Ruta al archivo .zip del bot (ej: 'ppo_neuroforge_v2026...').
    - vs_self: Si es True, el bot juega contra una copia de sí mismo. 
               Si es False, juega contra el oponente aleatorio del entorno.
    """
    env = StrategoEnv()
    
    # Intentar cargar el modelo si se proporciona una ruta
    if model_path:
        print(f" Cargando modelo entrenado desde: {model_path}...")
        model = MaskablePPO.load(model_path)
        # Si queremos self-play en la evaluación, le asignamos el mismo modelo al rival
        if vs_self:
            env.opponent_model = model
            print("⚔️ Modo: Bot Entrenado VS El Mismo")
        else:
            print("⚔️ Modo: Bot Entrenado VS Oponente Aleatorio")
    else:
        print("🤖 Modo: Oponente Aleatorio VS Oponente Aleatorio (Sin modelo)")
        model = None

    obs, info = env.reset()
    print("\n¡Tablero inicial desplegado!")
    print_board(env.pieces)
    
    input("Presiona ENTER para empezar la partida...")

    total_reward = 0.0
    done = False
    
    while not done and env.turn < max_turns:
        print("-" * 60)
        print(f"TURNO: {env.turn} | Jugando: BOT (Red Neuronal)")
        
        # 1. El Bot entrenado elige su movimiento usando la máscara de acciones legales
        if model is not None:
            masks = env.action_masks()
            action, _ = model.predict(obs, deterministic=True, action_masks=masks)
        else:
            # Si no hay modelo, elige una acción aleatoria válida
            masks = env.action_masks()
            valid_actions = np.where(masks)[0]
            action = np.random.choice(valid_actions)
            
        # 2. Ejecutar la acción en el entorno
        # Nota: Recuerda que en tu step() el Bot mueve primero y luego el entorno 
        # ejecuta automáticamente el turno del Player (oponente).
        obs, reward, terminated, truncated, info = env.step(action)
        total_reward += reward
        done = terminated or truncated
        
        # 3. Mostrar el estado del tablero
        print_board(env.pieces)
        print(f"Recompensa acumulada del Bot: {total_reward:.4f}")
        
        if not done:
            input("Presiona ENTER para ver el siguiente par de movimientos...")
            
    # Fin de la partida
    print("\n==================== FIN DE LA PARTIDA ====================")
    if env.turn >= max_turns:
        print(f"Partida finalizada por límite de turnos ({max_turns}).")
    else:
        # Analizar quién tiene el Núcleo de Energía (Rango 0)
        bot_has_core = any(p.piece_type == "ENERGY_CORE" and p.owner == BOT for p in env.pieces.values())
        player_has_core = any(p.piece_type == "ENERGY_CORE" and p.owner == PLAYER for p in env.pieces.values())
        
        if not bot_has_core:
            print("🏆 ¡Victoria para el PLAYER! El núcleo del Bot fue destruido o se quedó sin movimientos.")
        elif not player_has_core:
            print("🏆 ¡Victoria para el BOT! Logró destruir el núcleo del rival.")
        else:
            print("🤝 Partida terminada en Empate.")

if __name__ == "__main__":
    MI_MODELO = "ppo_neuroforge_v20260520_152651" 
    
    # Ejecutar la visualización
    jugar_partida_manual(model_path=MI_MODELO, vs_self=False, max_turns=300)