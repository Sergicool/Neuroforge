using Godot;
using System;
using System.Collections.Generic;

public partial class DeploymentUI : Control
{
    [Export] private PackedScene PIECE_BUTTON_SCENE;

    public event Action OnRandomPressed;
    public event Action OnStartPressed;
    public event Action<PieceType> OnPieceSelected;

    private Label _remainingLabel;
    private Button _randomButton;
    private Button _startButton;
    private GridContainer _pieceGrid;        // grid donde van los PieceButton

    private readonly Dictionary<PieceType, PieceButton> _pieceButtons = new();
    private PieceType? _activeType = null;

    public override void _Ready()
    {
        _remainingLabel = GetNode<Label>("MarginContainer/VBoxContainer/RemainingLabel");
        _pieceGrid = GetNode<GridContainer>("MarginContainer/VBoxContainer/PieceGrid");
        _randomButton = GetNode<Button>("MarginContainer/VBoxContainer/RandomButton");
        _startButton = GetNode<Button>("MarginContainer/VBoxContainer/StartButton");

        foreach (var kv in PiecesData.Data)
        {
            PieceType type = kv.Key;

            PieceButton btn = PIECE_BUTTON_SCENE.Instantiate<PieceButton>();
            _pieceGrid.AddChild(btn);
            btn.Setup(kv.Value, PieceOwner.PLAYER);

            btn.Toggled += (pressed) => OnPieceToggled(type, pressed);
            _pieceButtons[type] = btn;
        }

        _randomButton.Pressed += () => {
            AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
            OnRandomPressed?.Invoke(); 
            ClearActiveButton(); 
        };
        _randomButton.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _startButton.Pressed += () =>
        {
            AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
            OnStartPressed?.Invoke();
        };
        _startButton.MouseEntered += () =>
        {
            if (!_startButton.Disabled)
                AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        };
        _startButton.Disabled = true;
    }

    // ── Toggle exclusivo ──────────────────────────────────────────────────────

    private void OnPieceToggled(PieceType type, bool pressed)
    {
        if (!pressed)
        {
            if (_activeType == type) _activeType = null;
            return;
        }

        if (_activeType.HasValue && _activeType != type)
            _pieceButtons[_activeType.Value].SetPressedNoSignal(false);

        _activeType = type;
        OnPieceSelected?.Invoke(type);
    }

    // Limpia la selección visual (usado tras Random, que coloca todo automáticamente)
    private void ClearActiveButton()
    {
        if (_activeType.HasValue)
            _pieceButtons[_activeType.Value].SetPressedNoSignal(false);
        _activeType = null;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void SetRemainingPieces(int remaining)
    {
        _remainingLabel.Text = $"Remaining: {remaining}";
        _startButton.Disabled = remaining > 0;
    }

    public void UpdatePieceCounts(Dictionary<PieceType, int> remaining)
    {
        foreach (var kv in remaining)
        {
            if (!_pieceButtons.TryGetValue(kv.Key, out var btn)) continue;
            btn.SetCount(kv.Value);

            if (kv.Value == 0 && _activeType == kv.Key)
            {
                btn.SetPressedNoSignal(false);
                _activeType = null;
            }
        }
    }

    // Sincroniza el botón activo cuando el controller cambia la selección
    // (ej: al quitar una pieza del tablero se reactiva ese tipo)
    public void SetActiveType(PieceType? type)
    {
        if (_activeType.HasValue && _activeType != type)
            _pieceButtons[_activeType.Value].SetPressedNoSignal(false);

        _activeType = type;

        if (type.HasValue && _pieceButtons.TryGetValue(type.Value, out var btn))
            btn.SetPressedNoSignal(true);
    }

    public void ShowUI() => Visible = true;
    public void HideUI() => Visible = false;
}