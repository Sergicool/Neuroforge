import sys
import json
import numpy as np
from sb3_contrib import MaskablePPO

ROWS, COLS = 10, 10

def action_to_move(action: int):
    n = ROWS * COLS
    fi = action // n
    ti = action % n
    return {
        "from": {"x": fi % COLS, "y": fi // COLS},
        "to":   {"x": ti % COLS, "y": ti // COLS}
    }

def build_mask(state_data: dict) -> np.ndarray:
    n = ROWS * COLS
    mask = np.zeros(n * n, dtype=bool)
    for move in state_data["valid_moves"]:
        fx, fy = move["from"]["x"], move["from"]["y"]
        tx, ty = move["to"]["x"],   move["to"]["y"]
        
        # Validar que las coordenadas están dentro del tablero
        if not (0 <= fx < COLS and 0 <= fy < ROWS and
                0 <= tx < COLS and 0 <= ty < ROWS):
            print(f"WARN: coordenada fuera de rango: from=({fx},{fy}) to=({tx},{ty})", 
                  file=sys.stderr, flush=True)
            continue
            
        fi = fy * COLS + fx
        ti = ty * COLS + tx
        mask[fi * n + ti] = True
    return mask

def main():
    model_path = sys.argv[1] if len(sys.argv) > 1 else "neuroforge_bot.zip"
    
    try:
        model = MaskablePPO.load(model_path)
        print("READY", flush=True)
    except Exception as e:
        print(f"ERROR:{e}", flush=True)
        sys.exit(1)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            data = json.loads(line)
            
            # Reconstruir observación desde el estado plano
            flat = np.array(data["state"], dtype=np.float32)
            obs = flat.reshape(3, ROWS, COLS)
            
            mask = build_mask(data)
            
            # Si no hay movimientos válidos en la máscara, responder con fallback
            if not mask.any():
                print(json.dumps({"error": "no_valid_moves"}), flush=True)
                continue
            
            action, _ = model.predict(obs, deterministic=True, action_masks=mask)
            move = action_to_move(int(action))
            print(json.dumps(move), flush=True)
            
        except Exception as e:
            print(json.dumps({"error": str(e)}), flush=True)

if __name__ == "__main__":
    main()