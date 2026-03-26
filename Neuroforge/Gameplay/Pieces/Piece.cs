using Godot;
using System.Collections.Generic;

public partial class Piece : Node2D
{
    // Gestion interna
    private Sprite2D _sprite;

    public PieceOwner PlayerOwner { get; private set; }
    public PieceType Type { get; private set; }
    public int Rank { get; private set; }
    public bool CanMove { get; private set; }
    public PieceState State { get; private set; }
    public Tile CurrentTile { get; set; }
    private Dictionary<Vector2I, int> _tileCooldowns = new();

    public override void _Ready()
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");
    }

    // Inicializa una pieza dado su tipo y propietario
    public void Initialize(PieceType type, PieceOwner owner)
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");

        PlayerOwner = owner;
        Type = type;

        var def = PiecesData.Data[type];
        Rank = def.Rank;
        CanMove = def.CanMove;

        State = owner == PieceOwner.PLAYER ? PieceState.REVEALED_FOR_PLAYER : PieceState.REVEALED_FOR_BOT;

        UpdateVisual();
    }

    // Comprueba si la pieza puede moverse a una casilla
    public bool IsValidMove(Tile tile, int turn) => MovementSystem.CanMove(this, tile, turn);

    // Devuelve el resultado del combate dada la pieza a la que ataca
    public CombatResult ResolveCombat(Piece defender) => CombatSystem.Resolve(this, defender);

    // Revela la pieza en el tablero
    public void Reveal()
    {
        if (State == PieceState.REVEALED_FOR_BOTH) return;
        State = PieceState.REVEALED_FOR_BOTH;
        UpdateVisual();
    }

    // Registra la posicion de una casilla de la que sale
    public void RegisterTileExit(Vector2I tile, int turn)
    {
        _tileCooldowns[tile] = turn;
    }

    // Comprueba si puede volver a una casilla
    public bool CanReturnToTile(Vector2I tile, int currentTurn)
    {
        CleanupCooldowns(currentTurn);

        if (!_tileCooldowns.ContainsKey(tile)) return true;

        int lastTurn = _tileCooldowns[tile];

        return (currentTurn - lastTurn) >= 3;
    }

    public void CleanupCooldowns(int currentTurn)
    {
        var toRemove = new List<Vector2I>();

        foreach (var kvp in _tileCooldowns)
        {
            if (currentTurn - kvp.Value >= 3)
                toRemove.Add(kvp.Key);
        }

        foreach (var t in toRemove)
            _tileCooldowns.Remove(t);
    }

    // Actualiza su visualizacion
    private void UpdateVisual()
    {
        int sprite_width = PiecesData.ATLAS_COLUMN_WIDTH;
        int sprite_height = PiecesData.ATLAS_HEIGHT / 2;

        var def = PiecesData.Data[Type];
        _sprite.Texture = PiecesData.Atlas;
        _sprite.RegionEnabled = true;

        // Dependiendo de si esta revelada o no
        int x = State == PieceState.REVEALED_FOR_BOT
            ? PiecesData.HIDDEN_ATLAS_COLUMN * sprite_width
            : def.AtlasColumn * sprite_width;

        // Dependiendo de su propietario
        int y = PlayerOwner == PieceOwner.PLAYER ? 0 : sprite_height;

        _sprite.RegionRect = new Rect2(x, y, sprite_width, sprite_height);
        _sprite.Scale = new Vector2(4, 4);
    }
}