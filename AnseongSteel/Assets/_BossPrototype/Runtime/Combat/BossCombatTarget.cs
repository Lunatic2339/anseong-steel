using System;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    // Prototype receiver. The player team can bridge these methods/events to its own health and arm systems.
    public sealed class BossCombatTarget : MonoBehaviour
    {
        public float maximumHealth=300f;
        public float parryWindow=.22f;
        public float blockedDamageMultiplier=.1f;
        public float Health {get;private set;}
        public BossArm Guard {get;private set;}
        public BossArm GrabbedArm {get;private set;}
        public BossHitResult LastResult {get;private set;}
        public BossStatus Status {get;private set;}
        public event Action<BossStatus> StatusChanged;
        readonly System.Collections.Generic.Dictionary<BossStatus,float> statusExpiry=new System.Collections.Generic.Dictionary<BossStatus,float>();
        readonly System.Collections.Generic.List<BossStatus> expired=new System.Collections.Generic.List<BossStatus>();
        void Update()
        {
            expired.Clear();foreach(var pair in statusExpiry)if(Time.time>=pair.Value)expired.Add(pair.Key);
            if(expired.Count==0)return;
            foreach(var flag in expired){statusExpiry.Remove(flag);Status&=~flag;}StatusChanged?.Invoke(Status);
        }
        public void ApplyStatus(BossStatus status,float seconds)
        {
            if(seconds<=0)return;
            foreach(BossStatus flag in Enum.GetValues(typeof(BossStatus)))
                if(flag!=BossStatus.None&&(status&flag)!=0){Status|=flag;statusExpiry[flag]=Time.time+seconds;}
            if((Status&BossStatus.LeftArmDisabled)!=0)EndGuard(BossArm.Left);
            if((Status&BossStatus.RightArmDisabled)!=0)EndGuard(BossArm.Right);
            StatusChanged?.Invoke(Status);
        }
        public int HitCount {get;private set;}
        public event Action<BossHit,BossHitResult> HitReceived;
        public event Action<BossArm> GrabChanged;
        float leftGuardAt=float.NegativeInfinity,rightGuardAt=float.NegativeInfinity;
        void Awake()=>ResetTarget();
        public void BeginGuard(BossArm arms)
        {
            arms&=~GrabbedArm;
            if((Status&BossStatus.LeftArmDisabled)!=0)arms&=~BossArm.Left;
            if((Status&BossStatus.RightArmDisabled)!=0)arms&=~BossArm.Right;
            if((arms&BossArm.Left)!=0&&(Guard&BossArm.Left)==0)leftGuardAt=Time.time;
            if((arms&BossArm.Right)!=0&&(Guard&BossArm.Right)==0)rightGuardAt=Time.time;
            Guard|=arms;
        }
        public void EndGuard(BossArm arms)=>Guard&=~arms;
        public bool IsArmAvailable(BossArm arm)=>Health>0&&(GrabbedArm&arm)==0&&((arm&BossArm.Left)==0||(Status&BossStatus.LeftArmDisabled)==0)&&((arm&BossArm.Right)==0||(Status&BossStatus.RightArmDisabled)==0);
        public BossHitResult Receive(BossHit hit)
        {
            if(Health<=0)return BossHitResult.Miss;
            bool guarded=hit.requiredGuard!=BossArm.None&&(Guard&hit.requiredGuard)==hit.requiredGuard;
            bool parry=guarded&&((hit.requiredGuard&BossArm.Left)==0||Time.time-leftGuardAt<=parryWindow)&&
                ((hit.requiredGuard&BossArm.Right)==0||Time.time-rightGuardAt<=parryWindow);
            var result=parry?BossHitResult.Parried:guarded?BossHitResult.Blocked:hit.kind==BossAttackKind.Grab?BossHitResult.Grabbed:BossHitResult.Hit;
            if(result==BossHitResult.Grabbed) {GrabbedArm=hit.grabbedArm;EndGuard(GrabbedArm);GrabChanged?.Invoke(GrabbedArm);}
            else if(!parry)Health=Mathf.Max(0,Health-hit.damage*(guarded?blockedDamageMultiplier:1f));
            if(result==BossHitResult.Hit)ApplyStatus(hit.status,hit.statusSeconds);
            LastResult=result;HitCount++;HitReceived?.Invoke(hit,result);return result;
        }
        public void ReleaseGrab() {if(GrabbedArm==BossArm.None)return;GrabbedArm=BossArm.None;GrabChanged?.Invoke(GrabbedArm);}
        public void ResetTarget() {Health=maximumHealth;Guard=BossArm.None;Status=BossStatus.None;statusExpiry.Clear();StatusChanged?.Invoke(Status);ReleaseGrab();HitCount=0;LastResult=BossHitResult.Miss;leftGuardAt=rightGuardAt=float.NegativeInfinity;}
    }
}

