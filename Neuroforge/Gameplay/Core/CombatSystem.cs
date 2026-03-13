public enum CombatResult
{
    ATTACKER_DIES,
    DEFENDER_DIES,
    BOTH_DIE
}

public static class CombatSystem
{
    public static CombatResult Resolve(Piece attacker, Piece defender)
    {
        attacker.Reveal();
        defender.Reveal();

        if (defender.Type == PieceType.TURRET)
        {
            if (attacker.Type == PieceType.SABOTEUR)
                return CombatResult.DEFENDER_DIES;

            return CombatResult.ATTACKER_DIES;
        }

        if (attacker.Type == PieceType.PHANTOM && defender.Type == PieceType.ENERGY_CORE)
            return CombatResult.DEFENDER_DIES;

        if (attacker.Rank > defender.Rank)
            return CombatResult.DEFENDER_DIES;

        if (attacker.Rank < defender.Rank)
            return CombatResult.ATTACKER_DIES;

        return CombatResult.BOTH_DIE;
    }
}