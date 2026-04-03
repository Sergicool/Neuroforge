using Godot;
using System.Collections.Generic;

public partial class Piece : Node2D
{
    private Sprite2D _sprite;

    public PieceOwner PlayerOwner { get; private set; }
    public PieceType Type        { get; private set; }
    public int Rank              { get; private set; }
    public bool CanMove          { get; private set; }

    // Controla el render: las piezas del jugador siempre son visibles para él en pantalla.
    // Las piezas del bot se muestran ocultas hasta que combaten.
    public bool IsVisibleToPlayer { get; private set; }

    // Controla el conocimiento real del bot: solo true tras haber combatido con la pieza.
    // El bot NO conoce las piezas del jugador por el hecho de que estén visibles en pantalla.
    public bool IsRevealedToBot   { get; private set; }

    public Tile CurrentTile { get; set; }

    private readonly Dictionary<Tile, int> _tileCooldowns = new();

    public override void _Ready()
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");
    }

    // Inicializa una pieza dado su tipo y propietario
    public void Initialize(PieceType type, PieceOwner owner)
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");

        PlayerOwner = owner;
        Type        = type;

        var def = PiecesData.Data[type];
        Rank    = def.Rank;
        CanMove = def.CanMove;

        // Las piezas del jugador siempre son visibles en pantalla para él.
        // El bot no conoce ninguna pieza del jugador hasta que combate con ella.
        IsVisibleToPlayer = owner == PieceOwner.PLAYER;
        IsRevealedToBot   = owner == PieceOwner.BOT; // Las propias del bot siempre las conoce

        UpdateVisual();
    }

    // Devuelve el resultado del combate contra un defensor
    public CombatResult ResolveCombat(Piece defender) => CombatSystem.Resolve(this, defender);

    // Llamado al combatir: revela la pieza para ambos bandos.
    // El jugador puede ver las piezas del bot, y el bot aprende el tipo/rango de las del jugador.
    public void Reveal()
    {
        if (IsVisibleToPlayer && IsRevealedToBot) return;
        IsVisibleToPlayer = true;
        IsRevealedToBot   = true;
        UpdateVisual();
    }

    // Registra la casilla de la que sale la pieza y limpia cooldowns expirados
    public void RegisterTileExit(Tile tile, int turn)
    {
        _tileCooldowns[tile] = turn;
        CleanupCooldowns(turn);
    }

    // Devuelve true si la pieza puede volver a esa casilla en el turno actual
    public bool CanReturnToTile(Tile tile, int turn)
    {
        if (!_tileCooldowns.TryGetValue(tile, out int lastTurn)) return true;
        return (turn - lastTurn) >= 3;
    }

    // Elimina cooldowns que ya han expirado (llamado únicamente desde RegisterTileExit)
    private void CleanupCooldowns(int turn)
    {
        var toRemove = new List<Tile>();

        foreach (var kvp in _tileCooldowns)
            if (turn - kvp.Value >= 3)
                toRemove.Add(kvp.Key);

        foreach (var t in toRemove)
            _tileCooldowns.Remove(t);
    }

    // Actualiza el sprite según propietario y estado de revelación
    private void UpdateVisual()
    {
        int spriteWidth  = PiecesData.ATLAS_COLUMN_WIDTH;
        int spriteHeight = PiecesData.ATLAS_HEIGHT / 2;

        var def = PiecesData.Data[Type];
        _sprite.Texture      = PiecesData.Atlas;
        _sprite.RegionEnabled = true;

        // Las piezas del bot se muestran ocultas hasta que el jugador las revela en combate
        bool showHidden = PlayerOwner == PieceOwner.BOT && !IsVisibleToPlayer;
        int x = showHidden
            ? PiecesData.HIDDEN_ATLAS_COLUMN * spriteWidth
            : def.AtlasColumn * spriteWidth;

        int y = PlayerOwner == PieceOwner.PLAYER ? 0 : spriteHeight;

        _sprite.RegionRect = new Rect2(x, y, spriteWidth, spriteHeight);
        _sprite.Scale      = new Vector2(3.6f, 3.6f);
    }
}