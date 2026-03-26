using Godot;
using System.Collections.Generic;

public partial class Board : Node2D
{
    private const string TILE_SCENE_PATH  = "res://Gameplay/Board/Tile/Tile.tscn";
    private const string PIECE_SCENE_PATH = "res://Gameplay/Pieces/Piece.tscn";

    private PackedScene _tileScene;
    private PackedScene _pieceScene;

    private Node2D _tilesManager;
    private Node2D _piecesManager;

    public static readonly Vector2 TILE_SIZE = new(90, 90);

    private GameManager _game;

    // Lógica de selección de piezas delegada a InputController
    private BoardInputController _input;

    private readonly Dictionary<Vector2I, Tile> _grid = new();

    public override void _Ready()
    {
        _tileScene  = GD.Load<PackedScene>(TILE_SCENE_PATH);
        _pieceScene = GD.Load<PackedScene>(PIECE_SCENE_PATH);

        _tilesManager  = new Node2D { Name = "Tiles" };
        _piecesManager = new Node2D { Name = "Pieces" };

        AddChild(_tilesManager);
        AddChild(_piecesManager);

        GenerateBoard();
    }

    // Inicializa el tablero con referencia al game manager
    public void Initialize(GameManager gameManager)
    {
        _game  = gameManager;
        _input = new BoardInputController(this, gameManager);
    }

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

    // ==================== Interacción con casillas ====================

    // Punto de entrada del input: delega al controlador de input
    public void OnTileClicked(Tile tile)
    {
        if (_game.State == GameState.DEPLOYMENT)
        {
            _game.GetDeploymentController().TryTogglePiece(tile);
            return;
        }

        if (!_game.CanInteract()) return;

        _input.HandleTileClick(tile);
    }

    // ==================== Acciones de juego ====================

    // Mueve una pieza a una casilla destino
    public void MovePiece(Piece piece, Tile target)
    {
        Tile origin = piece.CurrentTile;
        piece.RegisterTileExit(origin.GridPosition, _game.TurnNumber);
        origin.ClearOccupant();
        target.SetOccupant(piece);
        piece.Position = target.Position;
    }

    // Resuelve el combate entre atacante y defensor
    public void ResolveCombat(Piece attacker, Piece defender)
    {
        Tile targetTile = defender.CurrentTile;
        CombatResult result = attacker.ResolveCombat(defender);

        switch (result)
        {
            case CombatResult.DEFENDER_DIES:
                defender.QueueFree();
                targetTile.ClearOccupant();
                MovePiece(attacker, targetTile);
                break;
            case CombatResult.ATTACKER_DIES:
                attacker.CurrentTile.ClearOccupant();
                attacker.QueueFree();
                break;
            case CombatResult.BOTH_DIE:
                attacker.CurrentTile.ClearOccupant();
                targetTile.ClearOccupant();
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

    public List<BotAction> GetAllPossibleActions(PieceOwner owner)
    {
        List<BotAction> actions = new();

        foreach (Tile tile in AllTiles)
        {
            if (!tile.IsOccupied) continue;
            Piece piece = tile.Occupant;
            if (piece.PlayerOwner != owner || !piece.CanMove) continue;

            foreach (Tile target in AllTiles)
            {
                if (target == tile) continue;
                if (MovementSystem.CanMove(piece, target, _game.TurnNumber, this))
                    actions.Add(new BotAction { From = tile.GridPosition, To = target.GridPosition });
            }
        }

        return actions;
    }

    public void ExecuteBotAction(BotAction action)
    {
        Tile from = GetTileAt(action.From);
        Tile to   = GetTileAt(action.To);

        if (from == null || to == null || !from.IsOccupied) return;

        Piece piece      = from.Occupant;
        TileAction tileAction = MovementSystem.GetAction(piece, to, _game.TurnNumber, this);

        if (tileAction == TileAction.MOVE)   MovePiece(piece, to);
        else if (tileAction == TileAction.ATTACK) ResolveCombat(piece, to.Occupant);
    }

    // Devuelve el estado del tablero como array de floats para la IA del bot.
    // Usa 8 canales para separar tipos de pieza y lo que el bot REALMENTE conoce,
    // sin incluir información de piezas del jugador que no han sido reveladas en combate.
    //
    // Canal 0 — piezas del BOT móviles:        rank / maxRank  (positivo)
    // Canal 1 — piezas del PLAYER reveladas:   rank / maxRank  (el bot las conoce)
    // Canal 2 — piezas del PLAYER ocultas:     1f              (el bot sabe que hay algo, no qué)
    // Canal 3 — casillas intransitables:        1f
    // Canal 4 — TURRET del BOT:                1f
    // Canal 5 — TURRET del PLAYER revelada:    1f
    // Canal 6 — ENERGY_CORE del BOT:           1f
    // Canal 7 — ENERGY_CORE del PLAYER:        1f              (siempre visible, es el objetivo)
    public float[] GetState()
    {
        const int CHANNELS = 8;
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

            if (tile.TileType == TileType.NO_PASSABLE)
            {
                state[GetIndex(3, r, c)] = 1f;
                continue;
            }

            if (!tile.IsOccupied) continue;

            Piece p = tile.Occupant;

            if (p.PlayerOwner == PieceOwner.BOT)
            {
                // El bot siempre conoce sus propias piezas
                if (p.Type == PieceType.TURRET)
                    state[GetIndex(4, r, c)] = 1f;
                else if (p.Type == PieceType.ENERGY_CORE)
                    state[GetIndex(6, r, c)] = 1f;
                else
                    state[GetIndex(0, r, c)] = p.Rank / MAX_RANK;
            }
            else // PLAYER
            {
                if (p.Type == PieceType.ENERGY_CORE)
                {
                    // El ENERGY_CORE del jugador es el objetivo: siempre conocido
                    state[GetIndex(7, r, c)] = 1f;
                }
                else if (!p.IsRevealedToBot)
                {
                    // El bot sabe que hay una pieza pero no conoce su tipo ni rango
                    state[GetIndex(2, r, c)] = 1f;
                }
                else if (p.Type == PieceType.TURRET)
                {
                    state[GetIndex(5, r, c)] = 1f;
                }
                else
                {
                    // Pieza revelada en combate: el bot conoce su rango
                    state[GetIndex(1, r, c)] = p.Rank / MAX_RANK;
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

        return new Vector2((maxX + 1) * TILE_SIZE.X / 2f, (maxY + 1) * TILE_SIZE.Y / 2f);
    }

    private void GenerateBoard()
    {
        using var file = FileAccess.Open("res://Gameplay/Board/BoardLayout.txt", FileAccess.ModeFlags.Read);
        int y = 0;

        while (!file.EofReached())
        {
            string line = file.GetLine().Trim();
            if (line.Length == 0) continue;

            for (int x = 0; x < line.Length; x++)
            {
                Tile tile = _tileScene.Instantiate<Tile>();
                tile.Initialize(this);
                tile.GridPosition = new Vector2I(x, y);
                tile.SetType(CharToTileType(line[x]));
                tile.Position = new Vector2(x * TILE_SIZE.X, y * TILE_SIZE.Y);

                _tilesManager.AddChild(tile);
                _grid[tile.GridPosition] = tile;
            }
            y++;
        }
    }

    private static TileType CharToTileType(char c) => c switch
    {
        'X' => TileType.NO_PASSABLE,
        'P' => TileType.PLAYER_DEPLOYMENT,
        'B' => TileType.BOT_DEPLOYMENT,
        _   => TileType.PASSABLE
    };
}