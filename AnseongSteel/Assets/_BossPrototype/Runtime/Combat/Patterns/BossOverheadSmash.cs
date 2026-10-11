using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossOverheadSmash : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.OverheadSmash;
        public override void Tick(BossCombatController c,float t)
        {
            if(c.HasAttackClip&&c.sword&&c.sword.IsDrawn){c.Sweep(c.sword.BladeBase,c.sword.BladeTip);return;}
            var a=c.transform.TransformPoint(new Vector3(0,Mathf.Lerp(3f,.7f,t),1.3f));c.Sweep(a,a+c.transform.forward*.65f);
        }
    }
}
