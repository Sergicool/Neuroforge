using Godot;

public static class CombatSystem
{
    public static CombatResult Resolve(Piece attacker, Piece defender)
    {
        attacker.Reveal();
        defender.Reveal();

        if (defender.Type == PieceType.TURRET)
        {
            return attacker.Type == PieceType.SABOTEUR
                ? CombatResult.DEFENDER_DIES
                : CombatResult.ATTACKER_DIES;
        }

        if (attacker.Type == PieceType.PHANTOM && defender.Type == PieceType.ENERGY_CORE)
            return CombatResult.DEFENDER_DIES;

        if (attacker.Rank > defender.Rank) return CombatResult.DEFENDER_DIES;
        if (attacker.Rank < defender.Rank) return CombatResult.ATTACKER_DIES;

        return CombatResult.BOTH_DIE;
    }
}