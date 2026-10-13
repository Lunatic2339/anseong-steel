using UnityEngine;
namespace AnseongSteel.Bosses
{
    [DefaultExecutionOrder(200)]
    public sealed class BossSwordAttachment : MonoBehaviour
    {
        public Transform sword,backSocket,rightHand;
        public Vector3 handPosition=new Vector3(0,0,0);
        public Vector3 handEuler=new Vector3(0,0,0);
        public Transform leftHand;
        public Vector3 leftPalmLocal;
        public Quaternion leftGripRotation=Quaternion.identity;
        [Min(0)] public float rightGripY=.92f, leftGripY=1.018f;
        public Mesh originalSwordMesh;
        [System.Serializable] public struct FingerPose { public Transform bone; public Quaternion rotation; }
        public FingerPose[] fingerPose;
        public bool IsDrawn {get;private set;}
        public float SupportWeight {get;private set;}
        public bool IsTransferring=>transferElapsed<TransferDuration;
        const float TransferDuration=.14f;
        float transferElapsed=TransferDuration, transferStarted;
        Vector3 transferPosition, destinationPosition;
        Quaternion transferRotation, destinationRotation;
        public float LastTransferJump {get;private set;}
        Transform grip;
        Vector3 backPosition,backScale;
        Quaternion backRotation;
        Animator animator;
        BossCombatController combat;
        public Vector3 BladeBase=>sword.TransformPoint(new Vector3(0,.73f,0));
        public Vector3 BladeTip=>sword.TransformPoint(new Vector3(0,.015f,0));
        void Awake()=>Initialize();
        public void Initialize()
        {
            if(grip||!sword||!backSocket||!rightHand)return;
            animator=GetComponent<BossAnimationDriver>().animator;
            combat=GetComponent<BossCombatController>();
            backPosition=sword.localPosition;backRotation=sword.localRotation;backScale=sword.localScale;
            grip=new GameObject("RightHandSwordGrip").transform;grip.SetParent(rightHand,false);
            grip.localPosition=handPosition;grip.localRotation=Quaternion.Euler(handEuler);
        }
        public void PrepareEquip(){Initialize();if(grip){grip.localPosition=handPosition;grip.localRotation=Quaternion.Euler(handEuler);}}
        public void Draw()
        {
            if(!grip||IsDrawn)return;
            transferElapsed=TransferDuration;
            sword.SetParent(grip,true);
            // FBX length is one metre. Anchor the selected hilt point inside the palm without changing world size.
            sword.rotation=grip.rotation;
            sword.position=grip.position-sword.TransformVector(new Vector3(0,rightGripY,0));
            IsDrawn=true;
        }
        public void Sheathe()
        {
            if(!sword||!backSocket||!IsDrawn)return;
            transferElapsed=TransferDuration;
            sword.SetParent(backSocket,false);sword.localPosition=backPosition;sword.localRotation=backRotation;sword.localScale=backScale;IsDrawn=false;
        }
        // Keep the rendered world pose at ownership changes, then remove only the
        // small contact residual in the new socket's local space.
        public void Transfer(bool draw)
        {
            var position=sword.position;var rotation=sword.rotation;
            if(draw)Draw();else Sheathe();
            destinationPosition=sword.localPosition;destinationRotation=sword.localRotation;
            sword.SetPositionAndRotation(position,rotation);
            transferPosition=sword.localPosition;transferRotation=sword.localRotation;
            transferElapsed=0;transferStarted=Time.time;
            LastTransferJump=Vector3.Distance(position,sword.position);
        }
        void SettleTransfer()
        {
            if(!IsTransferring)return;
            transferElapsed=Mathf.Min(TransferDuration,Time.time-transferStarted);
            float t=Mathf.Clamp01(transferElapsed/TransferDuration);
            t=t*t*t*(t*(t*6-15)+10);
            sword.localPosition=Vector3.Lerp(transferPosition,destinationPosition,t);
            sword.localRotation=Quaternion.Slerp(transferRotation,destinationRotation,t);
            if(transferElapsed>=TransferDuration){sword.localPosition=destinationPosition;sword.localRotation=destinationRotation;}
        }
        float Guard(AnimatorStateInfo state)
        {
            if(state.IsName("SwordIdle"))return 1;
            float n=Mathf.Clamp01(state.normalizedTime);
            if(state.IsName("DrawSword"))return Mathf.SmoothStep(0,1,Mathf.InverseLerp(.86f,1,n));
            if(state.IsName("SheatheSword"))return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.10f,n));
            return 0;
        }
        void OnDisable(){transferElapsed=TransferDuration;Sheathe();}
        void LateUpdate()
        {
            SettleTransfer();
            var equip=GetComponent<BossSwordEquip>();if(equip&&equip.IsBusy){ApplyGripPose(equip.GuardWeight,0,equip.SupportWeight);return;}
            float guard=0;
            if(animator&&animator.isActiveAndEnabled)
            {
                guard=Guard(animator.GetCurrentAnimatorStateInfo(0));
                if(animator.IsInTransition(0))guard=Mathf.Lerp(guard,Guard(animator.GetNextAnimatorStateInfo(0)),Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime));
            }
            float roll=0;
            if(combat&&combat.Definition&&combat.Definition.IsSword)
            {
                var definition=combat.Definition;var state=animator.GetCurrentAnimatorStateInfo(0);
                if(animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName(definition.StateName))state=animator.GetNextAnimatorStateInfo(0);
                if(state.IsName(definition.StateName))roll=definition.bladeRoll.Evaluate(Mathf.Clamp01(state.normalizedTime));
            }
            ApplyGripPose(guard,roll);
        }
        public void ApplyGripPose(float guardWeight=0,float bladeRoll=0,float supportMultiplier=1)
        {
            if(!IsDrawn||!sword||!grip){SupportWeight=0;return;}
            grip.localRotation=Quaternion.Euler(handEuler)*Quaternion.AngleAxis(bladeRoll,Vector3.up);
            // An outside diagonal guard leaves the face visible from the opponent's cockpit.
            rightHand.rotation=Quaternion.AngleAxis(-35f*guardWeight,transform.forward)*rightHand.rotation;
            // The sword is anchored inside the right palm. Preserve a closed grip
            // instead of allowing the unarmed finger motion to open around the hilt.
            if(!GetComponent<BossHandPose>()&&fingerPose!=null)foreach(var pose in fingerPose)if(pose.bone)pose.bone.localRotation=pose.rotation;
            SupportWeight=0;
            if(!leftHand||!leftHand.parent||!leftHand.parent.parent)return;
            var forearm=leftHand.parent;var upper=forearm.parent;
            var rotation=grip.rotation*leftGripRotation;
            var palm=sword.TransformPoint(new Vector3(0,leftGripY,0));
            var wrist=palm-rotation*Vector3.Scale(leftPalmLocal,leftHand.lossyScale);
            var origin=upper.position;var elbow=forearm.position;
            float first=Vector3.Distance(origin,elbow),second=Vector3.Distance(elbow,leftHand.position);
            var upperRotation=upper.rotation;var forearmRotation=forearm.rotation;var handRotation=leftHand.rotation;
            float support=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.28f,.65f,Vector3.Distance(wrist,leftHand.position)));
            support*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(first+second-.05f,first+second+.15f,Vector3.Distance(origin,wrist)));
            support*=supportMultiplier;
            SupportWeight=support;
            var delta=wrist-origin;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(first-second)+.001f,first+second-.001f);
            if(delta.sqrMagnitude<.000001f)return;
            var direction=delta.normalized;
            var bend=Vector3.ProjectOnPlane(elbow-origin,direction);
            if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(-transform.right,direction);
            float along=(first*first-second*second+distance*distance)/(2*distance);
            var desiredElbow=origin+direction*along+bend.normalized*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            upper.rotation=Quaternion.FromToRotation(elbow-origin,desiredElbow-origin)*upper.rotation;
            forearm.rotation=Quaternion.FromToRotation(leftHand.position-forearm.position,origin+direction*distance-forearm.position)*forearm.rotation;
            leftHand.rotation=rotation;
            var solvedUpper=upper.rotation;var solvedForearm=forearm.rotation;var solvedHand=leftHand.rotation;
            upper.rotation=Quaternion.Slerp(upperRotation,solvedUpper,support);
            forearm.rotation=Quaternion.Slerp(forearmRotation,solvedForearm,support);
            leftHand.rotation=Quaternion.Slerp(handRotation,solvedHand,support);
        }
    }
}
