using UnityEngine;
using UnityEngine.XR;
namespace AnseongSteel.RobotPlay
{
    public sealed class IntegratedFeedback : MonoBehaviour
    {
        public RobotPlaySession session;
        public AudioClip hit,guard,damage,boost;
        AudioSource audioSource;
        void Start(){audioSource=gameObject.AddComponent<AudioSource>();audioSource.spatialBlend=0;audioSource.volume=.4f;session.Feedback+=Receive;}
        void OnDestroy(){if(session)session.Feedback-=Receive;}
        void Receive(FeedbackKind kind,PilotSide side,float strength)
        {
            float intensity=kind==FeedbackKind.Damage?.65f:kind==FeedbackKind.Boost?.9f:kind==FeedbackKind.Guard?.2f:.35f;
            float duration=kind==FeedbackKind.Damage?.14f:.07f;
            var device=InputDevices.GetDeviceAtXRNode(side==PilotSide.Left?XRNode.LeftHand:XRNode.RightHand);
            if(device.isValid&&device.TryGetHapticCapabilities(out var caps)&&caps.supportsImpulse)device.SendHapticImpulse(0,intensity,duration);
            AudioClip clip=kind==FeedbackKind.Hit?hit:kind==FeedbackKind.Guard?guard:kind==FeedbackKind.Damage?damage:kind==FeedbackKind.Boost?boost:null;
            if(clip)audioSource.PlayOneShot(clip);
        }
    }
}