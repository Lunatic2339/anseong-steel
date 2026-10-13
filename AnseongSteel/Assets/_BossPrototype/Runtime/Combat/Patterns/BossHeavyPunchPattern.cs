namespace AnseongSteel.Bosses
{
    public sealed class BossHeavyPunchPattern : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.HeavyPunch;
        // The original BossHeavyPunch and its validated animation events own this hit test.
        public override void Tick(BossCombatController context,float progress) { }
    }
}
