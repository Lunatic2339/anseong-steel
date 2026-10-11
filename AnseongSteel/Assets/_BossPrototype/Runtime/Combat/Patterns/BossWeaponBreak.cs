using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossWeaponBreak : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.WeaponBreak;
        public override void Tick(BossCombatController c,float t)
        {if(c.HasAttackClip&&c.sword&&c.sword.IsDrawn){c.Sweep(c.sword.BladeBase,c.sword.BladeTip);return;}var a=c.transform.TransformPoint(new Vector3(.15f,1.8f,1.05f));c.Sweep(a,a+c.transform.forward*.2f);}
    }
}

