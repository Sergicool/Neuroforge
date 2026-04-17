// RulesOverlay.cs
using Godot;
using System.Threading.Tasks;

public partial class RulesOverlay : Control
{
    private static readonly Color COLOR_BG_HIDDEN = new(0, 0, 0, 0);
    private static readonly Color COLOR_BG_VISIBLE = new(0, 0, 0, 0.65f);

    private Panel _background, _popup;
    private Button _back;

    private GridContainer _piecesGrid;
    private Control[] _piecePanels;
    private Button[] _pieceButtons;
    private int _selectedPiece = 0;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _background = GetNode<Panel>("Background");
        _popup = GetNode<Panel>("PopUp");
        _back = GetNode<Button>("PopUp/BackButton");
        _back.Pressed += async () => await HideOverlay();

        _piecesGrid = GetNode<GridContainer>("PopUp/TabContainer/Tab5/GridContainer");

        // Recoge todos los botones hijos del grid
        var buttons = new System.Collections.Generic.List<Button>();
        foreach (Node child in _piecesGrid.GetChildren())
            if (child is Button btn) buttons.Add(btn);
        _pieceButtons = buttons.ToArray();

        // Recoge los paneles (Nombres: PiecePanel0, PiecePanel1…)
        var tab5 = _piecesGrid.GetParent();
        _piecePanels = new Control[_pieceButtons.Length];
        for (int i = 0; i < _pieceButtons.Length; i++)
            _piecePanels[i] = tab5.GetNode<Control>($"PiecePanel{i}");

        for (int i = 0; i < _pieceButtons.Length; i++)
        {
            _pieceButtons[i].ToggleMode = true;
            int index = i;
            _pieceButtons[i].Pressed += () => SelectPiece(index);
        }

        SelectPiece(0);

        Visible = false;
    }

    private void SelectPiece(int index)
    {
        _selectedPiece = index;

        for (int i = 0; i < _piecePanels.Length; i++)
        {
            _piecePanels[i].Visible = (i == index);
            _pieceButtons[i].ButtonPressed = (i == index);
        }
    }

    public async Task ShowOverlay()
    {
        Visible = true;
        _popup.Scale = Vector2.Zero;
        _background.Modulate = COLOR_BG_HIDDEN;
        _popup.PivotOffset = _popup.Size / 2f;
        _back.Disabled = false;

        Tween bgTween = CreateTween();
        bgTween.TweenProperty(_background, "modulate", COLOR_BG_VISIBLE, 0.3f)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        Tween popTween = CreateTween();
        popTween.TweenProperty(_popup, "scale", Vector2.One, 0.35f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        await ToSignal(popTween, Tween.SignalName.Finished);
    }

    public async Task HideOverlay()
    {
        _back.Disabled = true;
        _popup.PivotOffset = _popup.Size / 2f;

        Tween popTween = CreateTween();
        popTween.TweenProperty(_popup, "scale", Vector2.Zero, 0.25f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);

        Tween bgTween = CreateTween();
        bgTween.TweenProperty(_background, "modulate", COLOR_BG_HIDDEN, 0.25f);

        await ToSignal(popTween, Tween.SignalName.Finished);
        Visible = false;
    }


}