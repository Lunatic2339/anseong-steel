using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossGrab : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.Grab;
        public override void Tick(BossCombatController c,float t)
        {var a=c.HasAttackClip?c.HandPoint(BossArm.Right):c.transform.TransformPoint(new Vector3(.15f,1.8f,1.05f));c.Sweep(a,a+c.transform.forward*.2f);}
    }
}

