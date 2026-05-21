using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class Board : Node2D
{
    [Export] private TileMapLayer _mapLayer;
    [Export] private PackedScene TILE_SCENE;
    [Export] private PackedScene PIECE_SCENE;

    private Node2D _tilesManager;
    private Node2D _piecesManager;

    public static readonly Vector2I TILE_SIZE = new(48, 48);
    public static float TileScale => TILE_SIZE.X / 16f;

    private GameScene _game;
    private CombatUI _combatUI;

    // Lógica de selección de piezas delegada a InputController
    private BoardInputController _input;

    private readonly Dictionary<Vector2I, Tile> _grid = new();

    public override void _Ready()
    {
        _tilesManager = new Node2D { Name = "Tiles" };
        _piecesManager = new Node2D { Name = "Pieces" };

        AddChild(_tilesManager);
        AddChild(_piecesManager);

        GenerateBoard();
    }

    // Inicializa el tablero con referencia al game manager
    public void Initialize(GameScene gameManager)
    {
        _game = gameManager;
        _input = new BoardInputController(this, gameManager);
    }

    public void SetCombatUI(CombatUI ui) => _combatUI = ui;

    // ==================== Consultas del tablero ====================

    public IEnumerable<Tile> AllTiles => _grid.Values;

    public Tile GetTileAt(Vector2I pos) => _grid.TryGetValue(pos, out var tile) ? tile : null;

    public bool HasEnergyCore(PieceOwner owner)
    {
        foreach (Tile tile in AllTiles)
        {
            if (!tile.IsOccupied) continue;
            Piece piece = tile.Occupant;
            if (piece.PlayerOwner == owner && piece.Type == PieceType.ENERGY_CORE)
                return true;
        }
        return false;
    }

    public bool HasAnyMoves(PieceOwner owner)
    {
        foreach (Tile tile in AllTiles)
        {
            if (!tile.IsOccupied) continue;
            Piece piece = tile.Occupant;
            if (piece.PlayerOwner != owner || !piece.CanMove) continue;

            foreach (Tile target in AllTiles)
            {
                if (target == tile) continue;
                if (MovementSystem.CanMove(piece, target, _game.TurnNumber, this))
                    return true;
            }
        }
        return false;
    }

    public Dictionary<PieceType, int> GetRemainingCounts(PieceOwner owner)
    {
        var counts = new Dictionary<PieceType, int>();
        foreach (var kv in PiecesData.Data)
            counts[kv.Key] = 0;

        foreach (Tile tile in AllTiles)
            if (tile.IsOccupied && tile.Occupant.PlayerOwner == owner)
                counts[tile.Occupant.Type]++;

        return counts;
    }

    // ==================== Interacción con casillas ====================

    // Punto de entrada del input: delega al controlador de input
    public async Task OnTileClicked(Tile tile)
    {
        if (_game.State == GameState.DEPLOYMENT)
        {
            _game.GetDeploymentController().TryTogglePiece(tile);
            return;
        }

        if (!_game.CanInteract()) return;

        await _input.HandleTileClick(tile);
    }

    // ==================== Acciones de juego ====================

    // Mueve una pieza a una casilla destino
    public async Task MovePiece(Piece piece, Tile target, bool animated = true)
    {
        Tile origin = piece.CurrentTile;

        int distance = Mathf.Abs(target.GridPosition.X - origin.GridPosition.X) +
                       Mathf.Abs(target.GridPosition.Y - origin.GridPosition.Y);

        piece.RegisterMove(origin, target, _game.TurnNumber);
        origin.ClearOccupant();
        target.SetOccupant(piece);

        if (animated)
            await piece.AnimateMoveTo(target.Position);

        piece.Position = target.Position;

        // Revelar SCOUT si hace un movimiento que solo puede hacer el y si no esta ya revelada
        if (piece.Type == PieceType.SCOUT && distance > 1 && (piece.IsRevealedToBot == false || piece.IsVisibleToPlayer == false))
            await piece.AnimateBlinkReveal();
    }

    // Resuelve el combate entre atacante y defensor
    public async Task ResolveCombat(Piece attacker, Piece defender)
    {
        Tile defenderTile = defender.CurrentTile;
        CombatResult result = attacker.ResolveCombat(defender);

        // Animación de avance
        attacker.ZIndex = defenderTile.ZIndex + 1;
        await attacker.AnimateMoveTo(defenderTile.Position);
        attacker.ZIndex = defenderTile.ZIndex;

        bool attackerHiddenForUI = !attacker.IsVisibleToPlayer || !attacker.IsRevealedToBot;
        bool defenderHiddenForUI = !defender.IsVisibleToPlayer || !defender.IsRevealedToBot;

        if (_combatUI != null)
            await _combatUI.ShowCombat(attacker, defender, result, attackerHiddenForUI, defenderHiddenForUI);

        attacker.Reveal();
        defender.Reveal();

        // Aplicar resultado sobre el tablero
        switch (result)
        {
            case CombatResult.DEFENDER_DIES:
                defender.QueueFree();
                defenderTile.ClearOccupant();
                await MovePiece(attacker, defenderTile, animated: false);
                break;
            case CombatResult.ATTACKER_DIES:
                attacker.CurrentTile.ClearOccupant();
                attacker.QueueFree();
                break;
            case CombatResult.BOTH_DIE:
                attacker.CurrentTile.ClearOccupant();
                defenderTile.ClearOccupant();
                attacker.QueueFree();
                defender.QueueFree();
                break;
        }
    }

    // Crea e instancia una pieza en una casilla
    public void SpawnPiece(PieceType type, PieceOwner owner, Tile tile)
    {
        Piece piece = PIECE_SCENE.Instantiate<Piece>();
        piece.Initialize(type, owner);
        piece.Position = tile.Position;
        tile.SetOccupant(piece);
        _piecesManager.AddChild(piece);
    }

    // ==================== Comportamiento del bot ====================

    public List<MovementAction> GetAllPossibleActions(PieceOwner owner)
    {
        List<MovementAction> actions = new();

        foreach (Tile tile in AllTiles)
        {
            if (!tile.IsOccupied) continue;
            Piece piece = tile.Occupant;
            if (piece.PlayerOwner != owner || !piece.CanMove) continue;

            foreach (Tile target in AllTiles)
            {
                if (target == tile) continue;
                if (MovementSystem.CanMove(piece, target, _game.TurnNumber, this))
                    actions.Add(new MovementAction { From = tile.GridPosition, To = target.GridPosition });
            }
        }

        return actions;
    }

    public async Task ExecuteBotAction(MovementAction action)
    {
        _input.ClearSelection();

        Tile from = GetTileAt(action.From);
        Tile to = GetTileAt(action.To);

        if (from == null || to == null || !from.IsOccupied) return;

        Piece piece = from.Occupant;
        TileAction tileAction = MovementSystem.GetAction(piece, to, _game.TurnNumber, this);

        if (tileAction == TileAction.MOVE)
            await MovePiece(piece, to);
        else if (tileAction == TileAction.ATTACK)
            await ResolveCombat(piece, to.Occupant);
    }

    // Devuelve el estado del tablero como array de floats para la IA del bot.
    // Estructura de 3 canales perfectamente alineada con la nueva CNN en Python.
    // Formato plano equivalente a tensores: [Canal, Fila, Columna]
    public float[] GetState()
    {
        const int CHANNELS = 3;
        const float MAX_RANK = 10f;

        // 1. Detectar dimensiones reales del tablero dinámicamente (10x10 por defecto)
        int rows = 0, cols = 0;
        foreach (var pos in _grid.Keys)
        {
            rows = Mathf.Max(rows, pos.Y + 1);
            cols = Mathf.Max(cols, pos.X + 1);
        }

        float[] state = new float[CHANNELS * rows * cols];

        // 2. Función matemática de mapeo indexado (Orden: Canales -> Filas -> Columnas)
        // Esto replica exactamente cómo PyTorch aplana internamente un tensor (C, H, W)
        int GetIndex(int channel, int row, int col)
        {
            return (channel * rows * cols) + (row * cols) + col;
        }

        // 3. Rellenar la información procesando cada casilla
        foreach (Tile tile in AllTiles)
        {
            int r = tile.GridPosition.Y;
            int c = tile.GridPosition.X;

            // --- CANAL 1: Transitabilidad (Geografía del mapa) ---
            if (tile.TileType == TileType.NO_PASSABLE)
            {
                state[GetIndex(1, r, c)] = -1.0f; // Obstáculo / Lago
                // Si la casilla es intransitable, terminamos aquí para esta celda
                continue;
            }
            else
            {
                state[GetIndex(1, r, c)] = 1.0f;  // Terreno transitable libre
            }

            // Si no hay ninguna pieza en esta casilla, pasamos a la siguiente
            if (!tile.IsOccupied) continue;

            Piece p = tile.Occupant;

            // --- CANAL 0: Identidad y Rango de las Piezas ---
            if (p.PlayerOwner == PieceOwner.BOT)
            {
                // Piezas propias (Valores positivos)
                if (p.Type == PieceType.ENERGY_CORE)
                    state[GetIndex(0, r, c)] = 0.05f; // Un valor fijo único para tu Core
                else if (p.Type == PieceType.TURRET)
                    state[GetIndex(0, r, c)] = 0.1f;  // Un valor fijo único para tu Torreta
                else
                    state[GetIndex(0, r, c)] = p.Rank / MAX_RANK; // Rango normalizado (0.1 a 1.0)
            }
            else // PLAYER (El rival desde la perspectiva del Bot)
            {
                // Piezas enemigas (Valores negativos / Ocultación)
                if (!p.IsRevealedToBot)
                {
                    state[GetIndex(0, r, c)] = -0.05f; // "Sé que hay algo aquí, pero está oculto"
                }
                else
                {
                    // Si ya se reveló en combate, el Bot conoce su rango real en negativo
                    if (p.Type == PieceType.ENERGY_CORE)
                        state[GetIndex(0, r, c)] = -0.05f;
                    else if (p.Type == PieceType.TURRET)
                        state[GetIndex(0, r, c)] = -0.1f;
                    else
                        state[GetIndex(0, r, c)] = -(p.Rank / MAX_RANK);
                }
            }

            // --- CANAL 2: Movilidad de la pieza ---
            // Guardamos 1.0 si la pieza puede desplazarse en su turno, 0.0 si es estática
            if (p.Type != PieceType.ENERGY_CORE && p.Type != PieceType.TURRET && p.CanMove)
            {
                state[GetIndex(2, r, c)] = 1.0f;
            }
            else
            {
                state[GetIndex(2, r, c)] = 0.0f;
            }
        }

        return state;
    }

    // ==================== Generación del tablero ====================

    public Vector2 GetBoardCenter()
    {
        if (_grid.Count == 0) return Vector2.Zero;

        int maxX = 0, maxY = 0;
        foreach (var pos in _grid.Keys)
        {
            maxX = Mathf.Max(maxX, pos.X);
            maxY = Mathf.Max(maxY, pos.Y);
        }

        return new Vector2(maxX * TILE_SIZE.X / 2f, maxY * TILE_SIZE.Y / 2f);
    }

    private void GenerateBoard()
    {
        var usedCells = _mapLayer.GetUsedCells();

        foreach (Vector2I coords in usedCells)
        {
            Vector2I atlasCoords = _mapLayer.GetCellAtlasCoords(coords);

            Tile tile = TILE_SCENE.Instantiate<Tile>();
            tile.Initialize(this);
            tile.GridPosition = coords;
            tile.SetType(AtlasToTileType(atlasCoords));

            // MapToLocal nos da el centro de la celda en coordenadas del mundo
            tile.Position = _mapLayer.MapToLocal(coords);

            _tilesManager.AddChild(tile);
            _grid[coords] = tile;
        }
    }

    private TileType AtlasToTileType(Vector2I atlasCoords)
    {
        // Aquí defines tus "4 posiciones específicas" de la primera fila
        return atlasCoords switch
        {
            { X: 0, Y: 0 } => TileType.PASSABLE,
            { X: 1, Y: 0 } => TileType.NO_PASSABLE,
            { X: 2, Y: 0 } => TileType.PLAYER_DEPLOYMENT,
            { X: 3, Y: 0 } => TileType.BOT_DEPLOYMENT,
            _ => TileType.NO_PASSABLE // Por defecto
        };
    }
}