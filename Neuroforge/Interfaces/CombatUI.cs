using Godot;
using System;
using System.Threading.Tasks;
using static Godot.HttpRequest;

public partial class CombatUI : Control
{
    public event Action OnCombatClosed;

    private Panel _background;
    private Control _popup;

    private Panel _attackerSlot;
    private TextureRect _attackerSprite;
    private Label _attackerLabel;
    private TextureRect _attackerIcon;

    private Panel _defenderSlot;
    private TextureRect _defenderSprite;
    private Label _defenderLabel;
    private TextureRect _defenderIcon;

    private Label _resultLabel;

    private TaskCompletionSource<bool> _clickTcs;
    private TaskCompletionSource<bool> _skipTcs;
    private bool _resultShown = false;

    private static readonly Color COLOR_PIECE_NORMAL = Colors.White;
    private static readonly Color COLOR_PIECE_DEAD = new(0.25f, 0.25f, 0.25f, 1f);
    private static readonly Color COLOR_BG_HIDDEN = new(0, 0, 0, 0);
    private static readonly Color COLOR_BG_VISIBLE = new(0, 0, 0, 0.65f);

    private const float BLINK_HALF = 0.07f;
    private const int BLINK_COUNT = 3;
    private const int REVEAL_DELAY_MS = 300;

    private Color _attackerIconColor;
    private Color _defenderIconColor;

    public override void _Ready()
    {
        _background = GetNode<Panel>("Background");
        _popup = GetNode<Control>("Popup");

        _attackerSlot = GetNode<Panel>("Popup/AttackerScreen");
        _attackerSprite = GetNode<TextureRect>("Popup/AttackerScreen/AttackerPiece");
        _attackerLabel = GetNode<Label>("Popup/AttackerScreen/AttackerName");
        _attackerIcon = GetNode<TextureRect>("Popup/AttackerScreen/AttackerIconBorder/AttackerIcon");
        _attackerIconColor = _attackerIcon.Modulate;

        _defenderSlot = GetNode<Panel>("Popup/DefenderScreen");
        _defenderSprite = GetNode<TextureRect>("Popup/DefenderScreen/DefenderPiece");
        _defenderLabel = GetNode<Label>("Popup/DefenderScreen/DefenderName");
        _defenderIcon = GetNode<TextureRect>("Popup/DefenderScreen/DefenderIconBorder/DefenderIcon");
        _defenderIconColor = _defenderIcon.Modulate;

        _resultLabel = GetNode<Label>("Popup/ResultLabel");

        GuiInput += OnRootInput;
        _background.MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    private void OnRootInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !mb.Pressed
            || mb.ButtonIndex != MouseButton.Left) return;

        if (_resultShown)
        {
            // Resultado ya visible → cerrar
            _clickTcs?.TrySetResult(true);
        }
        else if (_resultLabel.Text == "")
        {
            // Animaciones en curso → skip
            _skipTcs?.TrySetResult(true);
        }
    }

    public async Task ShowCombat(Piece attacker, Piece defender, CombatResult result,
                              bool attackerHidden, bool defenderHidden)
    {
        _clickTcs = new TaskCompletionSource<bool>();
        _skipTcs = new TaskCompletionSource<bool>();
        _resultShown = false;

        PrepareUI(attacker, defender, attackerHidden, defenderHidden);
        await AnimateIn();

        if (attackerHidden || defenderHidden)
        {
            // Espera: delay O skip
            await Task.WhenAny(Task.Delay(REVEAL_DELAY_MS), _skipTcs.Task);

            Task t1 = attackerHidden ? BlinkReveal(_attackerSprite, _attackerLabel, attacker) : Task.CompletedTask;
            Task t2 = defenderHidden ? BlinkReveal(_defenderSprite, _defenderLabel, defender) : Task.CompletedTask;
            await Task.WhenAny(Task.WhenAll(t1, t2), _skipTcs.Task);

            // Si se hizo skip, forzar reveal visual inmediato
            if (_skipTcs.Task.IsCompleted)
            {
                if (attackerHidden) ForceReveal(_attackerSprite, _attackerLabel, attacker);
                if (defenderHidden) ForceReveal(_defenderSprite, _defenderLabel, defender);
            }
        }

        await Task.WhenAny(Task.Delay(REVEAL_DELAY_MS), _skipTcs.Task);

        await ShowResult(attacker, defender, result);
        _resultShown = true;

        await _clickTcs.Task;
        await HideAndClose();
    }

    private void ForceReveal(TextureRect spriteRect, Label label, Piece piece)
    {
        SetSpriteRegion(spriteRect, piece, forceHidden: false);
        spriteRect.Modulate = COLOR_PIECE_NORMAL;
        label.Text = GetPieceLabel(piece);
    }

    // ── Preparar UI ───────────────────────────────────────────────────────────

    private void PrepareUI(Piece attacker, Piece defender, bool attackerHidden, bool defenderHidden)
    {
        Visible = true;

        _background.Modulate = COLOR_BG_HIDDEN;
        _popup.Scale = Vector2.Zero;
        _popup.PivotOffset = _popup.Size / 2f;

        SetSpriteRegion(_attackerSprite, attacker, forceHidden: attackerHidden);
        SetSpriteRegion(_defenderSprite, defender, forceHidden: defenderHidden);

        _attackerSprite.Modulate = COLOR_PIECE_NORMAL;
        _defenderSprite.Modulate = COLOR_PIECE_NORMAL;

        _attackerLabel.Text = attackerHidden ? "" : GetPieceLabel(attacker);
        _defenderLabel.Text = defenderHidden ? "" : GetPieceLabel(defender);

        _attackerIcon.Modulate = _attackerIconColor;
        _defenderIcon.Modulate = _defenderIconColor;

        _resultLabel.Text = "";
    }

    // ── Animación de entrada ──────────────────────────────────────────────────

    private async Task AnimateIn()
    {
        Tween bgTween = CreateTween();
        bgTween.TweenProperty(_background, "modulate", COLOR_BG_VISIBLE, 0.3f)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        _popup.PivotOffset = _popup.Size / 2f;
        Tween popTween = CreateTween();
        popTween.TweenProperty(_popup, "scale", Vector2.One, 0.35f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        await ToSignal(popTween, Tween.SignalName.Finished);
    }

    // ── Parpadeo de revelación ────────────────────────────────────────────────

    private async Task BlinkReveal(TextureRect spriteRect, Label label, Piece piece)
    {
        for (int i = 0; i < BLINK_COUNT; i++)
        {
            Tween t1 = CreateTween();
            t1.TweenProperty(spriteRect, "modulate", Colors.Transparent, BLINK_HALF);
            await ToSignal(t1, Tween.SignalName.Finished);

            Tween t2 = CreateTween();
            t2.TweenProperty(spriteRect, "modulate", COLOR_PIECE_NORMAL, BLINK_HALF);
            await ToSignal(t2, Tween.SignalName.Finished);
        }

        AudioManager.PlaySfx("res://assets/sounds/PieceEffect1.wav");

        Tween tHide = CreateTween();
        tHide.TweenProperty(spriteRect, "modulate", Colors.Transparent, BLINK_HALF);
        await ToSignal(tHide, Tween.SignalName.Finished);

        SetSpriteRegion(spriteRect, piece, forceHidden: false);

        Tween tShow = CreateTween();
        tShow.TweenProperty(spriteRect, "modulate", COLOR_PIECE_NORMAL, BLINK_HALF * 2f);
        await ToSignal(tShow, Tween.SignalName.Finished);

        label.Text = GetPieceLabel(piece);
    }

    // ── Resultado ─────────────────────────────────────────────────────────────

    private async Task ShowResult(Piece attacker, Piece defender, CombatResult result)
    {
        Task dimA = (result == CombatResult.ATTACKER_DIES || result == CombatResult.BOTH_DIE)
            ? DimLoser(_attackerSprite, _attackerIcon) : Task.CompletedTask;
        Task dimD = (result == CombatResult.DEFENDER_DIES || result == CombatResult.BOTH_DIE)
            ? DimLoser(_defenderSprite, _defenderIcon) : Task.CompletedTask;

        await Task.WhenAll(dimA, dimD);

        _resultLabel.Text = GetResultMessage(attacker, defender, result);
    }

    private async Task DimLoser(TextureRect sprite, TextureRect icon)
    {
        for (int i = 0; i < BLINK_COUNT; i++)
        {
            Tween t1 = CreateTween();
            t1.TweenProperty(sprite, "modulate", Colors.Transparent, BLINK_HALF);
            await ToSignal(t1, Tween.SignalName.Finished);

            Tween t2 = CreateTween();
            t2.TweenProperty(sprite, "modulate", COLOR_PIECE_NORMAL, BLINK_HALF);
            await ToSignal(t2, Tween.SignalName.Finished);
        }

        AudioManager.PlaySfx("res://assets/sounds/PieceEffect5.wav");

        Tween tSprite = CreateTween();
        tSprite.TweenProperty(sprite, "modulate", COLOR_PIECE_DEAD, 0.3f)
               .SetTrans(Tween.TransitionType.Sine);

        Tween tIcon = CreateTween();
        tIcon.TweenProperty(icon, "modulate", COLOR_PIECE_DEAD, 0.3f)
             .SetTrans(Tween.TransitionType.Sine);

        await ToSignal(tSprite, Tween.SignalName.Finished);
    }

    // ── Cerrar ────────────────────────────────────────────────────────────────

    private async Task HideAndClose()
    {
        _popup.PivotOffset = _popup.Size / 2f;

        Tween popTween = CreateTween();
        popTween.TweenProperty(_popup, "scale", Vector2.Zero, 0.25f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);

        Tween bgTween = CreateTween();
        bgTween.TweenProperty(_background, "modulate", COLOR_BG_HIDDEN, 0.25f);

        await ToSignal(popTween, Tween.SignalName.Finished);

        Visible = false;
        OnCombatClosed?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetSpriteRegion(TextureRect rect, Piece piece, bool forceHidden)
    {
        int w = PiecesData.ATLAS_COLUMN_WIDTH;
        int h = PiecesData.ATLAS_HEIGHT / 2;

        int col = forceHidden ? PiecesData.HIDDEN_ATLAS_COLUMN : PiecesData.Data[piece.Type].AtlasColumn;
        int y = piece.PlayerOwner == PieceOwner.PLAYER ? 0 : h;

        var atlas = new AtlasTexture();
        atlas.Atlas = PiecesData.Atlas;
        atlas.Region = new Rect2(col * w, y, w, h);

        rect.Texture = atlas;
        rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    }

    private static string GetPieceLabel(Piece piece) => piece.Type switch
    {
        PieceType.ENERGY_CORE => "Energy Core",
        PieceType.TURRET => "Turret",
        PieceType.WAR_MACHINE => "War Machine",
        PieceType.NOVA => "Nova",
        PieceType.MECHA => "Mecha",
        PieceType.SENTINEL => "Sentinel",
        PieceType.CANINE => "Canine",
        PieceType.CYBORG => "Cyborg",
        PieceType.SOLDIER => "Soldier",
        PieceType.SABOTEUR => "Saboteur",
        PieceType.SCOUT => "Scout",
        PieceType.PHANTOM => "Phantom",
        _ => piece.Type.ToString()
    };

    private static string GetResultMessage(Piece attacker, Piece defender, CombatResult result)
    {
        if (defender.Type == PieceType.TURRET && attacker.Type == PieceType.SABOTEUR)
            return "Attacker wins - The saboteur disable the turret";
        if (defender.Type == PieceType.TURRET)
            return "Defender wins - The Turret stops the attack";
        if (attacker.Type == PieceType.PHANTOM && defender.Type == PieceType.WAR_MACHINE)
            return "Attacker wins - The Phantom counters the War Machine";

        return result switch
        {
            CombatResult.DEFENDER_DIES => "Attacker wins - Victory by rank",
            CombatResult.ATTACKER_DIES => "Defender wins - Victory by rank",
            CombatResult.BOTH_DIE => "Draw — Both pieces succumb",
            _ => ""
        };
    }
}