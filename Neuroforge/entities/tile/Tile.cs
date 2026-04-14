using Godot;

public partial class Tile : Area2D
{
    private static readonly Texture2D PASSABLE_TILE  = GD.Load<Texture2D>("res://assets/board/PassableTile.png");
    private static readonly Texture2D NO_PASSABLE_TILE  = GD.Load<Texture2D>("res://assets/board/NoPassableTile.png");
    private static readonly Color MOVEMENT_MARKER_COLOR = new Color(0.659f, 1.0f, 0.337f);
    private static readonly Color ATTACK_MARKER_COLOR = new Color(1.0f, 0.004f, 0.071f);

    private Board    _board;
    private Sprite2D _sprite;
    private AnimatedSprite2D _marker;
    private CollisionShape2D _collisionShape;
    private Texture2D _baseTexture;

    public Vector2I GridPosition { get; set; }
    public TileType TileType     { get; private set; }
    public Piece    Occupant     { get; private set; }
    public bool     IsOccupied   => Occupant != null;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _marker = GetNode<AnimatedSprite2D>("Marker");
        _marker.Visible = false;
        _collisionShape = GetNode<CollisionShape2D>("CollisionShape2D");
        if (_baseTexture != null) ApplyTexture(_baseTexture);
    }

    public void Initialize(Board board) => _board = board;

    // Establece el tipo de casilla y su textura base
    public void SetType(TileType type)
    {
        TileType     = type;
        _baseTexture = type == TileType.NO_PASSABLE ? NO_PASSABLE_TILE : PASSABLE_TILE;
        if (IsInsideTree())
            ApplyTexture(_baseTexture);
    }

    public void SetOccupant(Piece piece)
    {
        Occupant = piece;
        if (piece != null) piece.CurrentTile = this;
    }

    public void ClearOccupant() => Occupant = null;

    private void ApplyTexture(Texture2D tex)
    {
        _sprite.Texture = tex;
        _sprite.Scale = Board.TILE_SIZE / tex.GetSize();
        _collisionShape.Scale = _sprite.Scale;
    }

    // ========== Resaltados ==========
    public void ClearHighlight()  => _marker.Visible = false;

    public void HighlightMove()
    {
        _marker.Modulate = MOVEMENT_MARKER_COLOR;
        _marker.Visible = true;
    }

    public void HighlightAttack()
    {
        _marker.Modulate = ATTACK_MARKER_COLOR;
        _marker.Visible = true;
    }

    public override void _InputEvent(Viewport viewport, InputEvent e, int shapeIdx)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            _board?.OnTileClicked(this);
    }
}