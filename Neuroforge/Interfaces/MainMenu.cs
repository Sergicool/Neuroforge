using Godot;
using System;
using System.Threading.Tasks;

public partial class MainMenu : Control
{
    [Export] private PackedScene GAME_SCENE;

    private Button _play, _rules, _options, _exit;
    private RulesOverlay _rulesOverlay;
    private OptionsOverlay _optionsOverlay;

    public override void _Ready()
    {
        _play = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/PlayButton");
        _rules = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/RulesButton");
        _exit = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/ExitButton");
        _options = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/OptionsButton");

        _rulesOverlay = GetNode<RulesOverlay>("RulesOverlay");
        _optionsOverlay = GetNode<OptionsOverlay>("OptionsOverlay");

        _play.Pressed += OnPlayPressed;
        _play.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _rules.Pressed += OnRulesPressed;
        _rules.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _options.Pressed += OnOptionsPressed;
        _options.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _exit.Pressed += OnExitPressed;
        _exit.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");

        AudioManager.PlayMusic("res://assets/sounds/MainMenuMusic.wav");
    }

    private async void OnPlayPressed()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        await SceneManager.GoTo(GAME_SCENE.ResourcePath, SceneManager.Transition.Fade, 0.25f);
    }

    private async void OnRulesPressed()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        await _rulesOverlay.ShowOverlay();
    }

    private async void OnOptionsPressed()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        await _optionsOverlay.ShowOverlay();
    }

    private void OnExitPressed()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        GetTree().Quit();
    }
}
