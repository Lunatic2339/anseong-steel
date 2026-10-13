using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossSwordSlash : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.SwordSlash;
        public override void Tick(BossCombatController c,float t)
        {
            if(c.HasAttackClip&&c.sword&&c.sword.IsDrawn){c.Sweep(c.sword.BladeBase,c.sword.BladeTip);return;}
            var direction=Quaternion.AngleAxis(Mathf.Lerp(-65,65,t),Vector3.up)*c.transform.forward;
            var a=c.transform.position+Vector3.up*1.65f+direction*.55f;c.Sweep(a,a+direction*1.65f);
        }
    }
}
