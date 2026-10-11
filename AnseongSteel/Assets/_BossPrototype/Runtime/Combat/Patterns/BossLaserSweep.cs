using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossLaserSweep : BossAttackPattern
    {
        public override BossAttackKind Kind=>BossAttackKind.LaserSweep;
        public override void Tick(BossCombatController c,float t)
        {
            if(!c.laser)return;
            var start=c.laser.Origin;
            var collider=c.target.GetComponent<Collider>();
            var aim=collider?collider.bounds.center+Vector3.up*.65f:c.target.transform.position+Vector3.up*1.9f;
            var forward=c.transform.forward;
            forward.y=(aim.y-start.y)/Mathf.Max(.1f,Vector3.ProjectOnPlane(aim-start,Vector3.up).magnitude);
            var direction=Quaternion.AngleAxis(Mathf.Lerp(-55,55,t),c.transform.up)*forward.normalized;
            var end=c.laser.Trace(start+direction*c.Definition.range,c.hitLayers);
            c.laser.Fire(end);c.Sweep(start,end);
        }
    }
}
