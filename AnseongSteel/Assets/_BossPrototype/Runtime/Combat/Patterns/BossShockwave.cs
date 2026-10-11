using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossShockwave : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.Shockwave;
        public override void Tick(BossCombatController c,float t)=>c.HitExpandingRing(Mathf.Lerp(.1f,c.Definition.range,t));
    }
}
