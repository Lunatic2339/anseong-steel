using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossAlternatingCombo : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.AlternatingCombo;
        int strike;
        public override void Begin()=>strike=-1;
        public override void Tick(BossCombatController c,float t)
        {
            var times=c.Definition.strikeTimes;
            while(strike+1<times.Length && t>=times[strike+1])
            {
                strike++;c.BeginStrike(strike);
                var p=c.HandPoint(strike%2==0?BossArm.Left:BossArm.Right);
                c.Query(p,p,c.Definition.hitRadius);if(!c.Definition)return;
            }
        }
    }
}
