using Godot;
using System;

public partial class DeploymentUI : CanvasLayer
{
    public static DeploymentUI Instance { get; private set; }
    public event Action OnRandomPressed;
    public event Action OnStartPressed;

    private Button _randomButton;
    private Button _startButton;
    private Label _remainingLabel;

    public override void _Ready()
    {
        Instance = this;
        _randomButton = GetNode<Button>("Control/MarginContainer/Panel/VBoxContainer/RandomButton");
        _startButton = GetNode<Button>("Control/MarginContainer/Panel/VBoxContainer/StartButton");
        _remainingLabel = GetNode<Label>("Control/MarginContainer/Panel/VBoxContainer/PiecesCountLabel");

        _randomButton.Pressed += () => OnRandomPressed?.Invoke();
        _startButton.Pressed += () => OnStartPressed?.Invoke();

        _startButton.Disabled = true;
    }

    public void SetRemainingPieces(int remaining)
    {
        _remainingLabel.Text = $"Pieces Remaining: {remaining}";
        _startButton.Disabled = remaining > 0;
    }

    public void ShowUI() => Visible = true;
    public void HideUI() => Visible = false;
}