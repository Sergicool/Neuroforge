using NUnit.Framework;

public record TestPiece(PieceType Type = default, int Rank = 0) : ICombatant;

[TestFixture]
public class CombatSystemTests
{
    [Test]
    public void HigherRank_Wins_AgainstLowerRank()
    {
        var result = CombatSystem.Resolve(new TestPiece(Rank: 5), new TestPiece(Rank: 3));
        Assert.That(result, Is.EqualTo(CombatResult.DEFENDER_DIES));
    }
    [Test]
    public void LowerRank_Loses_AgainstHigherRank()
    {
        var result = CombatSystem.Resolve(new TestPiece(Rank: 2), new TestPiece(Rank: 7));
        Assert.That(result, Is.EqualTo(CombatResult.ATTACKER_DIES));
    }
    [Test]
    public void SameRank_Results_InDraw()
    {
        var result = CombatSystem.Resolve(new TestPiece(Rank: 4), new TestPiece(Rank: 4));
        Assert.That(result, Is.EqualTo(CombatResult.BOTH_DIE));
    }
    [Test]
    public void Saboteur_Always_Beats_Turret()
    {
        var result = CombatSystem.Resolve(
            new TestPiece(Type: PieceType.SABOTEUR),
            new TestPiece(Type: PieceType.TURRET));
        Assert.That(result, Is.EqualTo(CombatResult.DEFENDER_DIES));
    }
    [Test]
    public void NonSaboteur_Loses_AgainstTurret()
    {
        var result = CombatSystem.Resolve(
            new TestPiece(Rank: 9),
            new TestPiece(Type: PieceType.TURRET));
        Assert.That(result, Is.EqualTo(CombatResult.ATTACKER_DIES));
    }
    [Test]
    public void Phantom_Always_Beats_WarMachine()
    {
        var result = CombatSystem.Resolve(
            new TestPiece(Type: PieceType.PHANTOM),
            new TestPiece(Type: PieceType.WAR_MACHINE));
        Assert.That(result, Is.EqualTo(CombatResult.DEFENDER_DIES));
    }
}