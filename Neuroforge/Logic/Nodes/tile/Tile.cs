using Godot;

public partial class Tile : Area2D
{
    private static readonly Color MOVEMENT_MARKER_COLOR = new Color(0.598f, 1.0f, 0.226f);
    private static readonly Color ATTACK_MARKER_COLOR = new Color(1.0f, 0.004f, 0.071f);

    private Board _board;
    private AnimatedSprite2D _marker;
    private CollisionShape2D _collisionShape;

    public Vector2I GridPosition { get; set; }
    public TileType TileType { get; private set; }
    public Piece Occupant { get; private set; }
    public bool IsOccupied => Occupant != null;

    public override void _Ready()
    {
        _marker = GetNode<AnimatedSprite2D>("Marker");
        _marker.Visible = false;
        _collisionShape = GetNode<CollisionShape2D>("CollisionShape2D");

        // Ajustar el tamaño de la colisión al tamaño de celda definido en Board
        if (_collisionShape.Shape is RectangleShape2D rect)
        {
            // Si el TILE_SIZE es 48 y el rect de 20, escalamos a 2.4
            _collisionShape.Scale = Board.TILE_SIZE / rect.Size;
        }
    }

    public void Initialize(Board board) => _board = board;

    public void SetType(TileType type) => TileType = type;

    public void SetOccupant(Piece piece)
    {
        Occupant = piece;
        if (piece != null) piece.CurrentTile = this;
    }

    public void ClearOccupant() => Occupant = null;

    // ========== Resaltados ==========
    public void ClearHighlight() => _marker.Visible = false;

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

    public override void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            _board?.OnTileClicked(this);
        }
    }
}