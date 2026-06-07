import sys
import numpy as np
from sb3_contrib import MaskablePPO
from StrategoEnv import NeuroForgeEnv

def evaluate(model_path, n_games=200, opponent_path=None, deterministic=True):
    env = NeuroForgeEnv()
    
    # Cargar oponente si se especifica, si no usa aleatorio
    if opponent_path:
        # Limpieza de extensión por si acaso
        if opponent_path.endswith(".zip"):
            opponent_path = opponent_path[:-4]
        env.opponent_model = MaskablePPO.load(opponent_path)
        oponente = opponent_path
    else:
        oponente = "ALEATORIO"
    
    # Limpieza de extensión para el modelo principal
    if model_path.endswith(".zip"):
        model_path = model_path[:-4]
        
    try:
        model = MaskablePPO.load(model_path)
    except FileNotFoundError:
        print(f"❌ Error: No se pudo encontrar el archivo del modelo '{model_path}.zip'")
        return

    wins = draws = losses = timeouts = 0
    total_steps = 0
    
    print(f"🤖 Iniciando {n_games} partidas...")
    
    for i in range(n_games):
        obs, _ = env.reset()
        done = False
        steps = 0
        ep_reward = 0
        
        while not done:
            mask = env.action_masks()
            action, _ = model.predict(obs, deterministic=deterministic, action_masks=mask)
            obs, reward, done, _, _ = env.step(action)
            ep_reward += reward
            steps += 1
            
            # Control de seguridad por si el entorno no corta a los 800 pasos
            if steps >= 800:
                done = True
        
        total_steps += steps
        
        # Clasificar resultado por reward final o límite de pasos
        if steps >= 800:
            timeouts += 1
        elif ep_reward > 5.0:
            wins += 1
        elif ep_reward < -5.0:
            losses += 1
        else:
            draws += 1
            
        # Pequeño indicador de progreso para saber que no está colgado
        if (i + 1) % 50 == 0:
            print(f"   > Partidas completadas: {i + 1}/{n_games}")
    
    print("\n" + "="*40)
    print(f"Modelo:    {model_path}")
    print(f"Oponente:  {oponente}")
    print(f"Partidas:  {n_games}")
    print(f"---------------------------------")
    print(f"Victorias: {wins}  ({wins/n_games:.1%})")
    print(f"Derrotas:  {losses}  ({losses/n_games:.1%})")
    print(f"Empates:   {draws}  ({draws/n_games:.1%})")
    print(f"Timeout:   {timeouts}  ({timeouts/n_games:.1%})")
    print(f"Duración media: {total_steps/n_games:.0f} pasos")
    print("="*40 + "\n")

def evaluate_debug(model_path, n_games=200):
    env = NeuroForgeEnv()
    model = MaskablePPO.load(model_path)
    
    wins = real_losses = timeouts = 0
    total_steps = 0
    
    for _ in range(n_games):
        obs, _ = env.reset()
        done = False
        steps = 0
        ep_reward = 0
        last_reward = 0
        
        while not done:
            mask = env.action_masks()
            action, _ = model.predict(obs, deterministic=True, action_masks=mask)
            obs, reward, done, _, _ = env.step(action)
            ep_reward += reward
            last_reward = reward
            steps += 1
        
        total_steps += steps
        
        if last_reward == 10.0:
            wins += 1
        elif last_reward == -10.0:
            real_losses += 1
        else:
            timeouts += 1  # terminó por MAX_TURNS o empate real
    
    print(f"Victorias reales:  {wins} ({wins/n_games:.1%})")
    print(f"Derrotas reales:   {real_losses} ({real_losses/n_games:.1%})")
    print(f"Timeouts/empates:  {timeouts} ({timeouts/n_games:.1%})")
    print(f"Pasos medios:      {total_steps/n_games:.0f}")

if __name__ == "__main__":
    # CASO 1: Si pasas el nombre de un modelo por la consola
    if len(sys.argv) > 1:
        modelo_consola = sys.argv[1]
        print(f"🚀 Ejecutando evaluación desde argumento de línea de comandos...")
        evaluate_debug(modelo_consola, n_games=200)
        
    # CASO 2: Si ejecutas el script directamente sin argumentos adicionales
    else:
        evaluate_debug("🔄 Ejecutando batería de pruebas predefinidas...")
        
        # 📣 MODIFICA ESTOS NOMBRES CON TUS TIMESTAMPS REALES:
        ULTIMO_MODELO = "neuroforge_bot_v20260604_204512" 
        PENULTIMO_MODELO = "neuroforge_bot_v20260604_224454"
        PRIMER_MODELO = "neuroforge_bot_v20260605_173144.zip"
        
        # 1. Evaluar el modelo final contra aleatorio
        evaluate_debug(ULTIMO_MODELO, n_games=200)

        # 2. Evaluar el modelo final contra el modelo anterior (ver si mejoró)
        evaluate_debug(
            ULTIMO_MODELO,
            n_games=200,
            opponent_path=PENULTIMO_MODELO
        )

        # 3. Evaluar el primer modelo para comparar evolución
        evaluate_debug(PRIMER_MODELO, n_games=200)