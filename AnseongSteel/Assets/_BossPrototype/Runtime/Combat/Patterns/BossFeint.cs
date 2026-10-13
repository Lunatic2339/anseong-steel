using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossFeint : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.Feint;
        public override void Tick(BossCombatController c,float t)
        {
            if(c.HasAttackClip&&c.sword&&c.sword.IsDrawn){c.Sweep(c.sword.BladeBase,c.sword.BladeTip);return;}
            // First portion intentionally has no hit volume; reverse the threatened side afterwards.
            if(t<.45f)return;t=(t-.45f)/.55f;
            var direction=Quaternion.AngleAxis(Mathf.Lerp(65,-65,t),Vector3.up)*c.transform.forward;
            var a=c.transform.position+Vector3.up*1.65f+direction*.55f;c.Sweep(a,a+direction*1.65f);
        }
    }
}
