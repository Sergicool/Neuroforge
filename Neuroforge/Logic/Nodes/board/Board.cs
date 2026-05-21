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


    // GetState para el entorno de GYM

    private const float NORM = 12f;
    private const float OBS_TURRET = 11f;
    private const float OBS_ENERGY = 12f;

    private const int ROWS = 6;
    private const int COLS = 6;

    public float[][,] GetCurrentStateFlattened()
    {
        float[,] channel0 = new float[ROWS, COLS];
        float[,] channel1 = new float[ROWS, COLS];
        float[,] channel2 = new float[ROWS, COLS];

        foreach (var (coords, tile) in _grid)
        {
            if (coords.Y >= ROWS || coords.X >= COLS) continue;

            if (tile.TileType == TileType.NO_PASSABLE)
            {
                channel1[coords.Y, coords.X] = -1.0f;
            }
            else
            {
                channel1[coords.Y, coords.X] = 0.0f;
            }
        }

        int currentTurn = _game.TurnNumber;

        foreach (var (coords, tile) in _grid)
        {
            if (coords.Y >= ROWS || coords.X >= COLS) continue;

            if (tile.IsOccupied)
            {
                Piece piece = tile.Occupant;
                bool isBotPiece = (piece.PlayerOwner == PieceOwner.BOT);

                if (isBotPiece)
                {
                    if (piece.Type == PieceType.TURRET) channel0[coords.Y, coords.X] = OBS_TURRET / NORM;
                    else if (piece.Type == PieceType.ENERGY_CORE) channel0[coords.Y, coords.X] = OBS_ENERGY / NORM;
                    else channel0[coords.Y, coords.X] = (float)piece.Rank / NORM;
                }
                else
                {
                    if (piece.IsRevealedToBot)
                    {
                        if (piece.Type == PieceType.TURRET) channel0[coords.Y, coords.X] = -OBS_TURRET / NORM;
                        else if (piece.Type == PieceType.ENERGY_CORE) channel0[coords.Y, coords.X] = -OBS_ENERGY / NORM;
                        else channel0[coords.Y, coords.X] = -(float)piece.Rank / NORM;
                    }
                    else
                    {
                        channel0[coords.Y, coords.X] = 0.0f;
                    }
                }

                if (!isBotPiece && !piece.IsRevealedToBot)
                {
                    channel1[coords.Y, coords.X] = 0.5f;
                }
                else
                {
                    channel1[coords.Y, coords.X] = 1.0f;
                }

                if (isBotPiece && piece.CanMove)
                {
                    if (HasAnyLegalMove(piece, currentTurn))
                    {
                        channel2[coords.Y, coords.X] = 1.0f;
                    }
                }
            }
        }

        return new float[][,] { channel0, channel1, channel2 };
    }

    private bool HasAnyLegalMove(Piece piece, int turn)
    {
        foreach (Tile tile in AllTiles)
        {
            if (MovementSystem.CanMove(piece, tile, turn, this))
            {
                return true;
            }
        }
        return false;
    }
}