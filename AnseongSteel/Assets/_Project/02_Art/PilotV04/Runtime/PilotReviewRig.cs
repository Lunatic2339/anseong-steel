using System;
using System.Linq;
using UnityEngine;

namespace AnseongSteel.PilotV04
{
    // A small review rig: explicit targets, fixed feet, no network or animation dependencies.
    [ExecuteAlways, DefaultExecutionOrder(100)]
    public sealed class PilotReviewRig : MonoBehaviour
    {
        [Serializable] public class RestBone
        {
            public Transform bone;
            public Vector3 position;
            public Quaternion rotation;
        }
        [Serializable] public class Arm
        {
            public Transform upper, lower, hand, upperTwist, forearmTwist, armor;
            public Transform target, pole;
            public float upperLength, lowerLength;
            public Quaternion upperRest, lowerRest, handRest, upperTwistRest, forearmTwistRest, armorRest;
            public Vector3 lowerAxis, handAxis;
            [HideInInspector] public float targetError, clampedDistance;
            [HideInInspector] public Quaternion neutralFrame;
            [HideInInspector] public float wristFlex, wristDeviation, appliedRoll, orientationError;
            [NonSerialized] public bool rollInitialized;
            [NonSerialized] public float lastRawRoll, continuousRoll;
        }
        [Serializable] public class Finger
        {
            public Transform bone;
            public Quaternion rest;
            public Vector3 curlAxis;
            public float maxAngle;
            public bool left, thumb;
        }
        public Arm left = new Arm(), right = new Arm();
        public Transform headTarget;
        [Range(0,1)] public float leftGrip, rightGrip;
        [Range(0,1)] public float headInfluence = 1;
        [Range(0,1)] public float shoulderFollow = 1;
        [Header("Review limits (art tuning, degrees)")]
        [Range(5,70)] public float maxWristFlex=35;
        [Range(5,40)] public float maxWristDeviation=20;
        [Range(45,160)] public float maxForearmRoll=100;
        [SerializeField] RestBone[] restBones;
        [SerializeField] Finger[] fingers;
        [SerializeField] Transform head;
        [SerializeField] Quaternion headTargetOffset;
        [SerializeField] Quaternion leftTargetOffset, rightTargetOffset;
        public bool IsReady => restBones != null && restBones.Length > 0;

        public void Configure(Animator animator, Transform lh, Transform rh, Transform lp, Transform rp, Transform ht)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            headTarget = ht;
            restBones = animator.GetComponentsInChildren<Transform>(true)
                .Where(t => t != animator.transform)
                .Select(t => new RestBone {bone=t, position=t.localPosition, rotation=t.localRotation}).ToArray();
            SetupArm(left, animator, true, lh, lp);
            SetupArm(right, animator, false, rh, rp);
            // Targets use character-space axes; arbitrary imported bone rolls remain in these offsets.
            leftTargetOffset = Quaternion.Inverse(Quaternion.LookRotation(left.hand.position-left.lower.position,transform.up))*left.hand.rotation;
            rightTargetOffset = Quaternion.Inverse(Quaternion.LookRotation(right.hand.position-right.lower.position,transform.up))*right.hand.rotation;
            headTargetOffset = Quaternion.Inverse(transform.rotation) * head.rotation;
            fingers = restBones.Where(b => new[]{"Thumb","Index","Middle","Ring","Little"}.Any(b.bone.name.Contains))
                .Select(b => {
                    bool isLeft = b.bone.name.StartsWith("Left"), thumb = b.bone.name.Contains("Thumb");
                    Vector3 dir = b.bone.childCount > 0 ? b.bone.GetChild(0).position-b.bone.position : b.bone.position-b.bone.parent.position;
                    Vector3 bend = -transform.up;
                    if (thumb) bend = (-transform.forward*.55f-transform.up).normalized;
                    Vector3 axis = Vector3.Cross(dir.normalized, bend).normalized;
                    return new Finger { bone=b.bone,rest=b.rotation,curlAxis=b.bone.InverseTransformDirection(axis),left=isLeft,thumb=thumb,
                        maxAngle=thumb ? (b.bone.name.EndsWith("Proximal")?25:50) : b.bone.name.EndsWith("Intermediate")?78:b.bone.name.EndsWith("Distal")?52:60 };
                }).ToArray();
            animator.enabled = false;
        }
        void SetupArm(Arm arm, Animator animator, bool isLeft, Transform target, Transform pole)
        {
            string side = isLeft ? "Left" : "Right";
            arm.upper = animator.GetBoneTransform(isLeft?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            arm.lower = animator.GetBoneTransform(isLeft?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            arm.hand = animator.GetBoneTransform(isLeft?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            var all = animator.GetComponentsInChildren<Transform>(true);
            arm.upperTwist = all.Single(t=>t.name==side+"UpperArmTwist");
            arm.forearmTwist = all.Single(t=>t.name==side+"ForearmTwist");
            arm.armor = all.Single(t=>t.name==side+"ShoulderArmor");
            arm.upperLength = Vector3.Distance(arm.upper.position, arm.lower.position);
            arm.lowerLength = Vector3.Distance(arm.lower.position, arm.hand.position);
            arm.upperRest=arm.upper.localRotation;arm.lowerRest=arm.lower.localRotation;arm.handRest=arm.hand.localRotation;
            arm.upperTwistRest=arm.upperTwist.localRotation;arm.forearmTwistRest=arm.forearmTwist.localRotation;arm.armorRest=arm.armor.localRotation;
            arm.lowerAxis=arm.lower.InverseTransformDirection(arm.hand.position-arm.lower.position).normalized;
            arm.handAxis=arm.hand.InverseTransformDirection(arm.hand.position-arm.lower.position).normalized;
            arm.target=target;arm.pole=pole;
        }
        public void RestoreBindPose()
        {
            if (!IsReady) return;
            foreach(var b in restBones) if(b.bone) { b.bone.localPosition=b.position; b.bone.localRotation=b.rotation; }
        }
        void LateUpdate() { Evaluate(); }
        public void Evaluate()
        {
            if(!IsReady) return;
            RestoreBindPose();
            SolveArm(left,leftTargetOffset); SolveArm(right,rightTargetOffset);
            foreach(var finger in fingers) if(finger.bone)
                finger.bone.localRotation=finger.rest*Quaternion.AngleAxis(finger.maxAngle*(finger.left?leftGrip:rightGrip),finger.curlAxis);
            if(head && headTarget) head.rotation=Quaternion.Slerp(head.rotation,headTarget.rotation*headTargetOffset,headInfluence);
        }
        void SolveArm(Arm a, Quaternion targetOffset)
        {
            if(!a.target || !a.pole || !a.upper || !a.lower || !a.hand) return;
            Vector3 origin=a.upper.position, reach=a.target.position-origin;
            float requested=reach.magnitude, min=Mathf.Abs(a.upperLength-a.lowerLength)+.0001f;
            float d=Mathf.Clamp(requested,min,a.upperLength+a.lowerLength-.0001f);
            Vector3 forward=requested>.00001f?reach/requested:transform.forward;
            Vector3 bend=Vector3.ProjectOnPlane(a.pole.position-origin,forward);
            if(bend.sqrMagnitude<.000001f) bend=Vector3.ProjectOnPlane(-transform.up,forward);
            if(bend.sqrMagnitude<.000001f) bend=Vector3.ProjectOnPlane(transform.right,forward);
            bend.Normalize();
            float along=(a.upperLength*a.upperLength-a.lowerLength*a.lowerLength+d*d)/(2*d);
            float height=Mathf.Sqrt(Mathf.Max(0,a.upperLength*a.upperLength-along*along));
            Vector3 elbow=origin+forward*along+bend*height, wrist=origin+forward*d;
            a.upper.rotation=Quaternion.FromToRotation(a.lower.position-origin,elbow-origin)*a.upper.rotation;
            a.lower.rotation=Quaternion.FromToRotation(a.hand.position-a.lower.position,wrist-a.lower.position)*a.lower.rotation;
            Quaternion lowerDelta=Quaternion.Inverse(a.lowerRest)*a.lower.localRotation;
            a.upperTwist.localRotation=a.upperTwistRest*Quaternion.Slerp(Quaternion.identity,Twist(lowerDelta,a.lowerAxis),.35f);
            // +Z is the straight finger direction, +Y is the back of the hand.
            // Decompose orientation relative to the solved forearm, not world Euler angles.
            a.neutralFrame=(a.lower.rotation*a.handRest)*Quaternion.Inverse(targetOffset);
            Quaternion delta=Quaternion.Inverse(a.neutralFrame)*a.target.rotation;
            Quaternion twist=Twist(delta,Vector3.forward);
            Quaternion swing=Quaternion.Inverse(twist)*delta;
            Vector3 direction=swing*Vector3.forward;
            float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            float pitch=-Mathf.Atan2(direction.y,Mathf.Sqrt(direction.x*direction.x+direction.z*direction.z))*Mathf.Rad2Deg;
            a.wristFlex=Mathf.Clamp(pitch,-maxWristFlex,maxWristFlex);
            a.wristDeviation=Mathf.Clamp(yaw,-maxWristDeviation,maxWristDeviation);
            float rawRoll=Mathf.DeltaAngle(0,2*Mathf.Atan2(twist.z,twist.w)*Mathf.Rad2Deg);
            if(!a.rollInitialized){a.continuousRoll=rawRoll;a.rollInitialized=true;}
            else a.continuousRoll+=Mathf.DeltaAngle(a.lastRawRoll,rawRoll);
            a.lastRawRoll=rawRoll;a.appliedRoll=Mathf.Clamp(a.continuousRoll,-maxForearmRoll,maxForearmRoll);
            Quaternion limitedSwing=Quaternion.FromToRotation(Vector3.forward,Quaternion.Euler(a.wristFlex,a.wristDeviation,0)*Vector3.forward);
            Quaternion roll=Quaternion.AngleAxis(a.appliedRoll,Vector3.forward);
            // 35% along the proximal forearm, 90% at the distal shell/cuff, 100% at the hand.
            a.lower.rotation=Quaternion.AngleAxis(a.appliedRoll*.35f,(a.hand.position-a.lower.position).normalized)*a.lower.rotation;
            a.forearmTwist.localRotation=a.forearmTwistRest*Quaternion.AngleAxis(a.appliedRoll*.55f,a.lowerAxis);
            a.hand.rotation=a.neutralFrame*roll*limitedSwing*targetOffset;
            a.orientationError=Quaternion.Angle(a.hand.rotation,a.target.rotation*targetOffset);
            a.armor.localRotation=Quaternion.Slerp(Quaternion.identity,a.upper.localRotation*Quaternion.Inverse(a.upperRest),shoulderFollow)*a.armorRest;
            a.targetError=Vector3.Distance(a.hand.position,a.target.position);
            a.clampedDistance=Mathf.Abs(requested-d);
        }
        public static Quaternion Twist(Quaternion q,Vector3 axis)
        {
            Vector3 p=Vector3.Project(new Vector3(q.x,q.y,q.z),axis);
            Quaternion t=new Quaternion(p.x,p.y,p.z,q.w);
            float magnitude=Mathf.Sqrt(t.x*t.x+t.y*t.y+t.z*t.z+t.w*t.w);
            return magnitude>.000001f?new Quaternion(t.x/magnitude,t.y/magnitude,t.z/magnitude,t.w/magnitude):Quaternion.identity;
        }
        public Quaternion HandFrame(Arm a) => a.hand.rotation*Quaternion.Inverse(a==left?leftTargetOffset:rightTargetOffset);
        // Discrete inspection positions, not animation clips.
        public void SetPose(int index)
        {
            if(!IsReady) return;
            Vector3 l,r;Vector3 lr=Vector3.zero,rr=Vector3.zero;float grip;
            switch(index)
            {
                case 1: l=new Vector3(-.29f,1.55f,.28f);r=new Vector3(.29f,1.55f,.28f);lr=new Vector3(-75,0,0);rr=lr;grip=.85f;break;
                case 2: l=new Vector3(-.24f,1.25f,.42f);r=new Vector3(.24f,1.25f,.42f);lr=new Vector3(-15,0,0);rr=lr;grip=.65f;break;
                case 3: l=new Vector3(-.35f,1.84f,.13f);r=new Vector3(.34f,1.03f,.08f);lr=new Vector3(-140,0,0);grip=.25f;break;
                case 4: l=new Vector3(-.38f,1.30f,.29f);r=new Vector3(.38f,1.30f,.29f);lr=new Vector3(0,0,72);rr=new Vector3(0,0,-72);grip=.45f;break;
                case 5: l=new Vector3(-.65f,1.52f,.40f);r=new Vector3(.65f,1.52f,.40f);grip=0;break;
                default:l=new Vector3(-.38f,1.03f,.08f);r=new Vector3(.38f,1.03f,.08f);lr=new Vector3(75,0,0);rr=lr;grip=.12f;break;
            }
            left.target.SetPositionAndRotation(transform.TransformPoint(l),transform.rotation*Quaternion.Euler(lr));
            right.target.SetPositionAndRotation(transform.TransformPoint(r),transform.rotation*Quaternion.Euler(rr));
            left.pole.position=transform.TransformPoint(new Vector3(-.6f,1.18f,-.16f));
            right.pole.position=transform.TransformPoint(new Vector3(.6f,1.18f,-.16f));
            if(index==3) left.pole.position=transform.TransformPoint(new Vector3(-.60f,1.58f,-.08f));
            headTarget.SetPositionAndRotation(transform.TransformPoint(new Vector3(0,1.68f,.07f)),transform.rotation);
            leftGrip=rightGrip=grip;left.rollInitialized=right.rollInitialized=false;Evaluate();
            AlignHand(left,true,index);AlignHand(right,false,index);
            left.rollInitialized=right.rollInitialized=false;Evaluate();
        }
        void AlignHand(Arm a,bool isLeft,int pose)
        {
            Vector3 forward=(a.hand.position-a.lower.position).normalized;
            Vector3 backOfHand=(pose==1?transform.forward:pose==3?-transform.forward:transform.right*(isLeft?-1:1));
            backOfHand=Vector3.ProjectOnPlane(backOfHand,forward);
            if(backOfHand.sqrMagnitude<.001f)backOfHand=Vector3.ProjectOnPlane(transform.up,forward);
            a.target.rotation=Quaternion.LookRotation(forward,backOfHand.normalized);
            if(pose==4)a.target.rotation=Quaternion.AngleAxis(isLeft?65:-65,forward)*a.target.rotation;
        }
        public void SetWristReview(float roll,float grip)
        {
            SetPose(2);
            left.target.rotation=left.neutralFrame*Quaternion.AngleAxis(roll,Vector3.forward);
            right.target.rotation=right.neutralFrame*Quaternion.AngleAxis(-roll,Vector3.forward);
            left.rollInitialized=right.rollInitialized=false;leftGrip=rightGrip=grip;Evaluate();
        }
        void OnDrawGizmosSelected()
        {
            if(!IsReady)return;
            foreach(var a in new[]{left,right})
            {
                if(!a.target||!a.pole) continue;
                Gizmos.color=Color.cyan;Gizmos.DrawWireSphere(a.target.position,.025f);
                Gizmos.color=Color.yellow;Gizmos.DrawWireSphere(a.pole.position,.025f);
                Gizmos.DrawLine(a.upper.position,a.lower.position);Gizmos.DrawLine(a.lower.position,a.hand.position);
            }
        }
    }
}
