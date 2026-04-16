using Godot;
using System;
using System.Threading.Tasks;

public partial class PauseMenu : Control
{
    private static readonly Color COLOR_BG_HIDDEN = new(0, 0, 0, 0);
    private static readonly Color COLOR_BG_VISIBLE = new(0, 0, 0, 0.65f);

    private Panel _background, _popup;
    private Button _resume, _rules, _menu;
    private GameScene _gameScene;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _background = GetNode<Panel>("Background");
        _popup = GetNode<Panel>("PopUp");

        _resume = GetNode<Button>("PopUp/MarginContainer/VBoxContainer/PlayButton");
        _rules = GetNode<Button>("PopUp/MarginContainer/VBoxContainer/RulesButton");
        _menu = GetNode<Button>("PopUp/MarginContainer/VBoxContainer/ExitButton");

        _rules.Pressed += OnRulesPressed;
        _menu.Pressed += OnMenuPressed;
    }

    public void Init(GameScene parentScene)
    {
        _gameScene = parentScene;
        _resume.Pressed += parentScene.ResumeGame;
    }

    private async void OnRulesPressed()
    {
        await SceneManager.GoTo("res://scenes/RulesScene.tscn", SceneManager.Transition.Fade, 0.15f);

    }

    private async void OnMenuPressed()
    {
        GetTree().Paused = false;
        await SceneManager.GoBack(SceneManager.Transition.Fade, 0.25f);
    }

    public async Task ShowMenu()
    {
        Visible = true;
        _popup.Scale = Vector2.Zero;
        _background.Modulate = COLOR_BG_HIDDEN;

        await AnimateIn();
    }

    public async Task HideMenu()
    {
        await HideAndClose();

        Visible = false;
    }

    private async Task AnimateIn()
    {
        _resume.Disabled = false;
        _rules.Disabled = false;
        _menu.Disabled = false;

        _popup.PivotOffset = _popup.Size / 2f;

        Tween bgTween = CreateTween();
        bgTween.TweenProperty(_background, "modulate", COLOR_BG_VISIBLE, 0.3f)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        Tween popTween = CreateTween();
        popTween.TweenProperty(_popup, "scale", Vector2.One, 0.35f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        await ToSignal(popTween, Tween.SignalName.Finished);
    }

    private async Task HideAndClose()
    {
        _resume.Disabled = true;
        _rules.Disabled = true;
        _menu.Disabled = true;

        _popup.PivotOffset = _popup.Size / 2f;

        Tween popTween = CreateTween();
        popTween.TweenProperty(_popup, "scale", Vector2.Zero, 0.25f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);

        Tween bgTween = CreateTween();
        bgTween.TweenProperty(_background, "modulate", COLOR_BG_HIDDEN, 0.25f);

        await ToSignal(popTween, Tween.SignalName.Finished);
    }
}
