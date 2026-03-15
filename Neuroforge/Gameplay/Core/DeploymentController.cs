using Godot;
using System;
using System.Collections.Generic;

public partial class DeploymentController : Node
{
    // Externo
    private PackedScene _pieceScene = GD.Load<PackedScene>("res://Gameplay/Pieces/Piece.tscn");

    private DeploymentUI _ui;
    private GameManager _game;
    private Board _board;

    private Dictionary<PieceType, int> _remainingPieces = new();    // Cantidad de piezas de cada tipo que quedan por colocar
    private PieceType? _selectedPieceType;  // Tipo de pieza seleccionada para colocar manualmente en el tablero

    // Inicializa el controlador con referencia al game manager, tablero e interfaz de despliegue
    public void Initialize(GameManager game, Board board, DeploymentUI ui)
    {
        _game = game;
        _board = board;
        _ui = ui;

        InitializeArmy();

        _ui.OnPieceSelected += SelectPiece;
        _ui.OnRandomPressed += RandomizePlayerDeployment;
        _ui.OnStartPressed += StartBattle;

        UpdateUI();
    }

    // Establece la lista de todas las piezas pendientes de colocar
    private void InitializeArmy()
    {
        _remainingPieces.Clear();
        foreach (var kv in PiecesData.Data)
            _remainingPieces[kv.Key] = kv.Value.MaxCount;
    }

    // Devuelve el total de piezas que quedan por colocar
    private int GetRemainingCount()
    {
        int total = 0;
        foreach (var v in _remainingPieces.Values) total += v;
        return total;
    }

    // Colocacion random de piezas independientemente de las que ya haya posicionadas
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

    // Devuelve una lista con todas las piezas repetidas hasta sus respectivas cantidades maximas
    private List<PieceType> BuildArmyList()
    {
        List<PieceType> army = new();
        foreach (var kv in PiecesData.Data)
            for (int i = 0; i < kv.Value.MaxCount; i++)
                army.Add(kv.Key);
        return army;
    }

    // Devuelve la lista de todas las casillas en las que el jugador puede colocar nuevas piezas
    private List<Tile> GetPlayerDeploymentTiles()
    {
        List<Tile> tiles = new();
        foreach (Tile t in _board.AllTiles)
            if (t.TileType == TileType.PLAYER_DEPLOYMENT) tiles.Add(t);
        return tiles;
    }

    // Quita todas las piezas del jugador colocadas en el tablero
    private void ClearPlayerDeployment()
    {
        foreach (Tile t in _board.AllTiles)
            if (t.IsOccupied && t.Occupant.PlayerOwner == PieceOwner.PLAYER)
            {
                t.Occupant.QueueFree();
                t.ClearOccupant();
            }
    }
    
    // Actualiza la cantidad de piezas pendientes por colocar dependiendo de las que ya hay colocadas
    private void UpdateRemainingFromBoard()
    {
        InitializeArmy();
        foreach (Tile t in _board.AllTiles)
            if (t.IsOccupied && t.Occupant.PlayerOwner == PieceOwner.PLAYER)
                _remainingPieces[t.Occupant.Type]--;
    }

    // Genera las piezas del bot de manera aleatoria
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

    // Genera las piezas del bot, oculta la interfaz y comienza la partida
    private void StartBattle()
    {
        if (GetRemainingCount() > 0) return;

        SpawnBotArmy();
        _ui.HideUI();
        _game.StartGame();
    }

    // ==================== Interfaz ====================

    // Actualiza la interfaz dependiendo de las piezas que queden por colocar
    private void UpdateUI()
    {
        _ui.SetRemainingPieces(GetRemainingCount());
        _ui.UpdatePieceCounts(_remainingPieces);
    }

    // Establece el tipo de pieza seleccionada a colocar manuamlente
    public void SelectPiece(PieceType type)
    {
        if (_remainingPieces[type] <= 0) return;

        _selectedPieceType = type;
    }

    // Trata de colocar una pieza en una casilla dada
    public void TryPlacePiece(Tile tile)
    {
        if (_selectedPieceType == null) return;

        if (tile.TileType != TileType.PLAYER_DEPLOYMENT) return;

        if (tile.IsOccupied) return;

        if (_remainingPieces[_selectedPieceType.Value] <= 0) return;

        _board.SpawnPiece(_selectedPieceType.Value, PieceOwner.PLAYER, tile);

        _remainingPieces[_selectedPieceType.Value]--;

        UpdateUI();
    }

    public void TryTogglePiece(Tile tile)
    {
        if (tile.IsOccupied && tile.Occupant.PlayerOwner == PieceOwner.PLAYER)
        {
            PieceType type = tile.Occupant.Type;
            tile.Occupant.QueueFree();
            tile.ClearOccupant();
            _remainingPieces[type]++;
            _selectedPieceType = type;
            UpdateUI();
            return;
        }

        TryPlacePiece(tile);
    }
}