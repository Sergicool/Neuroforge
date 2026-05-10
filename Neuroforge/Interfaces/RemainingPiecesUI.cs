using Godot;
using System.Collections.Generic;

public partial class RemainingPiecesUI : Control
{
    [Export] private PackedScene PIECE_BUTTON_SCENE;

    private GridContainer _botGrid;
    private GridContainer _playerGrid;

    private readonly Dictionary<PieceType, PieceButton> _botButtons = new();
    private readonly Dictionary<PieceType, PieceButton> _playerButtons = new();

    public override void _Ready()
    {
        _botGrid = GetNode<GridContainer>("MarginContainer/VBoxContainer/GridContainer");
        _playerGrid = GetNode<GridContainer>("MarginContainer/VBoxContainer/GridContainer2");

        PopulateGrid(_botGrid, PieceOwner.BOT, _botButtons);
        PopulateGrid(_playerGrid, PieceOwner.PLAYER, _playerButtons);
    }

    // Rellena un grid con un PieceButton por cada tipo de pieza, desactivados e inertes
    private void PopulateGrid(GridContainer grid, PieceOwner owner, Dictionary<PieceType, PieceButton> map)
    {
        foreach (var kv in PiecesData.Data)
        {
            PieceButton btn = PIECE_BUTTON_SCENE.Instantiate<PieceButton>();
            grid.AddChild(btn);
            btn.Setup(kv.Value, owner);
            btn.SetCount(kv.Value.MaxCount);

            // Solo visual: desactivar toda interacción
            btn.Disabled = true;
            btn.MouseFilter = Control.MouseFilterEnum.Ignore;
            btn.FocusMode = Control.FocusModeEnum.None;

            map[kv.Key] = btn;
        }
    }

    // Actualiza el contador de piezas restantes de un bando
    public void UpdateCounts(PieceOwner owner, Dictionary<PieceType, int> remaining)
    {
        var map = owner == PieceOwner.BOT ? _botButtons : _playerButtons;

        foreach (var kv in remaining)
        {
            if (map.TryGetValue(kv.Key, out var btn))
                btn.SetCount(kv.Value);
        }
    }

    public void ShowUI() => Visible = true;
    public void HideUI() => Visible = false;
}