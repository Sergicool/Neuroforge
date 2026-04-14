using Godot;
using System;
using System.Threading.Tasks;

public partial class MainMenu : Control
{
    private Button _play, _rules, _exit;

    public override void _Ready()
    {
        _play = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/PlayButton");
        _rules = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/RulesButton");
        _exit = GetNode<Button>("ButtonsPanel/MarginContainer/VBoxContainer/ExitButton");

        _play.Pressed += OnPlayPressed;
        _rules.Pressed += OnRulesPressed;
        _exit.Pressed += OnExitPressed;
    }

    private async void OnPlayPressed()
    {
        await SceneManager.GoTo("res://scenes/GameScene.tscn", SceneManager.Transition.Fade, 0.25f);
    }

    private void OnRulesPressed()
    {

    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }
}
