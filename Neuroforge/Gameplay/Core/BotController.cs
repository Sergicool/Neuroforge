using Godot;
using System;

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

    public BotController(Board board)
    {
        _board = board;
    }

    // Ejecuta el turno del bot: elige una acción aleatoria y la lleva a cabo
    public void PlayTurn(GameManager game)
    {
        var actions = _board.GetAllPossibleActions(PieceOwner.BOT);

        if (actions.Count == 0)
        {
            game.EndTurn();
            return;
        }

        MovementAction action = actions[_rng.Next(actions.Count)];
        _board.ExecuteBotAction(action);

        game.EndTurn();
    }
}