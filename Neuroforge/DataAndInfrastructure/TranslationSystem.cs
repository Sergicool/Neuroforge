using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// Sistema de traducción, sin autoload, sin configuración en el editor.
// Se auto-inicializa la primera vez que se usa.

// ── Uso desde codigo ────────────────────────────────────────────────────────
///   string texto = TranslationSystem.Tr("ui.main.menu.play.button");
///   TranslationSystem.SetLocale("es");

// ── Uso desde el editor ─────────────────────────────────────────────
///   1. Añade el nodo al grupo global "translatable"
///   2. Poner como metadata el string tr = (key) Por ejemplo: "ui.main.menu.play.button"
///   Independientemente de lo que se ponga en el texto del nodo en el editor, se traducira

// ── Ejemplo de archivos JSON ───────────────────────────────────────────────────────────
///   res://DataAndInfrastructure/Translations/en.json
///   Formato: { 
///     "ui.main.menu.play.button": "Play",
///     ... 
///   }
///   
///   res://DataAndInfrastructure/Translations/es.json
///   Formato: { 
///     "ui.main.menu.play.button": "Jugar",
///     ... 
///   }
public partial class TranslationSystem
{
    public static event Action OnLocaleChanged;

    // ── Constantes ────────────────────────────────────────────────────────────
    private const string TranslationsPath = "res://DataAndInfrastructure/Translations/";
    private const string MetaKey = "tr";
    private const string GroupName = "translatable";
    private const string FallbackLocale = "en";

    // ── Estado ────────────────────────────────────────────────────────────────
    private static string _currentLocale = "";
    private static Dictionary<string, string> _strings = new();
    private static _Bridge _bridge;

    public static string CurrentLocale => _currentLocale;
    public static void Initialize() => EnsureReady();

    // ─────────────────────────────────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────────────────────────────────

    public static string Tr(string key)
    {
        EnsureReady();
        return _strings.TryGetValue(key, out var value) ? value : $"{key}";
    }

    public static string Tr(string key, params object[] args)
    {
        EnsureReady();
        string template = _strings.TryGetValue(key, out var value) ? value : $"{key}";
        return string.Format(template, args);
    }

    public static string Tr(string key, string fallbackValue)
    {
        EnsureReady();
        return _strings.TryGetValue(key, out var value) ? value : fallbackValue;
    }

    // Aplica la traducción de key al nodo indicado directamente.
    // Soporta Label, Button, RichTextLabel, LineEdit, TextEdit, Window.
    public static void ApplyToNode(Node node, string key)
    {
        string text = Tr(key);
        switch (node)
        {
            case Label l: l.Text = text; break;
            case Button b: b.Text = text; break;
            case RichTextLabel r: r.Text = text; break;
            case LineEdit le: le.PlaceholderText = text; break;
            case TextEdit te: te.PlaceholderText = text; break;
            case Window w: w.Title = text; break;
            default:
                GD.PushWarning($"[TranslationSystem] Tipo no soportado: {node.GetType().Name} (nodo: {node.Name})");
                break;
        }
    }

    // Cambia el locale activo y reaplica todas las traducciones de la escena actual.
    public static void SetLocale(string locale)
    {
        EnsureReady();
        LoadLocale(locale);
        TranslationServer.SetLocale(locale);

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.CurrentScene != null)
            ApplyRecursive(tree.CurrentScene);

        OnLocaleChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────────────────────────────────

    private static void EnsureReady()
    {
        if (_bridge != null && GodotObject.IsInstanceValid(_bridge)) return;

        // OS.GetLocaleLanguage() devuelve directamente "es", "en", "fr"... sin depender de que Godot haya inicializado el TranslationServer.
        string locale = string.IsNullOrEmpty(_currentLocale)
            ? OS.GetLocaleLanguage()
            : _currentLocale;
        LoadLocale(locale);

        _bridge = new _Bridge();
        var tree = Engine.GetMainLoop() as SceneTree;
        tree.Root.CallDeferred(Node.MethodName.AddChild, _bridge);
    }

    private static void LoadLocale(string locale)
    {
        _strings.Clear();

        if (!TryLoadFile(locale))
        {
            GD.PushWarning($"[TranslationSystem] Locale '{locale}' no encontrado, usando fallback '{FallbackLocale}'.");
            TryLoadFile(FallbackLocale);
            _currentLocale = FallbackLocale;
        }
        else
        {
            _currentLocale = locale;
        }
    }

    private static bool TryLoadFile(string locale)
    {
        string path = $"{TranslationsPath}{locale}.json";
        if (!FileAccess.FileExists(path)) return false;

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"[TranslationSystem] No se pudo abrir: {path}");
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText());
            if (parsed == null) return false;
            foreach (var kv in parsed) _strings[kv.Key] = kv.Value;
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[TranslationSystem] Error parseando {path}: {ex.Message}");
            return false;
        }
    }

    private static void ApplyRecursive(Node node)
    {
        if (node.IsInGroup(GroupName) && node.HasMeta(MetaKey))
        {
            string key = node.GetMeta(MetaKey).AsString();
            if (!string.IsNullOrEmpty(key))
                ApplyToNode(node, key);
        }
        foreach (Node child in node.GetChildren())
            ApplyRecursive(child);
    }

    // ── Bridge interno ────────────────────────────────────────────────────────
    // Nodo mínimo y permanente que escucha NodeAdded en el SceneTree.
    // Es private sealed — nadie fuera de TranslationSystem puede instanciarlo.
    private sealed partial class _Bridge : Node
    {
        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            GetTree().NodeAdded += OnNodeAdded;

            if (GetTree().CurrentScene != null)
                ApplyRecursive(GetTree().CurrentScene); // Aplica a los nodos que ya están en escena cuando el bridge arranca
        }

        private void OnNodeAdded(Node node)
        {
            if (node.IsInGroup(GroupName) && node.HasMeta(MetaKey))
            {
                string key = node.GetMeta(MetaKey).AsString();
                if (!string.IsNullOrEmpty(key))
                    ApplyToNode(node, key);
            }
        }
    }
}