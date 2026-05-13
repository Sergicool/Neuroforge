using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// <br/> Gestor de escenas estático (no requiere autoload ni configuración en el editor)
/// <br/> Se auto-inicializa la primera vez que se usa.
/// </summary>
public partial class AudioManager
{
    public enum Bus { Master, Music, Sfx, UI }

    // Configuración ajustable
    public static float DefaultFadeDuration = 1.0f;

    private static _AudioBridge _bridge;
    private static readonly Dictionary<string, AudioStream> _cache = new();

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reproduce música de fondo. Si ya hay música sonando, hace un crossfade.
    /// </summary>
    public static async void PlayMusic(string path, float fadeDuration = -1f)
    {
        await EnsureBridgeAsync();
        float duration = fadeDuration < 0 ? DefaultFadeDuration : fadeDuration;
        AudioStream stream = await LoadAudio(path);

        if (stream != null)
            _bridge.PlayMusic(stream, duration);
    }

    /// <summary>
    /// Reproduce un efecto de sonido puntual.
    /// </summary>
    public static async void PlaySfx(string path, float pitchVar = 0.0f)
    {
        await EnsureBridgeAsync();
        AudioStream stream = await LoadAudio(path);
        if (stream != null)
            _bridge.PlayOneShot(stream, Bus.Sfx, pitchVar);
    }

    /// <summary>
    /// Reproduce un sonido de interfaz.
    /// </summary>
    public static async void PlayUI(string path)
    {
        await EnsureBridgeAsync();
        AudioStream stream = await LoadAudio(path);
        if (stream != null)
            _bridge.PlayOneShot(stream, Bus.UI);
    }

    /// <summary>
    /// Cambia el volumen de un bus (0.0 a 1.0).
    /// </summary>
    public static void SetVolume(Bus bus, float volumeNormalized)
    {
        int index = AudioServer.GetBusIndex(bus.ToString());
        float db = Mathf.LinearToDb(Mathf.Clamp(volumeNormalized, 0.0001f, 1.0f));
        AudioServer.SetBusVolumeDb(index, db);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PRIVADO
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task EnsureBridgeAsync()
    {
        if (_bridge != null && GodotObject.IsInstanceValid(_bridge)) return;

        _bridge = new _AudioBridge();
        var tree = Engine.GetMainLoop() as SceneTree;
        tree.Root.CallDeferred(Node.MethodName.AddChild, _bridge);

        await _bridge.WaitUntilReady();
    }

    private static async Task<AudioStream> LoadAudio(string path)
    {
        if (_cache.TryGetValue(path, out AudioStream cached)) return cached;

        if (!ResourceLoader.Exists(path))
        {
            GD.PushError($"[AudioManager] El archivo no existe: {path}");
            return null;
        }

        var stream = GD.Load<AudioStream>(path);
        _cache[path] = stream;
        return stream;
    }

    // ── Bridge interno ────────────────────────────────────────────────────────
    private sealed partial class _AudioBridge : Node
    {
        private AudioStreamPlayer _musicA;
        private AudioStreamPlayer _musicB;
        private bool _isUsingA = true;

        private readonly TaskCompletionSource _readyTcs = new();
        public Task WaitUntilReady() => _readyTcs.Task;

        public override void _Ready()
        {
            // Configurar reproductores de música para crossfade
            _musicA = CreatePlayer(Bus.Music);
            _musicB = CreatePlayer(Bus.Music);
            AddChild(_musicA);
            AddChild(_musicB);

            _readyTcs.SetResult();
        }

        private AudioStreamPlayer CreatePlayer(Bus bus)
        {
            return new AudioStreamPlayer
            {
                Bus = bus.ToString(),
                ProcessMode = ProcessModeEnum.Always // Que suene incluso en pausa
            };
        }

        public void PlayMusic(AudioStream stream, float duration)
        {
            AudioStreamPlayer active = _isUsingA ? _musicA : _musicB;
            AudioStreamPlayer next = _isUsingA ? _musicB : _musicA;

            if (active.Stream == stream && active.Playing) return;

            next.Stream = stream;
            next.VolumeDb = -80; // Empezar en silencio
            next.Play();

            Tween tween = CreateTween().SetParallel(true);
            tween.TweenProperty(active, "volume_db", -80, duration);
            tween.TweenProperty(next, "volume_db", 0, duration);

            _isUsingA = !_isUsingA;
        }

        public void PlayOneShot(AudioStream stream, Bus bus, float pitchVar = 0f)
        {
            AudioStreamPlayer player = CreatePlayer(bus);
            AddChild(player);

            player.Stream = stream;
            if (pitchVar > 0)
                player.PitchScale = (float)GD.RandRange(1.0f - pitchVar, 1.0f + pitchVar);

            player.Play();

            // Auto-destrucción al terminar para no saturar de nodos
            player.Finished += () => player.QueueFree();
        }
    }
}