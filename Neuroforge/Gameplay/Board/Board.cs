using Godot;
using System.Collections.Generic;

public partial class Board : Node2D
{
    // Externo
    private const string TILE_SCENE_PATH = "res://Gameplay/Board/Tile/Tile.tscn";
    private const string PIECE_SCENE_PATH = "res://Gameplay/Pieces/Piece.tscn";
    private PackedScene _tileScene;
    private PackedScene _pieceScene;

    // Gestión interna
    private Node2D _tilesManager;
    private Node2D _piecesManager;

    public static readonly Vector2 TILE_SIZE = new(90, 90); // Tamaño de las casillas que se utilizara para posicionarlas

    private GameManager _game;      // Referencia al game manager

    private Piece _selectedPiece;                           // Pieza seleccionada
    private readonly List<Tile> _highlightedTiles = new();  // Movimientos posibles de la casilla seleccionada

    private readonly Dictionary<Vector2I, Tile> _grid = new();  // Diccionario de las casillas que conforma el tablero

    public override void _Ready()
    {
        // Genera e instancia el tablero
        _tileScene = GD.Load<PackedScene>(TILE_SCENE_PATH);
        _pieceScene = GD.Load<PackedScene>(PIECE_SCENE_PATH);

        _tilesManager = new Node2D { Name = "Tiles" };
        _piecesManager = new Node2D { Name = "Pieces" };

        AddChild(_tilesManager);
        AddChild(_piecesManager);

        GenerateBoard();
    }

    // Inicializa con referencia al game manager
    public void Initialize(GameManager gameManager) => _game = gameManager;

    // Devuelve una lista de todas las casillas
    public IEnumerable<Tile> AllTiles => _grid.Values;

    // Devuelve una casilla dada su posicion
    public Tile GetTileAt(Vector2I pos) => _grid.TryGetValue(pos, out var tile) ? tile : null;

    // Procesa la interaccion con una casilla
    public void OnTileClicked(Tile tile)
    {
        if (_game.State == GameState.DEPLOYMENT)
        {
            _game.GetDeploymentController().TryTogglePiece(tile);
            return;
        }

        if (!_game.CanInteract()) return;

        if (_selectedPiece == null)
        {
            TrySelectPiece(tile);
            return;
        }

        // Si se hace click en una casilla resaltada se procede con su accion respectiva
        if (_highlightedTiles.Contains(tile))
        {
            ExecuteAction(tile);
            _game.EndTurn();
        }
        ClearSelection();
    }

    // Prueba a seleccionarse la pieza de una casilla, y en caso de exito se selecciona mostrando sus posibles acciones
    private void TrySelectPiece(Tile tile)
    {
        if (!tile.IsOccupied) return;

        Piece piece = tile.Occupant;
        if (!_game.IsPlayersTurn(piece.PlayerOwner) || !piece.CanMove) return;

        _selectedPiece = piece;
        ShowPossibleActions(piece);
    }

    // Dependiendo de la pieza, se resaltan todas las casillas en las que pueda actuar dependiendo del tipo de accion
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

    // Ejecuta la accion de la pieza seleccionada, sobre una casilla objetivo
    private void ExecuteAction(Tile target)
    {
        TileAction action = MovementSystem.GetAction(_selectedPiece, target);
        if (action == TileAction.MOVE)
            MovePiece(_selectedPiece, target);
        else if (action == TileAction.ATTACK)
            ResolveCombat(_selectedPiece, target.Occupant);
    }

    // Mueve una pieza a una casilla
    private void MovePiece(Piece piece, Tile target)
    {
        piece.CurrentTile.ClearOccupant();
        target.SetOccupant(piece);
        piece.Position = target.Position;
    }

    // Procesa el combate entre 2 piezas
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

    // Limpia la pieza seleccionada y casillas resaltadas
    private void ClearSelection()
    {
        _selectedPiece = null;
        ClearHighlights();
    }

    // Limpia cualquier efecto de resaltado en las casillas resaltadas
    private void ClearHighlights()
    {
        foreach (Tile t in _highlightedTiles) t.ClearHighlight();
        _highlightedTiles.Clear();
    }

    // Logica para generar el tablero, leyendo el fichero con el layout, va creando e instanciando cada casilla con su respectivo tipo y posicion
    private void GenerateBoard()
    {
        using var file = FileAccess.Open("res://Gameplay/Board/BoardLayout.txt", FileAccess.ModeFlags.Read);
        int y = 0;
        Tile tile;
        while (!file.EofReached())
        {
            string line = file.GetLine().Trim();
            if (line.Length == 0) continue;

            for (int x = 0; x < line.Length; x++)
            {
                tile = _tileScene.Instantiate<Tile>();
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

    // Traduce de letra a tipo de casilla
    private TileType CharToTileType(char c) => c switch
    {
        'X' => TileType.NO_PASSABLE,
        'P' => TileType.PLAYER_DEPLOYMENT,
        'B' => TileType.BOT_DEPLOYMENT,
        _ => TileType.PASSABLE
    };

    // Obtiene el punto central del tablero
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

    // Crea e instancia una pieza dado su tipo, propietario y casilla en la que se crea
    public void SpawnPiece(PieceType type, PieceOwner owner, Tile tile)
    {
        Piece piece = _pieceScene.Instantiate<Piece>();
        piece.Initialize(type, owner);
        piece.Position = tile.Position;
        tile.SetOccupant(piece);
        _piecesManager.AddChild(piece);
    }

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
            if (piece.PlayerOwner != owner) continue;
            if (!piece.CanMove) continue;

            foreach (Tile target in AllTiles)
            {
                if (target == piece.CurrentTile) continue;

                if (MovementSystem.CanMove(piece, target))
                    return true;
            }
        }

        return false;
    }
}