using System;
using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    // Authored on the derived player prefab. Every reference is owned by this
    // prefab; no NPC, v026 or scene bone is inferred by name at runtime.
    public sealed class SoloPracticePlayerRig : MonoBehaviour
    {
        [Serializable]
        public sealed class ArmBinding
        {
            public Transform upper, forearm, hand, guardAnchor;
            public BoxCollider[] volumes;
        }
        public Transform viewAnchor;
        public ArmBinding left, right;
        public Renderer[] visibleArms, localHiddenBody;

        public bool TryBind(Transform robotFrame, out MechaVRBossContact.Arm[] arms, out Vector3[] guards)
        {
            arms=null;guards=null;
            if(!viewAnchor||!viewAnchor.IsChildOf(transform)||visibleArms==null||visibleArms.Length==0)return false;
            var bindings=new[]{left,right};arms=new MechaVRBossContact.Arm[2];guards=new Vector3[2];
            for(int i=0;i<2;i++)
            {
                var a=bindings[i];
                if(a==null||!a.upper||!a.forearm||!a.hand||!a.guardAnchor||a.volumes==null||a.volumes.Length==0)return false;
                if(!a.upper.IsChildOf(transform)||!a.forearm.IsChildOf(a.upper)||!a.hand.IsChildOf(a.forearm)||!a.guardAnchor.IsChildOf(transform)||a.guardAnchor.IsChildOf(a.upper))return false;
                float length=Vector3.Distance(a.upper.position,a.forearm.position)+Vector3.Distance(a.forearm.position,a.hand.position);
                if(length<.1f||Vector3.Distance(a.upper.position,a.guardAnchor.position)>=length*.9f)return false;
                foreach(var c in a.volumes)if(!c||!c.transform.IsChildOf(transform))return false;
                arms[i]=new MechaVRBossContact.Arm{label=i==0?"L":"R",upperArm=a.upper,forearm=a.forearm,hand=a.hand,volumes=a.volumes};
                guards[i]=robotFrame.InverseTransformPoint(a.guardAnchor.position);
            }
            return true;
        }
    }
}
