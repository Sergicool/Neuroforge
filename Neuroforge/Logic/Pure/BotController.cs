using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Accion que realiza el bot
public struct MovementAction
{
    public Vector2I From;
    public Vector2I To;
}

internal readonly struct HypotheticalPiece : ICombatant
{
    public PieceType Type { get; }
    public int Rank { get; }

    public HypotheticalPiece(PieceType type, int rank)
    {
        Type = type;
        Rank = rank;
    }
}

// Logica de decisión del bot
public class BotController
{
    private readonly Board _board;
    private readonly Random _rng = new();

    private const float ADVANCE_WEIGHT = 0.15f;
    private const float NOISE_WEIGHT = 0.05f;
    private const float ENERGY_CORE_VALUE = 500f;

    public BotController(Board board)
    {
        _board = board;
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

        MovementAction action = ChooseAction(actions);

        await _board.ExecuteBotAction(action);
        game.EndTurn();
    }



    private MovementAction ChooseAction(List<MovementAction> actions)
    {
        MovementAction bestAction = actions[0];
        float bestScore = float.NegativeInfinity;

        foreach (MovementAction action in actions)
        {
            float score = ScoreAction(action);
            if (score > bestScore)
            {
                bestScore = score;
                bestAction = action;
            }
        }

        return bestAction;
    }

    private float ScoreAction(MovementAction action)
    {
        Tile fromTile = _board.GetTileAt(action.From);
        Tile toTile = _board.GetTileAt(action.To);

        if (fromTile == null || toTile == null || !fromTile.IsOccupied)
            return float.NegativeInfinity;

        Piece piece = fromTile.Occupant;

        float score;

        if (toTile.IsOccupied)
        {
            // Es un ataque
            Piece defender = toTile.Occupant;
            score = defender.IsRevealedToBot
                ? ScoreKnownAttack(piece, defender)
                : ScoreUnknownAttack(piece);
        }
        else
        {
            // Es un movimiento simple
            score = ScoreMove(action);
        }

        // Un poco de ruido para desempatar y no ser totalmente predecible
        score += (float)_rng.NextDouble() * NOISE_WEIGHT;

        return score;
    }

    private float ScoreKnownAttack(Piece attacker, Piece defender)
    {
        CombatResult result = CombatSystem.Resolve(attacker, defender);
        return EvaluateResult(result, attacker.Type, defender.Type);
    }

    private float ScoreUnknownAttack(Piece attacker)
    {
        Dictionary<PieceType, int> hiddenCounts = GetHiddenEnemyCounts();

        int total = 0;
        foreach (int count in hiddenCounts.Values) total += count;

        if (total == 0)
            return 0f;

        float expectedValue = 0f;

        foreach (var kv in hiddenCounts)
        {
            if (kv.Value <= 0) continue;

            PieceType type = kv.Key;
            float probability = kv.Value / (float)total;

            int rank = PiecesData.Data.TryGetValue(type, out var def) ? def.Rank : 0;
            var hypotheticalDefender = new HypotheticalPiece(type, rank);

            CombatResult result = CombatSystem.Resolve(attacker, hypotheticalDefender);
            float outcomeValue = EvaluateResult(result, attacker.Type, type);

            expectedValue += probability * outcomeValue;
        }

        return expectedValue;
    }

    private float EvaluateResult(CombatResult result, PieceType attackerType, PieceType defenderType)
    {
        float attackerValue = PieceValue(attackerType);
        float defenderValue = PieceValue(defenderType);

        return result switch
        {
            CombatResult.DEFENDER_DIES => defenderValue,
            CombatResult.ATTACKER_DIES => -attackerValue,
            CombatResult.BOTH_DIE => defenderValue - attackerValue,
            _ => 0f,
        };
    }

    private float ScoreMove(MovementAction action)
    {
        int advance = action.To.Y - action.From.Y;
        return advance * ADVANCE_WEIGHT;
    }

    private static float PieceValue(PieceType type)
    {
        if (type == PieceType.ENERGY_CORE)
            return ENERGY_CORE_VALUE;

        return PiecesData.Data.TryGetValue(type, out var def) ? def.Rank : 1f;
    }

    private Dictionary<PieceType, int> GetHiddenEnemyCounts()
    {
        var counts = new Dictionary<PieceType, int>();
        foreach (var kv in PiecesData.Data)
            counts[kv.Key] = 0;

        foreach (Tile tile in _board.AllTiles)
        {
            if (!tile.IsOccupied) continue;
            Piece p = tile.Occupant;
            if (p.PlayerOwner != PieceOwner.PLAYER) continue;
            if (p.IsRevealedToBot) continue;

            counts[p.Type]++;
        }

        return counts;
    }
}