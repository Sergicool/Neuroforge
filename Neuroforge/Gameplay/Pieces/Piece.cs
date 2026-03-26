using Godot;
using System.Collections.Generic;

public partial class Piece : Node2D
{
    private Sprite2D _sprite;

    public PieceOwner PlayerOwner { get; private set; }
    public PieceType Type        { get; private set; }
    public int Rank              { get; private set; }
    public bool CanMove          { get; private set; }

    // Una pieza es visible para el jugador si es suya o ha sido revelada en combate
    public bool IsRevealed       { get; private set; }

    public Tile CurrentTile { get; set; }

    private readonly Dictionary<Vector2I, int> _tileCooldowns = new();

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

        // Las piezas del jugador empiezan visibles para él; las del bot, ocultas
        IsRevealed = owner == PieceOwner.PLAYER;

        UpdateVisual();
    }

    // Devuelve el resultado del combate contra un defensor
    public CombatResult ResolveCombat(Piece defender) => CombatSystem.Resolve(this, defender);

    // Revela la pieza para ambos jugadores (ocurre al combatir)
    public void Reveal()
    {
        if (IsRevealed) return;
        IsRevealed = true;
        UpdateVisual();
    }

    // Registra la casilla de la que sale la pieza y limpia cooldowns expirados
    public void RegisterTileExit(Vector2I tile, int turn)
    {
        _tileCooldowns[tile] = turn;
        CleanupCooldowns(turn);
    }

    // Devuelve true si la pieza puede volver a esa casilla en el turno actual
    public bool CanReturnToTile(Vector2I tile, int currentTurn)
    {
        if (!_tileCooldowns.TryGetValue(tile, out int lastTurn)) return true;
        return (currentTurn - lastTurn) >= 3;
    }

    // Elimina cooldowns que ya han expirado (llamado únicamente desde RegisterTileExit)
    private void CleanupCooldowns(int currentTurn)
    {
        var toRemove = new List<Vector2I>();

        foreach (var kvp in _tileCooldowns)
            if (currentTurn - kvp.Value >= 3)
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

        // Las piezas del bot no reveladas muestran el sprite oculto
        bool showHidden = PlayerOwner == PieceOwner.BOT && !IsRevealed;
        int x = showHidden
            ? PiecesData.HIDDEN_ATLAS_COLUMN * spriteWidth
            : def.AtlasColumn * spriteWidth;

        int y = PlayerOwner == PieceOwner.PLAYER ? 0 : spriteHeight;

        _sprite.RegionRect = new Rect2(x, y, spriteWidth, spriteHeight);
        _sprite.Scale      = new Vector2(4, 4);
    }
}