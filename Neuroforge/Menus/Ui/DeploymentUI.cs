using Godot;
using System;
using System.Collections.Generic;
using static Godot.Control;

public partial class DeploymentUI : CanvasLayer
{
    public event Action             OnRandomPressed;
    public event Action             OnStartPressed;
    public event Action<PieceType>  OnPieceSelected;

    private Label         _remainingLabel;
    private GridContainer _gridContainer;
    private Button        _randomButton;
    private Button        _startButton;

    private readonly Dictionary<PieceType, Button> _pieceButtons = new();

    public override void _Ready()
    {
        _remainingLabel = GetNode<Label>        ("Control/MarginContainer/Panel/VBoxContainer/PiecesCountLabel");
        _gridContainer  = GetNode<GridContainer>("Control/MarginContainer/Panel/VBoxContainer/GridContainer");
        _randomButton   = GetNode<Button>       ("Control/MarginContainer/Panel/VBoxContainer/RandomButton");
        _startButton    = GetNode<Button>       ("Control/MarginContainer/Panel/VBoxContainer/StartButton");

        foreach (var kv in PiecesData.Data)
        {
            PieceType type = kv.Key;
            Button btn = new Button
            {
                Text               = BuildPieceText(type, kv.Value.MaxCount),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            btn.Pressed += () => OnPieceSelected?.Invoke(type);
            _pieceButtons[type] = btn;
            _gridContainer.AddChild(btn);
        }

        _randomButton.Pressed += () => OnRandomPressed?.Invoke();
        _startButton.Pressed  += () => OnStartPressed?.Invoke();
        _startButton.Disabled  = true;
    }

    public GridContainer GetGridContainer() => _gridContainer;

    public void SetRemainingPieces(int remaining)
    {
        _remainingLabel.Text  = $"Pieces Remaining: {remaining}";
        _startButton.Disabled = remaining > 0;
    }

    public void ShowUI() => Visible = true;
    public void HideUI() => Visible = false;

    public void UpdatePieceCounts(Dictionary<PieceType, int> remaining)
    {
        foreach (var kv in remaining)
        {
            if (!_pieceButtons.TryGetValue(kv.Key, out var btn)) continue;
            btn.Text     = BuildPieceText(kv.Key, kv.Value);
            btn.Disabled = kv.Value == 0;
        }
    }

    private string BuildPieceText(PieceType type, int remaining)
    {
        var def      = PiecesData.Data[type];
        string rank  = def.Rank > 0 ? $" [{def.Rank}]" : "";
        return $"{type}{rank} x{remaining:00}";
    }
}