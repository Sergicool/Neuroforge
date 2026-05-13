using Godot;
using System;
using System.Threading.Tasks;

public partial class EndGameUI : Control
{
    private Panel _background, _popup;
    private Label _message;
    private Button _play, _menu;

    private static readonly Color COLOR_BG_VISIBLE = new(0, 0, 0, 0.75f);

    public override void _Ready()
    {
        _background = GetNode<Panel>("Background");
        _popup = GetNode<Panel>("PopUp");
        _message = GetNode<Label>("PopUp/Label");
        _play = GetNode<Button>("PopUp/Panel2/MarginContainer/HBoxContainer/Button");
        _menu = GetNode<Button>("PopUp/Panel2/MarginContainer/HBoxContainer/Button2");

        _play.Pressed += OnRetryPressed;
        _play.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");

        _menu.Pressed += OnMenuPressed;
        _menu.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");

        Visible = false;
        _background.Modulate = new Color(1, 1, 1, 0);
        _popup.Scale = Vector2.Zero;
    }

    public async void Show(string finalMessage, int result)
    {
        _message.Text = finalMessage;

        Color colorTarget = result switch
        {
            1 => new Color(0.427f, 1.0f, 0.378f), // Verde
            0 => new Color(0.844f, 0.158f, 0.164f), // Rojo
            _ => new Color(0.454f, 0.425f, 0.746f)  // Morado
        };
        _message.AddThemeColorOverride("font_color", colorTarget);

        float textWidth = _message.GetCombinedMinimumSize().X;

        float newWidth = textWidth * 1.25f;

        _popup.CustomMinimumSize = new Vector2(newWidth, _popup.CustomMinimumSize.Y);

        Visible = true;
        _popup.PivotOffset = _popup.Size / 2f;
        _popup.Scale = Vector2.Zero;

        Tween t = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenProperty(_background, "modulate", COLOR_BG_VISIBLE, 0.4f);
        t.TweenProperty(_popup, "scale", Vector2.One, 0.5f);
    }

    private void OnRetryPressed()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    private async void OnMenuPressed()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        GetTree().Paused = false;
        await SceneManager.GoBack(SceneManager.Transition.Fade, 0.25f);
    }
}