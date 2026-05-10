using Godot;
using static Godot.Control;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// <br/> Gestor de escenas estático (no requiere autoload ni configuración en el editor)
/// <br/> Se auto-inicializa la primera vez que se usa.
/// <br/>
/// <br/> Uso básico:
/// <code> await SceneManager.GoTo("res://scenes/Game.tscn", SceneManager.Transition.Fade, 0.4f); </code>
/// <code> await SceneManager.GoBack(); </code>
/// <code> await SceneManager.Preload("res://scenes/Game.tscn"); </code>
/// </summary>
public partial class SceneManager
{
    // ── Eventos ───────────────────────────────────────────────────────────────
    // Suscribirse con += y desuscribirse en _ExitTree con -= usando referencias
    // guardadas (no lambdas inline) para evitar ObjectDisposedException.

    /// <summary> Se dispara al iniciar la carga de una escena, antes de la transición.</summary>
    public static event Action<string> OnSceneLoadStarted;

    /// <summary> Progreso de carga entre 0.0 y 1.0. Solo útil si la escena no estaba en caché.</summary>
    public static event Action<float> OnSceneLoadProgress;

    /// <summary> Se dispara cuando la nueva escena ya está activa y la transición de entrada terminó.</summary>
    public static event Action<string> OnSceneLoadFinished;

    /// <summary> Se dispara justo antes de empezar la animación de salida.</summary>
    public static event Action OnTransitionStarted;

    /// <summary> Se dispara justo después de que termina la animación de entrada.</summary>
    public static event Action OnTransitionFinished;

    // ── Tipos de transición ───────────────────────────────────────────────────
    // La lógica de cada tipo vive en _Bridge.TransitionIn / TransitionOut.
    // Para añadir un tipo nuevo: añadir el valor aquí y el case en los metodos de _Bridge.
    public enum Transition { None, Fade, SlideLeft, SlideRight }

    // ── Configuración pública ─────────────────────────────────────────────────
    /// <summary> Duración usada cuando no se especifica una en GoTo / GoBack.</summary>
    public static float DefaultDuration = 0.0f;

    /// <summary> Color del overlay de fade. Negro por defecto.</summary>
    public static Color FadeColor = Colors.Black;

    // ── Estado ────────────────────────────────────────────────────────────────
    /// <summary> True si hay al menos una escena anterior en el historial.</summary>
    public static bool CanGoBack => _history.Count > 1;

    /// <summary> Ruta de la escena actualmente activa.</summary>
    public static string CurrentPath => _history.Count > 0 ? _history.Peek() : "";

    // Historial de rutas navegadas. GoBack hace pop y navega al tope.
    private static readonly Stack<string> _history = new();

    // Caché de PackedScene ya cargadas. GoTo y Preload las reutilizan.
    private static readonly Dictionary<string, PackedScene> _cache = new();

    // Mutex simple: evita que dos GoTo corran en paralelo.
    private static bool _isTransitioning = false;

    // El único Node que SceneManager necesita para vivir en el SceneTree.
    // Se crea automáticamente la primera vez que se llama a EnsureBridgeAsync.
    private static _Bridge _bridge;

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Navega a una nueva escena con transición opcional.
    /// Si la escena estaba precargada, el swap es instantáneo (solo espera la animación).
    /// </summary>
    /// <param name="path">Ruta res:// de la escena destino.</param>
    /// <param name="transition">Tipo de animación de transición.</param>
    /// <param name="duration">Duración en segundos. -1 usa DefaultDuration.</param>
    /// <param name="addToHistory">False para navegar sin añadir al historial (útil en GoBack).</param>
    public static async Task GoTo(
        string path,
        Transition transition = Transition.None,
        float duration = -1f,
        bool addToHistory = true)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        duration = duration < 0 ? DefaultDuration : duration;

        await EnsureBridgeAsync();

        OnSceneLoadStarted?.Invoke(path);
        OnTransitionStarted?.Invoke();

        // Animar salida (overlay se vuelve opaco) antes de cargar
        await _bridge.TransitionIn(transition, duration);

        PackedScene packed = await LoadScene(path);
        if (packed == null)
        {
            GD.PushError($"[SceneManager] No se pudo cargar: {path}");
            await _bridge.TransitionOut(transition, duration);
            _isTransitioning = false;
            return;
        }

        // Sustituir la escena activa mientras el overlay tapa la pantalla
        SwapScene(_bridge.GetTree(), packed, path, addToHistory);

        // Animar entrada (overlay desaparece) con la nueva escena ya visible
        await _bridge.TransitionOut(transition, duration);
        _isTransitioning = false;

        OnSceneLoadFinished?.Invoke(path);
        OnTransitionFinished?.Invoke();
    }

    /// <summary>
    /// Vuelve a la escena anterior del historial.
    /// No hace nada si no hay escena anterior (CanGoBack == false).
    /// </summary>
    public static async Task GoBack(Transition transition = Transition.None, float duration = -1f)
    {
        if (!CanGoBack) return;
        _history.Pop();
        await GoTo(_history.Peek(), transition, duration, addToHistory: false);
    }

    /// <summary>
    /// Precarga una escena en background sin navegar a ella.
    /// La siguiente llamada a GoTo con la misma ruta será instantánea.
    /// Llámalo desde _Ready mientras el jugador está en el menú o pantalla de carga.
    /// </summary>
    public static async Task Preload(string path)
    {
        if (_cache.ContainsKey(path)) return;
        await EnsureBridgeAsync();
        PackedScene packed = await LoadScene(path);
        if (packed != null) _cache[path] = packed;
    }

    /// <summary>
    /// Precarga varias escenas en secuencia. onProgress recibe 0.0→1.0 por escena completada.
    /// Útil para mostrar una barra de progreso en una pantalla de carga.
    /// </summary>
    public static async Task PreloadBatch(string[] paths, Action<float> onProgress = null)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            await Preload(paths[i]);
            onProgress?.Invoke((float)(i + 1) / paths.Length);
        }
    }

    /// <summary>Elimina una escena de la caché para liberar memoria.</summary>
    public static void EvictCache(string path) => _cache.Remove(path);

    /// <summary>
    /// Limpia el historial de navegación manteniendo solo la escena actual.
    /// Útil al entrar al juego desde el menú para que GoBack no vuelva al menú.
    /// </summary>
    public static void ClearHistory()
    {
        string current = CurrentPath;
        _history.Clear();
        if (!string.IsNullOrEmpty(current)) _history.Push(current);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PRIVADO
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea el bridge y lo añade al árbol si aún no existe.
    /// Espera a que _Ready() haya corrido antes de devolver el control,
    /// garantizando que GetTree() y el overlay son válidos cuando LoadScene los usa.
    /// </summary>
    private static async Task EnsureBridgeAsync()
    {
        if (_bridge != null && GodotObject.IsInstanceValid(_bridge)) return;

        _bridge = new _Bridge();
        var tree = Engine.GetMainLoop() as SceneTree;

        // CallDeferred sobre Root garantiza que AddChild ocurre al inicio
        // del siguiente frame, cuando el árbol está en estado seguro.
        tree.Root.CallDeferred(Node.MethodName.AddChild, _bridge);

        // Bloquear hasta que _Bridge._Ready() haya completado.
        // Sin esto, NextFrame() crashea porque GetTree() devuelve null.
        await _bridge.WaitUntilReady();

        // Registrar la escena inicial en el historial (solo la primera vez)
        if (tree?.CurrentScene?.SceneFilePath is { Length: > 0 } p)
            _history.Push(p);
    }

    /// <summary>
    /// Carga una escena desde disco usando el loader threaded de Godot,
    /// o la devuelve inmediatamente si ya estaba en caché.
    /// Reporta progreso via OnSceneLoadProgress mientras carga.
    /// </summary>
    private static async Task<PackedScene> LoadScene(string path)
    {
        if (_cache.TryGetValue(path, out PackedScene cached)) return cached;

        ResourceLoader.LoadThreadedRequest(path);
        while (true)
        {
            var arr = new Godot.Collections.Array();
            var status = ResourceLoader.LoadThreadedGetStatus(path, arr);

            if (arr.Count > 0)
                OnSceneLoadProgress?.Invoke(arr[0].As<float>());

            if (status == ResourceLoader.ThreadLoadStatus.Loaded)
            {
                PackedScene packed = ResourceLoader.LoadThreadedGet(path) as PackedScene;
                _cache[path] = packed;
                return packed;
            }

            if (status == ResourceLoader.ThreadLoadStatus.Failed) return null;

            // Ceder el control al motor un frame para no bloquear el hilo principal
            await _bridge.NextFrame();
        }
    }

    /// <summary>
    /// Sustituye la escena activa por la nueva.
    /// QueueFree en lugar de Free para respetar el ciclo de vida de Godot.
    /// </summary>
    private static void SwapScene(SceneTree tree, PackedScene packed, string path, bool addToHistory)
    {
        tree.CurrentScene?.QueueFree();
        Node next = packed.Instantiate();
        tree.Root.AddChild(next);
        tree.CurrentScene = next;
        if (addToHistory) _history.Push(path);
    }

    // ── Bridge interno ────────────────────────────────────────────────────────
    // Node mínimo que vive en el SceneTree permanentemente (Layer 100).
    // Responsabilidades: NextFrame, overlay de transición.
    // Es private sealed — nadie fuera de SceneManager puede instanciarlo.
    private sealed partial class _Bridge : CanvasLayer
    {
        private ColorRect _overlay;

        // TaskCompletionSource permite que EnsureBridgeAsync espere
        // a que _Ready() haya corrido antes de continuar.
        private readonly TaskCompletionSource _readyTcs = new();
        public Task WaitUntilReady() => _readyTcs.Task;

        public override void _Ready()
        {
            Layer = 100;

            _overlay = new ColorRect
            {
                Color = FadeColor,
                Modulate = new Color(1, 1, 1, 0),   // invisible al inicio
                MouseFilter = MouseFilterEnum.Ignore  // no bloquear input cuando no hay transición
            };
            _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(_overlay);

            _readyTcs.SetResult(); // desbloquear WaitUntilReady()
        }

        /// <summary>Cede el control al motor durante un frame sin bloquear el hilo.</summary>
        public async Task NextFrame() =>
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        /// <summary>
        /// Animación de salida: el overlay se vuelve opaco tapando la escena actual.
        /// Se llama ANTES del swap de escena.
        /// TODO: añadir cases para SlideLeft, SlideRight, etc.
        /// </summary>
        public async Task TransitionIn(Transition t, float duration)
        {
            if (t == Transition.None) return;

            _overlay.MouseFilter = MouseFilterEnum.Stop; // bloquear input durante transición
            _overlay.Color = FadeColor;

            Tween tween = CreateTween();
            tween.TweenProperty(_overlay, "modulate:a", 1.0f, duration);
            await ToSignal(tween, Tween.SignalName.Finished);
        }

        /// <summary>
        /// Animación de entrada: el overlay desaparece revelando la nueva escena.
        /// Se llama DESPUÉS del swap de escena.
        /// TODO: añadir cases para SlideLeft, SlideRight, etc.
        /// </summary>
        public async Task TransitionOut(Transition t, float duration)
        {
            if (t == Transition.None) return;

            Tween tween = CreateTween();
            tween.TweenProperty(_overlay, "modulate:a", 0.0f, duration);
            await ToSignal(tween, Tween.SignalName.Finished);

            _overlay.MouseFilter = MouseFilterEnum.Ignore; // devolver input al juego
        }
    }
}