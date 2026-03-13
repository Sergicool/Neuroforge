using Godot;
using System.Collections.Generic;

public partial class DeploymentController : Node
{
    private DeploymentUI _ui;

    private GameManager _game;
    private Board _board;

    private Dictionary<PieceType, int> _remainingPieces = new();
    private PackedScene _pieceScene = GD.Load<PackedScene>("res://Gameplay/Pieces/Piece.tscn");

    public void Initialize(GameManager game, Board board, DeploymentUI ui)
    {
        _game = game;
        _board = board;
        _ui = ui;

        InitializeArmy();

        _ui.OnRandomPressed += RandomizePlayerDeployment;
        _ui.OnStartPressed += StartBattle;

        UpdateUI();
    }

    // ARMY SETUP

    private void InitializeArmy()
    {
        _remainingPieces.Clear();

        foreach (var kv in PiecesData.Data)
        {
            _remainingPieces[kv.Key] = kv.Value.MaxCount;
        }
    }

    private int GetRemainingCount()
    {
        int total = 0;

        foreach (var v in _remainingPieces.Values)
            total += v;

        return total;
    }

    private void UpdateUI()
    {
        _ui.SetRemainingPieces(GetRemainingCount());
    }

    // RANDOM DEPLOYMENT

    private void RandomizePlayerDeployment()
    {
        ClearPlayerDeployment();

        List<PieceType> army = BuildArmyList();

        RandomNumberGenerator rng = new();
        rng.Randomize();

        List<Tile> playerTiles = GetPlayerDeploymentTiles();

        Shuffle(playerTiles, rng);

        int index = 0;

        foreach (Tile tile in playerTiles)
        {
            if (index >= army.Count)
                break;

            SpawnPiece(army[index], PieceOwner.PLAYER, tile);
            index++;
        }

        UpdateRemainingFromBoard();
        UpdateUI();
    }

    private List<PieceType> BuildArmyList()
    {
        var army = new List<PieceType>();

        foreach (var kv in PiecesData.Data)
        {
            for (int i = 0; i < kv.Value.MaxCount; i++)
                army.Add(kv.Key);
        }

        return army;
    }

    private List<Tile> GetPlayerDeploymentTiles()
    {
        List<Tile> tiles = new();

        foreach (Tile t in _board.AllTiles)
        {
            if (t.TileType == TileType.PLAYER_DEPLOYMENT)
                tiles.Add(t);
        }

        return tiles;
    }

    private void Shuffle<T>(List<T> list, RandomNumberGenerator rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // PIECE SPAWN

    private void SpawnPiece(PieceType type, PieceOwner owner, Tile tile)
    {
        Piece piece = _pieceScene.Instantiate<Piece>();
        piece.Initialize(type, owner);

        piece.Position = tile.Position;
        tile.SetOccupant(piece);

        _board.AddChild(piece);
    }

    // CLEANUP

    private void ClearPlayerDeployment()
    {
        foreach (Tile tile in _board.AllTiles)
        {
            if (!tile.IsOccupied)
                continue;

            if (tile.Occupant.PlayerOwner == PieceOwner.PLAYER)
            {
                tile.Occupant.QueueFree();
                tile.ClearOccupant();
            }
        }
    }

    private void UpdateRemainingFromBoard()
    {
        InitializeArmy();

        foreach (Tile tile in _board.AllTiles)
        {
            if (!tile.IsOccupied)
                continue;

            if (tile.Occupant.PlayerOwner != PieceOwner.PLAYER)
                continue;

            _remainingPieces[tile.Occupant.Type]--;
        }
    }

    // START BATTLE

    private void StartBattle()
    {
        if (GetRemainingCount() > 0)
            return;

        SpawnBotArmy();

        _ui.HideUI();

        _game.StartBattle();
    }

    private void SpawnBotArmy()
    {
        List<PieceType> army = BuildArmyList();

        RandomNumberGenerator rng = new();
        rng.Randomize();

        List<Tile> tiles = new();

        foreach (Tile t in _board.AllTiles)
        {
            if (t.TileType == TileType.BOT_DEPLOYMENT)
                tiles.Add(t);
        }

        Shuffle(tiles, rng);

        int index = 0;

        foreach (Tile tile in tiles)
        {
            if (index >= army.Count)
                break;

            SpawnPiece(army[index], PieceOwner.BOT, tile);
            index++;
        }
    }
}
