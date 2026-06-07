using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

// Accion que realiza el bot
public struct MovementAction
{
    public Vector2I From;
    public Vector2I To;
}

// Lógica de decisión del bot
public class BotController
{
    private readonly Board _board;
    private readonly Random _rng = new();
    private Process _pythonProcess;
    private bool _modelReady = false;

    private Task _initTask;

    public BotController(Board board)
    {
        _board = board;
        _initTask = Task.Run(StartPythonProcess);
    }

    public async Task WaitUntilReady()
    {
        if (_initTask != null) await _initTask;
    }

    private void StartPythonProcess()
    {
        try
        {
            // Ruta relativa al directorio de trabajo tanto en debug como en exe
            string baseDir = System.IO.Path.GetDirectoryName(
                OS.GetExecutablePath()
            );

            // En debug el ejecutable es el editor de Godot,
            // así que la ruta apunta a la raíz del proyecto
            string scriptPath = System.IO.Path.Combine(baseDir, "Bot", "bot_inference.py");
            string modelPath = System.IO.Path.Combine(baseDir, "Bot", "neuroforge_bot_v20260607_160715.zip");

            if (!System.IO.File.Exists(scriptPath))
            {
                GD.PrintErr($"[BotController] Script no encontrado: {scriptPath}");
                GD.Print("[BotController] Fallback aleatorio activo.");
                return;
            }
            if (!System.IO.File.Exists(modelPath))
            {
                GD.PrintErr($"[BotController] Modelo no encontrado: {modelPath}");
                GD.Print("[BotController] Fallback aleatorio activo.");
                return;
            }

            // Detectar si estamos en Windows o Linux/Mac
            string python = OperatingSystem.IsWindows() ? "python" : "python3";

            _pythonProcess = new Process();
            _pythonProcess.StartInfo = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{scriptPath}\" \"{modelPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            _pythonProcess.Start();

            string response = _pythonProcess.StandardOutput.ReadLine();
            _modelReady = response == "READY";

            if (_modelReady)
                GD.Print("[BotController] Modelo cargado correctamente.");
            else
                GD.PrintErr($"[BotController] Error al cargar modelo: {response}");

        }
        catch (Exception e)
        {
            GD.PrintErr($"[BotController] No se pudo iniciar Python: {e.Message}");
            GD.Print("[BotController] Usando modo aleatorio como fallback.");
            _modelReady = false;
        }
    }

    public async void PlayTurn(GameScene game)
    {
        var actions = _board.GetAllPossibleActions(PieceOwner.BOT);

        if (actions.Count == 0)
        {
            game.EndTurn();
            return;
        }

        game.SetState(GameState.EXECUTING_ACTION);

        MovementAction action;

        if (_modelReady)
        {
            action = await GetModelAction(actions);
        }
        else
        {
            action = actions[_rng.Next(actions.Count)];
        }

        await _board.ExecuteBotAction(action);
        game.EndTurn();
    }

    private async Task<MovementAction> GetModelAction(List<MovementAction> fallbackActions)
    {
        try
        {
            float[] state = _board.GetFlatState();

            // Construir JSON manualmente para evitar problemas con tipos anónimos
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"state\":[");
            for (int i = 0; i < state.Length; i++)
            {
                sb.Append(state[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (i < state.Length - 1) sb.Append(',');
            }
            sb.Append("],\"valid_moves\":[");
            for (int i = 0; i < fallbackActions.Count; i++)
            {
                var a = fallbackActions[i];
                sb.Append($"{{\"from\":{{\"x\":{a.From.X},\"y\":{a.From.Y}}},\"to\":{{\"x\":{a.To.X},\"y\":{a.To.Y}}}}}");
                if (i < fallbackActions.Count - 1) sb.Append(',');
            }
            sb.Append("]}");

            string json = sb.ToString();

            await _pythonProcess.StandardInput.WriteLineAsync(json);
            await _pythonProcess.StandardInput.FlushAsync();

            string response = await Task.Run(() =>
                _pythonProcess.StandardOutput.ReadLine());

            if (string.IsNullOrEmpty(response))
            {
                GD.PrintErr("[BotController] Respuesta vacía, usando fallback.");
                return fallbackActions[_rng.Next(fallbackActions.Count)];
            }

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                GD.PrintErr($"[BotController] Error del modelo: {err.GetString()}");
                return fallbackActions[_rng.Next(fallbackActions.Count)];
            }

            var from = root.GetProperty("from");
            var to = root.GetProperty("to");

            return new MovementAction
            {
                From = new Vector2I(
                    from.GetProperty("x").GetInt32(),
                    from.GetProperty("y").GetInt32()),
                To = new Vector2I(
                    to.GetProperty("x").GetInt32(),
                    to.GetProperty("y").GetInt32())
            };
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BotController] Excepción en inferencia: {e.Message}");
            GD.PrintErr($"[BotController] StackTrace: {e.StackTrace}");
            return fallbackActions[_rng.Next(fallbackActions.Count)];
        }
    }

    public void Dispose()
    {
        try
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _pythonProcess.Kill();
                _pythonProcess.Dispose();
                GD.Print("[BotController] Proceso Python cerrado.");
            }
        }
        catch { }
    }
}