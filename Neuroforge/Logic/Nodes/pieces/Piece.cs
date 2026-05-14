using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class Piece : Node2D
{
    private Sprite2D _sprite;
    private Sprite2D _spriteBorder;

    public PieceOwner PlayerOwner { get; private set; }
    public PieceType Type { get; private set; }
    public int Rank { get; private set; }
    public bool CanMove { get; private set; }

    // Controla el render: las piezas del jugador siempre son visibles para él en pantalla.
    // Las piezas del bot se muestran ocultas hasta que combaten.
    public bool IsVisibleToPlayer { get; private set; }

    // Controla el conocimiento real del bot: solo true tras haber combatido con la pieza.
    // El bot NO conoce las piezas del jugador por el hecho de que estén visibles en pantalla.
    public bool IsRevealedToBot { get; private set; }

    public Tile CurrentTile { get; set; }

    // Historial de los últimos movimientos: (origen, destino).
    // Se guardan hasta 3 entradas (los 3 movimientos previos que nos interesan).
    private const int MOVE_HISTORY_SIZE = 3;
    private readonly List<(Tile From, Tile To)> _moveHistory = new();

    // Número de turno global en el que se realizó cada movimiento del historial.
    // Si entre dos entradas del historial hubo un turno en el que esta pieza NO movió,
    // la racha de oscilación se considera interrumpida y se resetea.
    private readonly List<int> _moveTurnNumbers = new();

    public override void _Ready()
    {
        _sprite ??= GetNode<Sprite2D>("Sprite2D");
        _spriteBorder = GetNode<Sprite2D>("SpriteBorder");
        _spriteBorder.Visible = false;
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

        // Las piezas del jugador siempre son visibles en pantalla para él.
        // El bot no conoce ninguna pieza del jugador hasta que combate con ella.
        IsVisibleToPlayer = owner == PieceOwner.PLAYER;
        IsRevealedToBot = owner == PieceOwner.BOT; // Las propias del bot siempre las conoce

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
        IsRevealedToBot = true;
        UpdateVisual();
    }

    public async Task AnimateBlinkReveal()
    {

        // Animacion intercalando el sprite normal con el oculto
        int blinkCount = 3;
        float blinkInterval = 0.07f;

        for (int i = 0; i < blinkCount; i++)
        {
            // Mostrar sprite revelado temporalmente
            UpdateVisual();
            await ToSignal(GetTree().CreateTimer(blinkInterval), SceneTreeTimer.SignalName.Timeout);

            // Volver a oculto, forzando el sprite a estado "oculto" sin cambiar las flags
            ShowHiddenSprite();
            await ToSignal(GetTree().CreateTimer(blinkInterval), SceneTreeTimer.SignalName.Timeout);
        }

        AudioManager.PlaySfx("res://assets/sounds/PieceEffect1.wav");

        // Revelar pieza
        Reveal();
    }

    private void ShowHiddenSprite()
    {
        int spriteWidth = PiecesData.ATLAS_COLUMN_WIDTH;
        int spriteHeight = PiecesData.ATLAS_HEIGHT / 2;

        _sprite.RegionRect = new Rect2(
            PiecesData.HIDDEN_ATLAS_COLUMN * spriteWidth,
            PlayerOwner == PieceOwner.PLAYER ? 0 : spriteHeight,
            spriteWidth,
            spriteHeight
        );
    }

    // Registra el movimiento realizado en el historial
    public void RegisterMove(Tile from, Tile to, int turnNumber = -1)
    {
        _moveHistory.Add((from, to));
        _moveTurnNumbers.Add(turnNumber);
        if (_moveHistory.Count > MOVE_HISTORY_SIZE)
        {
            _moveHistory.RemoveAt(0);
            _moveTurnNumbers.RemoveAt(0);
        }
    }

    // Devuelve true si ejecutar 'from→to' sería el 4.º movimiento consecutivo
    // entre las mismas 2 casillas (A→B, B→A, A→B → bloquea B→A).
    // "Consecutivo" significa que esta pieza movió en cada uno de esos turnos sin saltarse ninguno.
    public bool IsOscillating(Tile from, Tile to, int currentTurn)
    {
        if (_moveHistory.Count < 3) return false;

        // 1. Verificación de Reseteo: 
        // Si el último movimiento de ESTA pieza no fue en el turno anterior del jugador,
        // la racha se ha roto. 
        // Como TurnNumber aumenta cada vez que el Jugador empieza, la diferencia debe ser 1.
        int lastMoveTurn = _moveTurnNumbers[_moveTurnNumbers.Count - 1];
        if (currentTurn - lastMoveTurn > 1) return false;

        // 2. Verificar que los 3 movimientos en el historial fueron consecutivos para la pieza
        for (int i = 1; i < _moveTurnNumbers.Count; i++)
        {
            if (_moveTurnNumbers[i] - _moveTurnNumbers[i - 1] != 1) return false;
        }

        // 3. Verificar patrón A-B, B-A, A-B
        Tile tileA = _moveHistory[0].From;
        Tile tileB = _moveHistory[0].To;

        // El patrón debe ser: 
        // H0: A -> B
        // H1: B -> A
        // H2: A -> B
        // Intento actual: B -> A (Este es el que queremos bloquear)

        bool patternMatch = _moveHistory[0].From == tileA && _moveHistory[0].To == tileB &&
                            _moveHistory[1].From == tileB && _moveHistory[1].To == tileA &&
                            _moveHistory[2].From == tileA && _moveHistory[2].To == tileB &&
                            from == tileB && to == tileA;

        return patternMatch;
    }

    // Actualiza el sprite según propietario y estado de revelación
    private void UpdateVisual()
    {
        int spriteWidth = PiecesData.ATLAS_COLUMN_WIDTH;
        int spriteHeight = PiecesData.ATLAS_HEIGHT / 2;

        var def = PiecesData.Data[Type];

        _sprite.Texture = PiecesData.Atlas;
        _sprite.RegionEnabled = true;

        bool showHidden = PlayerOwner == PieceOwner.BOT && !IsVisibleToPlayer;
        int x = showHidden
            ? PiecesData.HIDDEN_ATLAS_COLUMN * spriteWidth
            : def.AtlasColumn * spriteWidth;
        int y = PlayerOwner == PieceOwner.PLAYER ? 0 : spriteHeight;

        _sprite.RegionRect = new Rect2(x, y, spriteWidth, spriteHeight);

        if (_spriteBorder != null)
        {
            bool knowsMe = PlayerOwner == PieceOwner.PLAYER && IsRevealedToBot;
            _spriteBorder.Visible = knowsMe;
        }
    }

    // Mueve la pieza visualmente hasta una posición destino
    public async Task AnimateMoveTo(Vector2 targetPos, float duration = 0.25f)
    {
        Tween tween = CreateTween();
        tween.TweenProperty(this, "position", targetPos, duration)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);
        await ToSignal(tween, Tween.SignalName.Finished);
    }
}