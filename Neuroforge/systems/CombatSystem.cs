public static class CombatSystem
{
    // Resuelve el combate entre un atacante y un defensor
    public static CombatResult Resolve(Piece attacker, Piece defender)
    {
        // La torreta siempre gana como defensora, salvo contra el SABOTEUR
        if (defender.Type == PieceType.TURRET)
        {
            return attacker.Type == PieceType.SABOTEUR
                ? CombatResult.DEFENDER_DIES
                : CombatResult.ATTACKER_DIES;
        }

        // El PHANTOM gana siempre contra el CORE
        if (attacker.Type == PieceType.PHANTOM && defender.Type == PieceType.WAR_MACHINE)
            return CombatResult.DEFENDER_DIES;

        // Regla general: gana el de mayor rango
        if (attacker.Rank > defender.Rank) return CombatResult.DEFENDER_DIES;
        if (attacker.Rank < defender.Rank) return CombatResult.ATTACKER_DIES;

        // Empate: ambas piezas son eliminadas
        return CombatResult.BOTH_DIE;
    }
}