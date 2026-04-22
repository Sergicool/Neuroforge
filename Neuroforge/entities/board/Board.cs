using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class Board : Node2D
{
    [Export] private TileMapLayer _mapLayer;

    private const string TILE_SCENE_PATH = "res://entities/tile/Tile.tscn";
    private const string PIECE_SCENE_PATH = "res://entities/pieces/Piece.tscn";

    private PackedScene _tileScene;
    private PackedScene _pieceScene;

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
        _tileScene = GD.Load<PackedScene>(TILE_SCENE_PATH);
        _pieceScene = GD.Load<PackedScene>(PIECE_SCENE_PATH);

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
            if (piece.PlayerOwner == owner && piece.Type == PieceType.NEXUS)
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

        piece.RegisterMove(origin, target);
        origin.ClearOccupant();
        target.SetOccupant(piece);

        if (animated)
            await piece.AnimateMoveTo(target.Position);

        piece.Position = target.Position;

        // Revelar SCOUT si hace un movimiento que solo puede hacer el y si no esta ya revelada
        if (piece.Type == PieceType.SCOUT && distance > 1 && piece.IsRevealedToBot == false)
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
        Piece piece = _pieceScene.Instantiate<Piece>();
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
    // 9 canales — estructura idéntica a _get_obs() del entorno Python de entrenamiento.
    //
    // Canal 0 — piezas del BOT móviles             rank / maxRank
    // Canal 1 — piezas del PLAYER reveladas         rank / maxRank
    // Canal 2 — piezas del PLAYER ocultas           1f  (el bot sabe que hay algo, no qué tipo)
    // Canal 3 — casillas intransitables             1f
    // Canal 4 — TURRET del BOT                      1f
    // Canal 5 — TURRET del PLAYER revelada          1f
    // Canal 6 — ENERGY_CORE del BOT                 1f
    // Canal 7 — ENERGY_CORE del PLAYER localizado   1f  (solo si fue revelado en combate)
    // Canal 8 — zona de despliegue del PLAYER        1f  (el Core oculto comenzó aquí; el bot infiere por descarte)
    public float[] GetState()
    {
        const int CHANNELS = 9;
        const float MAX_RANK = 10f;

        int rows = 0, cols = 0;
        foreach (var pos in _grid.Keys)
        {
            rows = Mathf.Max(rows, pos.Y + 1);
            cols = Mathf.Max(cols, pos.X + 1);
        }

        float[] state = new float[CHANNELS * rows * cols];

        int GetIndex(int ch, int r, int c) => ch * rows * cols + r * cols + c;

        foreach (Tile tile in AllTiles)
        {
            int r = tile.GridPosition.Y;
            int c = tile.GridPosition.X;

            // Canal 3: casillas intransitables
            if (tile.TileType == TileType.NO_PASSABLE)
            {
                state[GetIndex(3, r, c)] = 1f;
                continue;
            }

            // Canal 8: zona de despliegue del jugador (estático, igual que en Python)
            // El bot sabe que el Core rival empezó aquí; a medida que ataca piezas ocultas
            // en esta zona y no aparece el Core, puede inferir su posición por descarte.
            if (tile.TileType == TileType.PLAYER_DEPLOYMENT)
                state[GetIndex(8, r, c)] = 1f;

            if (!tile.IsOccupied) continue;

            Piece p = tile.Occupant;

            if (p.PlayerOwner == PieceOwner.BOT)
            {
                // El bot siempre conoce sus propias piezas
                if (p.Type == PieceType.TURRET)
                    state[GetIndex(4, r, c)] = 1f;
                else if (p.Type == PieceType.NEXUS)
                    state[GetIndex(6, r, c)] = 1f;
                else
                    state[GetIndex(0, r, c)] = p.Rank / MAX_RANK;
            }
            else // PLAYER
            {
                if (p.Type == PieceType.NEXUS)
                {
                    if (p.IsRevealedToBot)
                        state[GetIndex(7, r, c)] = 1f;  // Core localizado tras combate
                    else
                        state[GetIndex(2, r, c)] = 1f;  // Oculto: el bot no sabe que es el Core
                }
                else if (!p.IsRevealedToBot)
                {
                    state[GetIndex(2, r, c)] = 1f;      // Pieza oculta desconocida
                }
                else if (p.Type == PieceType.TURRET)
                {
                    state[GetIndex(5, r, c)] = 1f;      // Turret revelada en combate
                }
                else
                {
                    state[GetIndex(1, r, c)] = p.Rank / MAX_RANK;  // Pieza revelada con rango conocido
                }
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

            Tile tile = _tileScene.Instantiate<Tile>();
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