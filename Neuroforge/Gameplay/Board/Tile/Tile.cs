using Godot;

public partial class Tile : Area2D
{
    private static readonly Texture2D BLUE_TILE  = GD.Load<Texture2D>("res://Gameplay/Board/Tile/BlueTile.png");
    private static readonly Texture2D GREY_TILE  = GD.Load<Texture2D>("res://Gameplay/Board/Tile/GreyTile.png");
    private static readonly Texture2D GREEN_TILE = GD.Load<Texture2D>("res://Gameplay/Board/Tile/GreenTile.png");
    private static readonly Texture2D RED_TILE   = GD.Load<Texture2D>("res://Gameplay/Board/Tile/RedTile.png");

    private Board    _board;
    private Sprite2D _sprite;
    private CollisionShape2D _collisionShape;
    private Texture2D _baseTexture;

    public Vector2I GridPosition { get; set; }
    public TileType TileType     { get; private set; }
    public Piece    Occupant     { get; private set; }
    public bool     IsOccupied   => Occupant != null;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _collisionShape = GetNode<CollisionShape2D>("CollisionShape2D");
        if (_baseTexture != null) ApplyTexture(_baseTexture);
    }

    public void Initialize(Board board) => _board = board;

    // Establece el tipo de casilla y su textura base
    public void SetType(TileType type)
    {
        TileType     = type;
        _baseTexture = type == TileType.NO_PASSABLE ? GREY_TILE : BLUE_TILE;
        if (IsInsideTree())
            ApplyTexture(_baseTexture);
    }

    public void SetOccupant(Piece piece)
    {
        Occupant = piece;
        if (piece != null) piece.CurrentTile = this;
    }

    public void ClearOccupant() => Occupant = null;

    // ========== Resaltados ==========
    public void HighlightMove()   => ApplyTexture(GREEN_TILE);
    public void HighlightAttack() => ApplyTexture(RED_TILE);
    public void ClearHighlight()  => ApplyTexture(_baseTexture);

    private void ApplyTexture(Texture2D tex)
    {
        _sprite.Texture = tex;
        _sprite.Scale   = Board.TILE_SIZE / tex.GetSize();
        _collisionShape.Scale = Board.TILE_SIZE / tex.GetSize();
    }

    public override void _InputEvent(Viewport viewport, InputEvent e, int shapeIdx)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            _board?.OnTileClicked(this);
    }
}