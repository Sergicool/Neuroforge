using Godot;

public partial class Piece : Node2D
{
    private Sprite2D _sprite;
    private const int SPRITE_WIDTH = 16;
    private const int SPRITE_HALF_HEIGHT = 16;

    public PieceType Type { get; private set; }
    public int Rank { get; private set; }
    public bool CanMove { get; private set; }
    public PieceState State { get; private set; }
    public PieceOwner PlayerOwner { get; private set; }
    public Tile CurrentTile { get; set; }

    public override void _Ready()
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");
    }

    public void Initialize(PieceType type, PieceOwner owner)
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");

        Type = type;
        PlayerOwner = owner;

        var def = PiecesData.Data[type];
        Rank = def.Rank;
        CanMove = def.CanMove;

        State = owner == PieceOwner.PLAYER ? PieceState.REVEALED : PieceState.HIDDEN;
        UpdateVisual();
    }

    public bool IsValidMove(Tile tile) => MovementSystem.CanMove(this, tile);

    public CombatResult ResolveCombat(Piece defender) => CombatSystem.Resolve(this, defender);

    public void Reveal()
    {
        if (State == PieceState.REVEALED) return;
        State = PieceState.REVEALED;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        var def = PiecesData.Data[Type];
        _sprite.Texture = PiecesData.Atlas;
        _sprite.RegionEnabled = true;

        int x = State == PieceState.HIDDEN
            ? PiecesData.HIDDEN_ATLAS_COLUMN * SPRITE_WIDTH
            : def.AtlasColumn * SPRITE_WIDTH;
        int y = PlayerOwner == PieceOwner.PLAYER ? 0 : SPRITE_HALF_HEIGHT;

        _sprite.RegionRect = new Rect2(x, y, SPRITE_WIDTH, SPRITE_HALF_HEIGHT);
        _sprite.Scale = new Vector2(4, 4);
    }
}