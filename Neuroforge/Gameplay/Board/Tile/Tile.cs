using Godot;

public enum TileType
{
    PASSABLE,
    NO_PASSABLE,
    PLAYER_DEPLOYMENT,
    BOT_DEPLOYMENT
}

public partial class Tile : Area2D
{
    private Sprite2D _sprite;

    public Vector2I GridPosition { get; set; }
    public TileType TileType { get; private set; }
    public Piece Occupant { get; private set; }
    public bool IsOccupied => Occupant != null;

    private static readonly Texture2D BLUE_TILE = GD.Load<Texture2D>("res://Gameplay/Board/Tile/BlueTile.png");
    private static readonly Texture2D GREY_TILE = GD.Load<Texture2D>("res://Gameplay/Board/Tile/GreyTile.png");
    private static readonly Texture2D GREEN_TILE = GD.Load<Texture2D>("res://Gameplay/Board/Tile/GreenTile.png");
    private static readonly Texture2D RED_TILE = GD.Load<Texture2D>("res://Gameplay/Board/Tile/RedTile.png");

    private Texture2D _baseTexture;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");

        if (_baseTexture != null)
            ApplyTexture(_baseTexture);
    }

    public void SetType(TileType type)
    {
        TileType = type;

        _baseTexture = type == TileType.NO_PASSABLE
            ? GREY_TILE
            : BLUE_TILE;

        if (IsInsideTree())
            ApplyTexture(_baseTexture);
    }

    private void ApplyTexture(Texture2D tex)
    {
        _sprite.Texture = tex;
        _sprite.Scale = Board.TILE_SIZE / tex.GetSize();
    }

    // ================= OCCUPANT =================

    public void SetOccupant(Piece piece)
    {
        Occupant = piece;
        if (piece != null)
            piece.CurrentTile = this;
    }

    public void ClearOccupant() => Occupant = null;

    // ================= INPUT =================

    public override void _InputEvent(Viewport viewport, InputEvent e, int shapeIdx)
    {
        if (e is InputEventMouseButton mb &&
            mb.ButtonIndex == MouseButton.Left &&
            mb.Pressed)
        {
            GetTree().CallGroup("board", "OnTileClicked", this);
        }
    }

    // ================= HIGHLIGHT =================

    public void HighlightMove() => ApplyTexture(GREEN_TILE);
    public void HighlightAttack() => ApplyTexture(RED_TILE);
    public void ClearHighlight() => ApplyTexture(_baseTexture);
}