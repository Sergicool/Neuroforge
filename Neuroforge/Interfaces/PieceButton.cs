using Godot;

// Poner en el editor una escala de forma que:
// X sea multiplo de 16 + 8
// Y sea multiplo de 16 + 30
public partial class PieceButton : Button
{
    private TextureRect _icon;
    private Label       _countLabel;

    public override void _Ready()
    {
        ToggleMode = true;
        ActionMode = ActionModeEnum.Press;

        _icon       = GetNode<TextureRect>("MarginContainer/VBoxContainer/TextureRect");
        _countLabel = GetNode<Label>("MarginContainer/VBoxContainer/Label");
    }

    public void Setup(PieceDefinition def, PieceOwner owner)
    {
        int w = PiecesData.ATLAS_COLUMN_WIDTH;
        int h = PiecesData.ATLAS_HEIGHT / 2;
        int x = def.AtlasColumn * w;
        int y = owner == PieceOwner.PLAYER ? 0 : h;

        _icon.Texture          = PiecesData.Atlas;
        _icon.StretchMode      = TextureRect.StretchModeEnum.KeepAspectCentered;

        var atlas              = new AtlasTexture();
        atlas.Atlas            = PiecesData.Atlas;
        atlas.Region           = new Rect2(x, y, w, h);
        _icon.Texture          = atlas;

        SetCount(def.MaxCount);
    }

    public void SetCount(int count)
    {
        _countLabel.Text = $"{count:00}";
        Disabled         = count <= 0;
        if (count <= 0) SetPressedNoSignal(false);
    }
}