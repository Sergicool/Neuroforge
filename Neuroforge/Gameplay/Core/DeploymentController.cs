using Godot;
using System.Collections.Generic;

public class DeploymentController
{
    private DeploymentUI _ui;
    private GameManager  _game;
    private Board        _board;

    private readonly Dictionary<PieceType, int> _remainingPieces = new();
    private PieceType? _selectedPieceType;

    // Inicializa el controlador con referencias al game manager, tablero e interfaz
    public void Initialize(GameManager game, Board board, DeploymentUI ui)
    {
        _game  = game;
        _board = board;
        _ui    = ui;

        InitializeArmy();

        _ui.OnPieceSelected  += SelectPiece;
        _ui.OnRandomPressed  += RandomizePlayerDeployment;
        _ui.OnStartPressed   += StartBattle;

        UpdateUI();
    }

    // ==================== Lógica de despliegue ====================

    // Intenta colocar o quitar una pieza al hacer click en una casilla
    public void TryTogglePiece(Tile tile)
    {
        if (tile.IsOccupied && tile.Occupant.PlayerOwner == PieceOwner.PLAYER)
        {
            RemovePiece(tile);
            return;
        }

        TryPlacePiece(tile);
    }

    // Selecciona el tipo de pieza a colocar manualmente
    public void SelectPiece(PieceType type)
    {
        if (_remainingPieces[type] <= 0) return;
        _selectedPieceType = type;
    }

    // Intenta colocar la pieza seleccionada en una casilla
    private void TryPlacePiece(Tile tile)
    {
        if (_selectedPieceType == null) return;
        if (tile.TileType != TileType.PLAYER_DEPLOYMENT) return;
        if (tile.IsOccupied) return;
        if (_remainingPieces[_selectedPieceType.Value] <= 0) return;

        _board.SpawnPiece(_selectedPieceType.Value, PieceOwner.PLAYER, tile);
        _remainingPieces[_selectedPieceType.Value]--;
        UpdateUI();
    }

    // Quita la pieza de una casilla y la devuelve al inventario
    private void RemovePiece(Tile tile)
    {
        PieceType type = tile.Occupant.Type;
        tile.Occupant.QueueFree();
        tile.ClearOccupant();
        _remainingPieces[type]++;
        _selectedPieceType = type;
        _ui.SetActiveType(type);
        UpdateUI();
    }

    // Coloca todas las piezas del jugador de forma aleatoria
    private void RandomizePlayerDeployment()
    {
        ClearPlayerDeployment();
        List<PieceType> army  = BuildArmyList();
        List<Tile>      tiles = GetPlayerDeploymentTiles();
        Util.Shuffle(tiles);

        for (int i = 0; i < tiles.Count && i < army.Count; i++)
            _board.SpawnPiece(army[i], PieceOwner.PLAYER, tiles[i]);

        UpdateRemainingFromBoard();
        UpdateUI();
    }

    // Genera las piezas del bot de forma aleatoria en sus casillas de despliegue
    private void SpawnBotArmy()
    {
        List<PieceType> army  = BuildArmyList();
        List<Tile>      tiles = GetBotDeploymentTiles();
        Util.Shuffle(tiles);

        for (int i = 0; i < tiles.Count && i < army.Count; i++)
            _board.SpawnPiece(army[i], PieceOwner.BOT, tiles[i]);
    }

    // Confirma el despliegue, genera el ejército del bot e inicia la partida
    private void StartBattle()
    {
        if (GetRemainingCount() > 0) return;

        SpawnBotArmy();
        _ui.HideUI();
        _game.StartGame();
    }

    // ==================== Helpers ====================

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

    private List<PieceType> BuildArmyList()
    {
        List<PieceType> army = new();
        foreach (var kv in PiecesData.Data)
            for (int i = 0; i < kv.Value.MaxCount; i++)
                army.Add(kv.Key);
        return army;
    }

    private List<Tile> GetPlayerDeploymentTiles()
    {
        List<Tile> tiles = new();
        foreach (Tile t in _board.AllTiles)
            if (t.TileType == TileType.PLAYER_DEPLOYMENT) tiles.Add(t);
        return tiles;
    }

    private List<Tile> GetBotDeploymentTiles()
    {
        List<Tile> tiles = new();
        foreach (Tile t in _board.AllTiles)
            if (t.TileType == TileType.BOT_DEPLOYMENT) tiles.Add(t);
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

    private void UpdateUI()
    {
        _ui.SetRemainingPieces(GetRemainingCount());
        _ui.UpdatePieceCounts(_remainingPieces);
    }
}