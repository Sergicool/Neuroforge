using Godot;

public static class CombatSystem
{
    // Resuelve el combate entre un atacante y un defensor
    public static CombatResult Resolve(Piece attacker, Piece defender)
    {
        // Ambas piezas se revelan
        attacker.Reveal();
        defender.Reveal();

        // La torreta siempre gana como defensor excepto contra el SABOTEUR
        if (defender.Type == PieceType.TURRET)
        {
            return attacker.Type == PieceType.SABOTEUR
                ? CombatResult.DEFENDER_DIES
                : CombatResult.ATTACKER_DIES;
        }

        // Si es el PHANTOM quien ataca, contra el CORE, gana
        if (attacker.Type == PieceType.PHANTOM && defender.Type == PieceType.CORE)
            return CombatResult.DEFENDER_DIES;

        // Regla general: Gana el que tiene mas rango
        if (attacker.Rank > defender.Rank) return CombatResult.DEFENDER_DIES;
        if (attacker.Rank < defender.Rank) return CombatResult.ATTACKER_DIES;

        // Si queda en empate, ambas piezas pierden
        return CombatResult.BOTH_DIE;
    }
}