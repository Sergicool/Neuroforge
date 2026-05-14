using Godot;
using System.Threading.Tasks;

/// <summary>
/// Overlay de opciones reutilizable: resolución de pantalla y volúmenes de audio.
/// Uso: await _optionsOverlay.ShowOverlay();
/// </summary>
public partial class OptionsOverlay : Control
{
    // ── Nodos ────────────────────────────────────────────────────────────────
    private Panel _background, _popup;
    private Button _closeButton;
    private OptionButton _resolutionDropdown;
    private HSlider _masterSlider, _musicSlider, _sfxSlider, _uiSlider;
    private Label _masterNumber, _musicNumber, _sfxNumber, _uiNumber;

    // ── Resoluciones disponibles ─────────────────────────────────────────────
    private static readonly string[] WindowModes = { "  Window", "  Full Screen" };

    private static readonly Color BG_HIDDEN = new(0, 0, 0, 0);
    private static readonly Color BG_VISIBLE = new(0, 0, 0, 0.65f);

    // ── Ciclo de vida ────────────────────────────────────────────────────────
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // Funciona aunque el juego este pausado

        _background = GetNode<Panel>("Background");
        _popup = GetNode<Panel>("PopUp");
        _closeButton = GetNode<Button>("PopUp/BackButton");

        _resolutionDropdown = GetNode<OptionButton>("PopUp/PanelContainer/MarginContainer/VBoxContainer/ResolutionRow/ResolutionDropdown");
        _masterSlider = GetNode<HSlider>("PopUp/PanelContainer/MarginContainer/VBoxContainer/MasterRow/HBoxContainer/MasterSlider");
        _masterNumber = GetNode<Label>("PopUp/PanelContainer/MarginContainer/VBoxContainer/MasterRow/HBoxContainer/MasterNumber");
        _musicSlider = GetNode<HSlider>("PopUp/PanelContainer/MarginContainer/VBoxContainer/MusicRow/HBoxContainer/MusicSlider");
        _musicNumber = GetNode<Label>("PopUp/PanelContainer/MarginContainer/VBoxContainer/MusicRow/HBoxContainer/MusicNumber");
        _sfxSlider = GetNode<HSlider>("PopUp/PanelContainer/MarginContainer/VBoxContainer/SfxRow/HBoxContainer/SfxSlider");
        _sfxNumber = GetNode<Label>("PopUp/PanelContainer/MarginContainer/VBoxContainer/SfxRow/HBoxContainer/SfxNumber");
        _uiSlider = GetNode<HSlider>("PopUp/PanelContainer/MarginContainer/VBoxContainer/UIRow/HBoxContainer/UISlider");
        _uiNumber = GetNode<Label>("PopUp/PanelContainer/MarginContainer/VBoxContainer/UIRow/HBoxContainer/UINumber");

        Visible = false;

        // Resoluciones
        foreach (var label in WindowModes)
            _resolutionDropdown.AddItem(label);
        SyncResolutionDropdown();

        // Sliders — reemplaza los 4 InitSlider anteriores:
        InitSlider(_masterSlider, _masterNumber, AudioManager.Bus.Master);
        InitSlider(_musicSlider, _musicNumber, AudioManager.Bus.Music);
        InitSlider(_sfxSlider, _sfxNumber, AudioManager.Bus.Sfx);
        InitSlider(_uiSlider, _uiNumber, AudioManager.Bus.UI);

        // Señales
        _closeButton.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _closeButton.Pressed += async () => await HideOverlay();
        _resolutionDropdown.ItemSelected += OnResolutionSelected;
        _resolutionDropdown.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _resolutionDropdown.Pressed += () => AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        // Señales de volumen — reemplaza las 4 ValueChanged anteriores:
        _masterSlider.ValueChanged += v => OnVolumeChanged(AudioManager.Bus.Master, _masterNumber, (float)v);
        _masterSlider.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _masterSlider.DragStarted += () => AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        _musicSlider.ValueChanged += v => OnVolumeChanged(AudioManager.Bus.Music, _musicNumber, (float)v);
        _musicSlider.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _musicSlider.DragStarted += () => AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        _sfxSlider.ValueChanged += v => OnVolumeChanged(AudioManager.Bus.Sfx, _sfxNumber, (float)v);
        _sfxSlider.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _sfxSlider.DragStarted += () => AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        _uiSlider.ValueChanged += v => OnVolumeChanged(AudioManager.Bus.UI, _uiNumber, (float)v);
        _uiSlider.MouseEntered += () => AudioManager.PlayUI("res://assets/sounds/HoverButton.wav");
        _uiSlider.DragStarted += () => AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
    }

    // ── API pública ──────────────────────────────────────────────────────────
    public async Task ShowOverlay()
    {
        Visible = true;
        _popup.Scale = Vector2.Zero;
        _background.Modulate = BG_HIDDEN;
        _closeButton.Disabled = false;

        _popup.PivotOffset = _popup.Size / 2f;

        Tween bg = CreateTween();
        bg.TweenProperty(_background, "modulate", BG_VISIBLE, 0.3f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        Tween pop = CreateTween();
        pop.TweenProperty(_popup, "scale", Vector2.One, 0.35f)
           .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        await ToSignal(pop, Tween.SignalName.Finished);
    }

    public async Task HideOverlay()
    {
        AudioManager.PlayUI("res://assets/sounds/PressButton.wav");
        _closeButton.Disabled = true;
        _popup.PivotOffset = _popup.Size / 2f;

        Tween pop = CreateTween();
        pop.TweenProperty(_popup, "scale", Vector2.Zero, 0.25f)
           .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);

        Tween bg = CreateTween();
        bg.TweenProperty(_background, "modulate", BG_HIDDEN, 0.25f);

        await ToSignal(pop, Tween.SignalName.Finished);
        Visible = false;
    }

    // ── Lógica interna ───────────────────────────────────────────────────────

    // Reemplaza InitSlider por esta versión:
    private void InitSlider(HSlider slider, Label numberLabel, AudioManager.Bus bus)
    {
        slider.MinValue = 0;
        slider.MaxValue = 100;
        slider.Step = 1;

        int idx = AudioServer.GetBusIndex(bus.ToString());
        float db = AudioServer.GetBusVolumeDb(idx);
        float linear = Mathf.DbToLinear(db);
        int percent = Mathf.RoundToInt(Mathf.Clamp(linear, 0f, 1f) * 100f);
        slider.Value = percent;
        numberLabel.Text = percent.ToString();
    }

    private void SyncResolutionDropdown()
    {
        bool isFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen
                         || DisplayServer.WindowGetMode() == DisplayServer.WindowMode.ExclusiveFullscreen;
        _resolutionDropdown.Select(isFullscreen ? 1 : 0);
    }

    private void OnResolutionSelected(long index)
    {
        if (index == 1)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        else
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
    }

    private void OnVolumeChanged(AudioManager.Bus bus, Label numberLabel, float value)
    {
        int percent = Mathf.RoundToInt(value);
        numberLabel.Text = percent.ToString();
        AudioManager.SetVolume(bus, percent / 100f);
    }
}