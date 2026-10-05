using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    // No motor IO. Swap this event consumer for an approved hardware adapter later.
    public sealed class RobotFeedbackView : MonoBehaviour,IRobotFeedback
    {
        public RobotPlaySession session;
        public Renderer[] boosters;
        float[] until={0,0};
        void OnEnable(){if(session)session.Feedback+=Receive;}
        void OnDisable(){if(session)session.Feedback-=Receive;}
        public void Receive(FeedbackKind kind,PilotSide side,float strength)
        {
            if(kind==FeedbackKind.Boost)until[(int)side]=Time.time+.5f;
            if(kind==FeedbackKind.Hit||kind==FeedbackKind.Guard||kind==FeedbackKind.Boost)
                Debug.Log($"[Robot feedback / mock] {kind} {side} {strength}",this);
        }
        void Update(){for(int i=0;i<boosters.Length;i++)if(boosters[i])boosters[i].enabled=Time.time<until[i];}
    }
}
