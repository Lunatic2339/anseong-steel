using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossMissileBarrage : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.MissileBarrage;
        int launched;
        public override void Begin()=>launched=0;
        public override void Tick(BossCombatController c,float t)
        {
            int desired=Mathf.Min(c.Definition.projectileCount,1+Mathf.FloorToInt(t*c.Definition.projectileCount));
            while(launched<desired)c.SpawnMissile(launched++);
        }
    }
}
