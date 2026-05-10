using Godot;
using System;
using System.Threading.Tasks;

public partial class MainMenu : Control
{
    [Export] private PackedScene GAME_SCENE;

    private Button _play, _rules, _exit;
    private RulesOverlay _rulesOverlay;

    public override void _Ready()
    {
        _play = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/PlayButton");
        _rules = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/RulesButton");
        _exit = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/ExitButton");

        _rulesOverlay = GetNode<RulesOverlay>("RulesOverlay");

        _play.Pressed += OnPlayPressed;
        _rules.Pressed += OnRulesPressed;
        _exit.Pressed += OnExitPressed;
    }

    private async void OnPlayPressed()
    {
        await SceneManager.GoTo(GAME_SCENE.ResourcePath, SceneManager.Transition.Fade, 0.25f);
    }

    private async void OnRulesPressed()
    {
        await _rulesOverlay.ShowOverlay();
    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }
}
