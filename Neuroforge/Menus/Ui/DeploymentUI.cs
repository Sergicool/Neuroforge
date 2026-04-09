using Godot;
using System;
using System.Collections.Generic;

public partial class DeploymentUI : Control
{
    public event Action OnRandomPressed;
    public event Action OnStartPressed;
    public event Action<PieceType> OnPieceSelected;

    // Ruta a la escena del botón — ajusta según tu estructura de carpetas
    private const string PIECE_BUTTON_SCENE = "res://Menus/Ui/PieceButton.tscn";

    private Label _remainingLabel;
    private Button _randomButton;
    private Button _startButton;
    private GridContainer _pieceGrid;        // grid donde van los PieceButton

    private readonly Dictionary<PieceType, PieceButton> _pieceButtons = new();
    private PieceType? _activeType = null;

    private PackedScene _pieceButtonScene;

    public override void _Ready()
    {
        _pieceButtonScene = GD.Load<PackedScene>(PIECE_BUTTON_SCENE);

        _remainingLabel = GetNode<Label>("MarginContainer/VBoxContainer/RemainingLabel");
        _pieceGrid = GetNode<GridContainer>("MarginContainer/VBoxContainer/PieceGrid");
        _randomButton = GetNode<Button>("MarginContainer/VBoxContainer/RandomButton");
        _startButton = GetNode<Button>("MarginContainer/VBoxContainer/StartButton");

        foreach (var kv in PiecesData.Data)
        {
            PieceType type = kv.Key;

            PieceButton btn = _pieceButtonScene.Instantiate<PieceButton>();
            _pieceGrid.AddChild(btn);
            btn.Setup(kv.Value, PieceOwner.PLAYER);

            btn.Toggled += (pressed) => OnPieceToggled(type, pressed);
            _pieceButtons[type] = btn;
        }

        _randomButton.Pressed += () => { OnRandomPressed?.Invoke(); ClearActiveButton(); };
        _startButton.Pressed += () => OnStartPressed?.Invoke();
        _startButton.Disabled = true;
    }

    // ── Toggle exclusivo ──────────────────────────────────────────────────────

    // TODO: Arreglar que al deseleccionar una pieza se pueda seguir colocando aunque el boton de dicha pieza no este activada
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
        _remainingLabel.Text = $"Restantes: {remaining}";
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