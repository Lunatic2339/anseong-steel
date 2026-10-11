using System;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    // Run after the Animator and sword grip/IK, but before combat hit queries.
    [DefaultExecutionOrder(250)]
    public sealed class BossHandPose : MonoBehaviour
    {
        [Serializable] public struct FingerJoint
        {
            public Transform bone;
            public Vector3 localPosition;
            public Quaternion fistRotation;
            public Quaternion relaxedRotation;
            public Quaternion gripRotation;
            public bool left;
        }
        public Animator animator;
        public BossSwordAttachment sword;
        public FingerJoint[] joints;
        [Range(0,1)] public float strength=1;
        public float FistWeight {get;private set;}
        public float GrabWeight {get;private set;}
        public float CastWeight {get;private set;}
        public float RelaxedWeight {get;private set;}
        public float RightGripWeight {get;private set;}
        public float LeftGripWeight {get;private set;}
        float gripBlend;
        Quaternion[] sourceRotations;
        Vector3[] sourcePositions;
        bool overridden;
        void Update()=>Restore();
        void LateUpdate()
        {
            if(!animator||!animator.isActiveAndEnabled){FistWeight=0;return;}
            var equip=GetComponent<BossSwordEquip>();
            if(equip&&equip.IsBusy){gripBlend=equip.GripWeight;Apply(0,strength,equip.GripWeight*strength,GetComponent<BossSwordAttachment>().SupportWeight*strength);return;}
            float weight=StateWeight(animator.GetCurrentAnimatorStateInfo(0));
            float relaxed=RelaxedState(animator.GetCurrentAnimatorStateInfo(0));
            float grab=GrabState(animator.GetCurrentAnimatorStateInfo(0));
            float cast=CastState(animator.GetCurrentAnimatorStateInfo(0));
            if(animator.IsInTransition(0))
            {
                float transition=Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime);
                weight=Mathf.Lerp(weight,StateWeight(animator.GetNextAnimatorStateInfo(0)),transition);
                relaxed=Mathf.Lerp(relaxed,RelaxedState(animator.GetNextAnimatorStateInfo(0)),transition);
                grab=Mathf.Lerp(grab,GrabState(animator.GetNextAnimatorStateInfo(0)),transition);
                cast=Mathf.Lerp(cast,CastState(animator.GetNextAnimatorStateInfo(0)),transition);
            }
            if(sword&&sword.IsDrawn){weight=0;grab=0;}
            gripBlend=Mathf.MoveTowards(gripBlend,sword&&sword.IsDrawn?1:0,Time.deltaTime*10);
            if(sword&&sword.IsDrawn)relaxed=1;
            Apply(weight*strength,relaxed*strength,gripBlend*strength,gripBlend*(sword?sword.SupportWeight:0)*strength,grab*strength,cast*strength);
        }
        static bool IsGrab(AnimatorStateInfo state)=>state.IsName("Combat_Grab")||state.IsName("Combat_ArmLock");
        static bool IsCast(AnimatorStateInfo state)=>state.IsName("Combat_EMP")||state.IsName("Combat_SensorJam")||state.IsName("Combat_VisionDisruption");
        public static float CastEnvelope(float n)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.15f,n))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,.98f,n)));
        static float CastState(AnimatorStateInfo state)=>IsCast(state)?CastEnvelope(Mathf.Clamp01(state.normalizedTime)):0;
        static float GrabState(AnimatorStateInfo state)=>IsGrab(state)?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.32f,.60f,state.normalizedTime)):0;
        static float RelaxedState(AnimatorStateInfo state)=>state.IsName("Idle")||state.IsName("Walk")||state.IsName("WalkBackward")||state.IsName("SwordIdle")||state.IsName("Punch")||state.IsName("Combat_AlternatingCombo")||IsGrab(state)||IsCast(state)?1:0;
        static float StateWeight(AnimatorStateInfo state)
        {
            if(!state.IsName("Punch")&&!state.IsName("Combat_AlternatingCombo"))return 0;
            float n=Mathf.Clamp01(state.normalizedTime);
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.08f,n))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(state.IsName("Punch")?.80f:.70f,.98f,n)));
        }
        public void Apply(float weight,float relaxed=0,float rightGrip=0,float leftGrip=0,float grab=0,float cast=0)
        {
            FistWeight=Mathf.Clamp01(weight);RelaxedWeight=Mathf.Clamp01(relaxed);RightGripWeight=Mathf.Clamp01(rightGrip);LeftGripWeight=Mathf.Clamp01(leftGrip);
            GrabWeight=Mathf.Clamp01(grab);
            CastWeight=Mathf.Clamp01(cast);
            if(joints==null)return;
            if(Mathf.Max(FistWeight,RelaxedWeight,RightGripWeight,LeftGripWeight,GrabWeight,CastWeight)<=0)return;
            if(sourceRotations==null||sourceRotations.Length!=joints.Length){sourceRotations=new Quaternion[joints.Length];sourcePositions=new Vector3[joints.Length];}
            for(int i=0;i<joints.Length;i++)
            {
                var joint=joints[i];if(!joint.bone)continue;
                sourceRotations[i]=joint.bone.localRotation;sourcePositions[i]=joint.bone.localPosition;
                var rotation=Quaternion.Slerp(sourceRotations[i],joint.relaxedRotation,RelaxedWeight);
                // Release the thumb slightly ahead of the fingers, without exposing raw clip poses.
                float curl=joint.bone.name.Contains("Thumb")?Mathf.Pow(FistWeight,1.35f):FistWeight;
                rotation=Quaternion.Slerp(rotation,joint.fistRotation,curl);
                rotation=Quaternion.Slerp(rotation,joint.fistRotation,CastWeight*.92f);
                // Leave room for the opponent's forearm; the supporting hand stays relaxed.
                if(!joint.left)rotation=Quaternion.Slerp(rotation,Quaternion.Slerp(joint.relaxedRotation,joint.fistRotation,joint.bone.name.Contains("Thumb")?.64f:.72f),GrabWeight);
                float grip=joint.left?LeftGripWeight:RightGripWeight;
                joint.bone.localRotation=Quaternion.Slerp(rotation,joint.gripRotation,grip);
                joint.bone.localPosition=Vector3.Lerp(sourcePositions[i],joint.localPosition,Mathf.Max(FistWeight,RelaxedWeight,grip,joint.left?0:GrabWeight,CastWeight));
            }
            overridden=true;
        }
        // Mixamo omits constant distal channels. Restore their animation input
        // before the next Animator evaluation so an additive override cannot stick.
        public void Restore()
        {
            if(!overridden)return;
            for(int i=0;i<joints.Length;i++)if(joints[i].bone){joints[i].bone.localRotation=sourceRotations[i];joints[i].bone.localPosition=sourcePositions[i];}
            overridden=false;
        }
        void OnDisable(){Restore();FistWeight=RelaxedWeight=RightGripWeight=LeftGripWeight=GrabWeight=CastWeight=gripBlend=0;}
    }
}
