using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossMissile : MonoBehaviour
    {
        public BossCombatController Owner {get;private set;}
        public bool Resolved {get;private set;}
        Vector3 destination,velocity;
        float speed,age;
        int generation;
        BossHit hit;
        LayerMask mask;
        public void Initialize(BossCombatController owner,Vector3 target,float travelSpeed,BossHit attack,int epoch,LayerMask layers)
        {Owner=owner;destination=target;speed=travelSpeed;hit=attack;generation=epoch;mask=layers;velocity=(target-transform.position).normalized*speed;}
        void Update()
        {
            if(Resolved)return;
            if(!Owner||!Owner.isActiveAndEnabled||Owner.Generation!=generation||age>6f){Resolve();return;}
            age+=Time.deltaTime;
            var step=velocity*Time.deltaTime;float distance=step.magnitude;
            var hits=Physics.SphereCastAll(transform.position,.075f,velocity.normalized,distance,mask,QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var contact in hits)
            {
                if(contact.collider.transform.IsChildOf(Owner.transform))continue;
                var receiver=contact.collider.GetComponentInParent<BossCombatTarget>();
                if(receiver) {hit.point=contact.point;Owner.ResolveProjectile(receiver,hit);}
                Resolve();return;
            }
            transform.position+=step;
            if((transform.position-destination).sqrMagnitude<.01f)Resolve();
        }
        public bool Intercept() {if(Resolved)return false;Owner?.ReportInterception();Resolve();return true;}
        public void Cancel()=>Resolve();
        void Resolve() {if(Resolved)return;Resolved=true;gameObject.SetActive(false);Destroy(gameObject);}
    }
}
