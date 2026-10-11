using AnseongSteel.Hud;
using AnseongSteel.PlayerMotion;
using UnityEngine;
using UnityEngine.UI;
namespace AnseongSteel.RobotPlay
{
    // Reuses the authored HUD without changing its standalone demo or its input owner.
    [DefaultExecutionOrder(650)]
    public sealed class IntegratedCombatHud : MonoBehaviour
    {
        public RobotPlaySession session;
        public RobotPilotInput pilot;
        public ArmMotionInput cockpitContext;
        public CockpitLiveView feed;
        public SuitHudController Hud { get; private set; }
        public string CombatText { get; private set; }
        public int ConfirmedHits { get; private set; }
        public int Guards { get; private set; }
        public int Dodges { get; private set; }
        public int IncomingDamage { get; private set; }
        public event System.Action<FeedbackKind,PilotSide> Confirmed;
        Text response,link,explanation,flag;Button restartButton;Text restartLabel;
        int previousHealth,previousPhase=-1;
        float flashUntil;
        string last="READY";
        Vector3 centerOffset,leftOffset,rightOffset;
        Quaternion centerRotation,leftRotation,rightRotation;
        void Start()
        {
            foreach(var t in session.robotRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=27;
            foreach(var t in session.target.GetComponentsInChildren<Transform>(true))t.gameObject.layer=27;
            feed.playerBody=null;feed.firstPersonArms=null;
            cockpitContext.cockpitCamera.cullingMask &= ~(1<<27);
            foreach(var c in new[]{feed.exteriorCamera,feed.leftCamera,feed.rightCamera})c.cullingMask=(1<<27)|(1<<BattlefieldGuide.GuideLayer);
            Hud=SuitHudController.Attach(cockpitContext,feed);
            foreach(var t in Hud.HelmetCanvas.GetComponentsInChildren<Text>(true))
            {
                if(t.name=="Response")response=t;if(t.name=="Pilot")link=t;
                if(t.name=="Demo explanation")explanation=t;if(t.name=="Prototype flag")flag=t;
                if(t.text=="DAMAGE DEMO / RESET") { t.text="RESTART RUN";var button=t.GetComponent<Button>();restartButton=button;restartLabel=t;button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>{if(session.NetworkActive)GetComponent<IntegratedOnlineState>().RequestRestart();else GetComponent<IntegratedPlayFlow>().Retry();}); }
            }
            if(response){response.rectTransform.sizeDelta=new Vector2(310,120);response.fontSize=16;}
            if(flag)flag.text="LIVE / TEST";
            if(explanation)explanation.text="ACTUAL COMBAT / TEST HIT-REGION MAPPING\nESC: SETTINGS   R: RETRY";
            previousHealth=session.State.robotHealth;
            centerOffset=Vector3.up*1.65f;leftOffset=centerOffset;rightOffset=centerOffset;
            centerRotation=Quaternion.identity;leftRotation=Quaternion.Euler(0,-CockpitLiveView.SectorDegrees,0);rightRotation=Quaternion.Euler(0,CockpitLiveView.SectorDegrees,0);
            session.Feedback+=Feedback;
        }
        public void ResetHudCombat()
        { if(Hud)Hud.Damage.Reset();ConfirmedHits=Guards=Dodges=IncomingDamage=0;last="READY";previousPhase=-1;previousHealth=session.tuning.robotHealth; }
        void OnDestroy(){if(session)session.Feedback-=Feedback;}
        void Feedback(FeedbackKind kind,PilotSide side,float strength)
        {
            if(kind==FeedbackKind.Hit)ConfirmedHits++;
            if(kind==FeedbackKind.Guard)Guards++;
            if(kind==FeedbackKind.Damage && Hud && !session.NetworkActive)
            {
                IncomingDamage+=(int)strength;
                // Explicit prototype attack-region mapping; not anatomical impact detection.
                var state=session.State;
                SuitPart part=state.both?SuitPart.Torso:side==PilotSide.Left?SuitPart.LeftArm:SuitPart.RightArm;
                Hud.Damage.AddDamage(part,strength/Mathf.Max(1,session.tuning.robotHealth));
            }
            last=kind+" "+side;flashUntil=Time.unscaledTime+1.5f;
            Confirmed?.Invoke(kind,side);
        }
        void LateUpdate()
        {
            if(!Hud)return;
            if(restartButton){restartButton.interactable=!session.NetworkActive||session.Authority;restartLabel.text=session.NetworkActive&&!session.Authority?"HOST RESTART ONLY":"RESTART RUN";}
            var s=session.State;
            if(s.robotHealth>previousHealth) { Hud.Damage.Reset();ConfirmedHits=Guards=Dodges=IncomingDamage=0; }
            if(previousPhase==0&&s.phase==1&&!session.trainingWindow && s.robotHealth==previousHealth && s.body.z<=-.55f)
            { Dodges++;last="EVADED / BODY MOVEMENT";flashUntil=Time.unscaledTime+1.5f; }
            previousHealth=s.robotHealth;previousPhase=s.phase;
            var encounter=session.GetComponent<IntegratedBossEncounter>();
            var online=session.GetComponent<IntegratedOnlineState>();
            string window=session.NetworkActive&&online?online.Cue:encounter&&encounter.Active?encounter.Cue:session.trainingWindow?"TRAINING":s.phase==0?(s.both?"INCOMING: BOTH GUARD":"INCOMING: "+(s.attackSide==0?"LEFT":"RIGHT")):"ATTACK WINDOW";
            CombatText=$"BOSS {s.bossHealth} / ROBOT {s.robotHealth}\nHITS {s.hits}  COMBO {s.combo}/2\n{window}";
            if(response)response.text=CombatText+"\n"+(Time.unscaledTime<flashUntil?last:"");
            bool tracked=pilot.leftTracked.IsPressed()&&pilot.rightTracked.IsPressed();
            if(link)link.text=session.NetworkActive?"LAN / "+(session.LocalSide==0?"LEFT":"RIGHT")+" / "+session.ConnectionState:pilot.source==PrototypeInput.DesktopBoth?"LOCAL / KEYBOARD":tracked?"LOCAL / TWO HANDS":"LOCAL / NO XR";
            // Keep robot optics independent of HMD head motion and the cockpit's physical display.
            feed.exteriorCamera.transform.SetPositionAndRotation(session.robotRoot.position+centerOffset,centerRotation);
            feed.leftCamera.transform.SetPositionAndRotation(session.robotRoot.position+leftOffset,leftRotation);
            feed.rightCamera.transform.SetPositionAndRotation(session.robotRoot.position+rightOffset,rightRotation);
            Vector3 a=session.robotRoot.TransformPoint(RobotPlaySession.Neutral(0));
            var box=session.target.GetComponent<BoxCollider>();
            Vector3 b=box?box.transform.TransformPoint(box.center):session.target.transform.position+Vector3.up*1.45f;
            Hud.SupplyWorldGuide(new[]{a,Vector3.Lerp(a,b,.5f)+Vector3.up*.06f,b});
            if(session.targetMarker)session.targetMarker.material.color=s.phase==0&&!session.trainingWindow?Color.red:new Color(.1f,.8f,.5f);
        }
    }
}