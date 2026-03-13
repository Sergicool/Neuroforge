using Godot;
using System.Collections.Generic;

public enum TileAction
{
    NONE,
    MOVE,
    ATTACK
}

public partial class Board : Node2D
{
    private GameManager _game;

    public static readonly Vector2 TILE_SIZE = new(90, 90);

    private const string TILE_SCENE_PATH = "res://Gameplay/Board/Tile/Tile.tscn";
    private const string BOARD_LAYOUT_PATH = "res://Gameplay/Board/BoardLayout.txt";
    private const string PIECE_SCENE_PATH = "res://Gameplay/Pieces/Piece.tscn";

    private Node2D _tilesManager;
    private Node2D _piecesManager;

    private PackedScene _tileScene;
    private PackedScene _pieceScene;

    private readonly Dictionary<Vector2I, Tile> _grid = new();
    public IEnumerable<Tile> AllTiles => _grid.Values;

    private Piece _selectedPiece;
    private readonly List<Tile> _highlightedTiles = new();

    private GameState _state = GameState.WAITING_INPUT;
    private PieceOwner _currentTurn = PieceOwner.PLAYER;

    public override void _Ready()
    {
        _tileScene = GD.Load<PackedScene>(TILE_SCENE_PATH);
        _pieceScene = GD.Load<PackedScene>(PIECE_SCENE_PATH);

        _tilesManager = new Node2D { Name = "Tiles" };
        _piecesManager = new Node2D { Name = "Pieces" };

        AddChild(_tilesManager);
        AddChild(_piecesManager);

        GenerateBoard();
        SpawnTestPieces();

        AddToGroup("board");
    }

    public void Initialize(GameManager gameManager)
    {
        _game = gameManager;
    }

    // ================= INPUT =================

    public void OnTileClicked(Tile tile)
    {
        if (!_game.CanInteract())
            return;

        if (_selectedPiece == null)
        {
            TrySelectPiece(tile);
            return;
        }

        if (_highlightedTiles.Contains(tile))
        {
            ExecuteAction(tile);
            _game.EndTurn();
            ClearSelection();
            return;
        }

        ClearSelection();
    }

    private void TrySelectPiece(Tile tile)
    {
        if (!tile.IsOccupied)
            return;

        Piece piece = tile.Occupant;

        if (!_game.IsPlayersTurn(piece.PlayerOwner))
            return;

        if (!piece.CanMove)
            return;

        _selectedPiece = piece;
        ShowPossibleActions(piece);
    }

    // ================= ACTIONS =================

    private void ShowPossibleActions(Piece piece)
    {
        ClearHighlights();

        foreach (Tile tile in AllTiles)
        {
            TileAction action = MovementSystem.GetAction(piece, tile);

            if (action == TileAction.NONE)
                continue;

            _highlightedTiles.Add(tile);

            if (action == TileAction.MOVE)
                tile.HighlightMove();
            else if (action == TileAction.ATTACK)
                tile.HighlightAttack();
        }
    }

    private void ExecuteAction(Tile target)
    {
        TileAction action = MovementSystem.GetAction(_selectedPiece, target);

        if (action == TileAction.MOVE)
            MovePiece(_selectedPiece, target);
        else if (action == TileAction.ATTACK)
            ResolveCombat(_selectedPiece, target.Occupant);
    }

    private void MovePiece(Piece piece, Tile target)
    {
        piece.CurrentTile.ClearOccupant();
        target.SetOccupant(piece);
        piece.Position = target.Position;
    }

    private void ResolveCombat(Piece attacker, Piece defender)
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

    // ================= TURN =================

    private void ClearSelection()
    {
        _selectedPiece = null;
        ClearHighlights();
        _state = GameState.WAITING_INPUT;
    }

    private void ClearHighlights()
    {
        foreach (Tile tile in _highlightedTiles)
            tile.ClearHighlight();

        _highlightedTiles.Clear();
    }

    // ================= BOARD =================

    public Tile GetTileAt(Vector2I pos)
        => _grid.TryGetValue(pos, out var tile) ? tile : null;

    private void GenerateBoard()
    {
        using var file = FileAccess.Open(BOARD_LAYOUT_PATH, FileAccess.ModeFlags.Read);

        int y = 0;
        while (!file.EofReached())
        {
            string line = file.GetLine().Trim();
            if (line.Length == 0)
                continue;

            for (int x = 0; x < line.Length; x++)
            {
                Tile tile = _tileScene.Instantiate<Tile>();
                tile.GridPosition = new Vector2I(x, y);
                tile.SetType(CharToTileType(line[x]));
                tile.Position = new Vector2(x * TILE_SIZE.X, y * TILE_SIZE.Y);

                _tilesManager.AddChild(tile);
                _grid[tile.GridPosition] = tile;
            }
            y++;
        }
    }

    private TileType CharToTileType(char c) => c switch
    {
        'X' => TileType.NO_PASSABLE,
        'P' => TileType.PLAYER_DEPLOYMENT,
        'B' => TileType.BOT_DEPLOYMENT,
        _ => TileType.PASSABLE
    };

    public Vector2 GetBoardCenter()
    {
        if (_grid.Count == 0)
            return Vector2.Zero;

        int maxX = 0;
        int maxY = 0;

        foreach (var pos in _grid.Keys)
        {
            maxX = Mathf.Max(maxX, pos.X);
            maxY = Mathf.Max(maxY, pos.Y);
        }

        return new Vector2(
            (maxX + 1) * TILE_SIZE.X / 2,
            (maxY + 1) * TILE_SIZE.Y / 2
        );
    }

    // Test spawn
    private void SpawnTestPieces()
    {
        SpawnArmyFromData(PieceOwner.PLAYER, startRow: 6);
        SpawnArmyFromData(PieceOwner.BOT, startRow: 0);
    }

    private void SpawnArmyFromData(PieceOwner owner, int startRow)
    {
        List<PieceType> army = BuildArmyFromData();

        int index = 0;

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                if (index >= army.Count)
                    return;

                var pos = new Vector2I(x, startRow + y);

                if (_grid.TryGetValue(pos, out Tile tile))
                {
                    SpawnPiece(army[index], owner, tile);
                    index++;
                }
            }
        }
    }

    private List<PieceType> BuildArmyFromData()
    {
        var army = new List<PieceType>();

        foreach (var kvp in PiecesData.Data)
        {
            var type = kvp.Key;
            var def = kvp.Value;

            for (int i = 0; i < def.MaxCount; i++)
                army.Add(type);
        }

        Shuffle(army);

        return army;
    }

    private void Shuffle(List<PieceType> list)
    {
        RandomNumberGenerator rng = new();
        rng.Randomize();

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Test spawn end

    private void SpawnPiece(PieceType type, PieceOwner owner, Tile tile)
    {
        Piece piece = _pieceScene.Instantiate<Piece>();
        piece.Initialize(type, owner);
        piece.Position = tile.Position;
        tile.SetOccupant(piece);
        _piecesManager.AddChild(piece);
    }
}