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

    private void InitializeArmy()
    {
        _remainingPieces.Clear();
        foreach (var kv in PiecesData.Data)
            _remainingPieces[kv.Key] = kv.Value.MaxCount;
    }

    private int GetRemainingCount()
    {
        int total = 0;
        foreach (var v in _remainingPieces.Values) total += v;
        return total;
    }

    private void UpdateUI() => _ui.SetRemainingPieces(GetRemainingCount());

    private void RandomizePlayerDeployment()
    {
        ClearPlayerDeployment();
        List<PieceType> army = BuildArmyList();
        List<Tile> tiles = GetPlayerDeploymentTiles();
        Util.Shuffle(tiles);

        for (int i = 0; i < tiles.Count && i < army.Count; i++)
            _board.SpawnPiece(army[i], PieceOwner.PLAYER, tiles[i]);

        UpdateRemainingFromBoard();
        UpdateUI();
    }

    private List<PieceType> BuildArmyList()
    {
        List<PieceType> army = new();
        foreach (var kv in PiecesData.Data)
            for (int i = 0; i < kv.Value.MaxCount; i++) army.Add(kv.Key);
        return army;
    }

    private List<Tile> GetPlayerDeploymentTiles()
    {
        List<Tile> tiles = new();
        foreach (Tile t in _board.AllTiles)
            if (t.TileType == TileType.PLAYER_DEPLOYMENT) tiles.Add(t);
        return tiles;
    }

    private void ClearPlayerDeployment()
    {
        foreach (Tile t in _board.AllTiles)
            if (t.IsOccupied && t.Occupant.PlayerOwner == PieceOwner.PLAYER)
            {
                t.Occupant.QueueFree();
                t.ClearOccupant();
            }
    }

    private void UpdateRemainingFromBoard()
    {
        InitializeArmy();
        foreach (Tile t in _board.AllTiles)
            if (t.IsOccupied && t.Occupant.PlayerOwner == PieceOwner.PLAYER)
                _remainingPieces[t.Occupant.Type]--;
    }

    private void StartBattle()
    {
        if (GetRemainingCount() > 0) return;

        SpawnBotArmy();
        _ui.HideUI();
        _game.StartBattle();
    }

    private void SpawnBotArmy()
    {
        List<PieceType> army = BuildArmyList();
        List<Tile> tiles = new();

        foreach (Tile t in _board.AllTiles)
            if (t.TileType == TileType.BOT_DEPLOYMENT) tiles.Add(t);

        Util.Shuffle(tiles);

        for (int i = 0; i < tiles.Count && i < army.Count; i++)
            _board.SpawnPiece(army[i], PieceOwner.BOT, tiles[i]);
    }
}