using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossSensorJam : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.SensorJam;
        public override void Tick(BossCombatController c,float t)
        { var p=c.transform.position+Vector3.up*1.25f;c.Query(p,p,c.Definition.range); }
    }
}
