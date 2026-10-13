using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    [DefaultExecutionOrder(900)]
    public sealed class RobotRigVisual : MonoBehaviour
    {
        [System.Serializable] public class Binding
        {
            public Transform upper,lower,hand;
            public Vector3 upperAxis,lowerAxis;
            public Quaternion handOffset;
        }
        public RobotPlaySession session;
        public Binding[] arms=new Binding[2];
        void LateUpdate()
        {
            if(!session||!session.contact)return;
            for(int i=0;i<2;i++)
            {
                var a=arms[i];var source=session.contact.arms[i];if(a==null||!a.upper||!a.lower||!a.hand)continue;
                a.upper.position=source.upperArm.position;
                a.upper.rotation=Quaternion.FromToRotation(a.upper.TransformDirection(a.upperAxis),source.forearm.position-source.upperArm.position)*a.upper.rotation;
                a.lower.position=source.forearm.position;
                a.lower.rotation=Quaternion.FromToRotation(a.lower.TransformDirection(a.lowerAxis),source.hand.position-source.forearm.position)*a.lower.rotation;
                a.hand.SetPositionAndRotation(source.hand.position,source.hand.rotation*a.handOffset);
            }
        }
    }
}
