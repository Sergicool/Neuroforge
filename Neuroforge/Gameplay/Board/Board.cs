using Godot;
using System.Collections.Generic;

public partial class Board : Node2D
{
    public static readonly Vector2 TILE_SIZE = new(90, 90);

    private GameManager _game;
    private Node2D _tilesManager;
    private Node2D _piecesManager;

    private PackedScene _tileScene;
    private PackedScene _pieceScene;

    private Piece _selectedPiece;
    private readonly List<Tile> _highlightedTiles = new();
    private readonly Dictionary<Vector2I, Tile> _grid = new();
    public IEnumerable<Tile> AllTiles => _grid.Values;

    public override void _Ready()
    {
        _tileScene = GD.Load<PackedScene>("res://Gameplay/Board/Tile/Tile.tscn");
        _pieceScene = GD.Load<PackedScene>("res://Gameplay/Pieces/Piece.tscn");

        _tilesManager = new Node2D { Name = "Tiles" };
        _piecesManager = new Node2D { Name = "Pieces" };

        AddChild(_tilesManager);
        AddChild(_piecesManager);

        GenerateBoard();
        AddToGroup("board");
    }

    public void Initialize(GameManager gameManager) => _game = gameManager;

    public Tile GetTileAt(Vector2I pos) => _grid.TryGetValue(pos, out var tile) ? tile : null;

    public void OnTileClicked(Tile tile)
    {
        if (_game.State == GameState.DEPLOYMENT || !_game.CanInteract()) return;

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
        }
        else
        {
            ClearSelection();
        }
    }

    private void TrySelectPiece(Tile tile)
    {
        if (!tile.IsOccupied) return;

        Piece piece = tile.Occupant;
        if (!_game.IsPlayersTurn(piece.PlayerOwner) || !piece.CanMove) return;

        _selectedPiece = piece;
        ShowPossibleActions(piece);
    }

    private void ShowPossibleActions(Piece piece)
    {
        ClearHighlights();
        foreach (Tile tile in AllTiles)
        {
            TileAction action = MovementSystem.GetAction(piece, tile);
            if (action == TileAction.NONE) continue;

            _highlightedTiles.Add(tile);
            if (action == TileAction.MOVE) tile.HighlightMove();
            else if (action == TileAction.ATTACK) tile.HighlightAttack();
        }
    }

    private void ExecuteAction(Tile target)
    {
        TileAction action = MovementSystem.GetAction(_selectedPiece, target);
        if (action == TileAction.MOVE) MovePiece(_selectedPiece, target);
        else if (action == TileAction.ATTACK) ResolveCombat(_selectedPiece, target.Occupant);
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

    private void ClearSelection()
    {
        _selectedPiece = null;
        ClearHighlights();
    }

    private void ClearHighlights()
    {
        foreach (Tile t in _highlightedTiles) t.ClearHighlight();
        _highlightedTiles.Clear();
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
        if (_grid.Count == 0) return Vector2.Zero;

        int maxX = 0, maxY = 0;
        foreach (var pos in _grid.Keys)
        {
            maxX = Mathf.Max(maxX, pos.X);
            maxY = Mathf.Max(maxY, pos.Y);
        }

        return new Vector2((maxX + 1) * TILE_SIZE.X / 2, (maxY + 1) * TILE_SIZE.Y / 2);
    }

    public void SpawnPiece(PieceType type, PieceOwner owner, Tile tile)
    {
        Piece piece = _pieceScene.Instantiate<Piece>();
        piece.Initialize(type, owner);
        piece.Position = tile.Position;
        tile.SetOccupant(piece);
        _piecesManager.AddChild(piece);
    }
}