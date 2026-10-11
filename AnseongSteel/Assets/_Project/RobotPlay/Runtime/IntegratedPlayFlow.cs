using AnseongSteel.PlayerMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace AnseongSteel.RobotPlay
{
    [DefaultExecutionOrder(750)]
    public sealed class IntegratedPlayFlow : MonoBehaviour
    {
        public enum RunState { Calibrating, Battle, Won, Lost }
        public enum Lesson { Jab, Hook, Defence, CooperativeCombo, Complete }
        public RobotPlaySession session;
        public RobotPilotInput pilot;
        public IntegratedCombatHud bridge;
        public IntegratedBossEncounter boss;
        public float openingGraceSeconds=4,targetDurationSeconds=300;
        public RunState State { get; private set; }
        public Lesson CurrentLesson { get; private set; }
        public float Elapsed => began<0?0:Time.time-began;
        public string Objective { get; private set; }
        public bool Terminal => State==RunState.Won||State==RunState.Lost;
        readonly InputAction retry=new InputAction("Retry encounter",InputActionType.Button,"<XRController>{RightHand}/secondaryButton");
        Text objective;
        float began=-1,readySince=-1;
        bool advance;
        int priorDodges;
        void OnEnable(){retry.Enable();}
        void OnDisable(){retry.Disable();if(boss)boss.Stop();if(objective)objective.gameObject.SetActive(false);}
        void Start()
        {
            bridge.Confirmed+=Confirmed;
            var go=new GameObject("Encounter objective",typeof(RectTransform));go.transform.SetParent(bridge.Hud.HelmetCanvas.transform,false);
            var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,280);rect.sizeDelta=new Vector2(600,75);go.layer=AnseongSteel.Hud.SuitHudController.HelmetLayer;
            objective=go.AddComponent<Text>();var source=bridge.Hud.HelmetCanvas.GetComponentInChildren<Text>();objective.font=source.font;objective.material=source.material;objective.fontSize=17;objective.alignment=TextAnchor.MiddleCenter;objective.color=new Color(.45f,.84f,1);objective.raycastTarget=false;
            Retry();
        }
        void OnDestroy(){if(bridge)bridge.Confirmed-=Confirmed;}
        public void Retry()
        {
            boss.Stop();session.ResetSimulation();pilot.Recenter();bridge.ResetHudCombat();bridge.Hud.SetMenuOpen(false);
            State=RunState.Calibrating;CurrentLesson=Lesson.Jab;began=readySince=-1;advance=false;priorDodges=0;
            session.trainingWindow=true;session.SetBossWindow(true,1,false);
        }
        void Confirmed(FeedbackKind kind,PilotSide side)
        {
            if(State!=RunState.Battle)return;
            if(CurrentLesson==Lesson.Jab&&kind==FeedbackKind.Hit&&session.Combat.arms[(int)side].trajectory.LastCompleted.kind==ObservedPunch.Jab)advance=true;
            if(CurrentLesson==Lesson.Hook&&kind==FeedbackKind.Hit&&session.Combat.arms[(int)side].trajectory.LastCompleted.kind==ObservedPunch.Hook)advance=true;
            if(CurrentLesson==Lesson.Defence&&kind==FeedbackKind.Guard)advance=true;
            if(CurrentLesson==Lesson.CooperativeCombo&&kind==FeedbackKind.Combo)advance=true;
        }
        void Update()
        {
            var keyboard=Keyboard.current;
            if(keyboard!=null&&keyboard.f1Key.wasPressedThisFrame&&!session.NetworkActive)
            {pilot.source=pilot.source==PrototypeInput.XRLocalBoth?PrototypeInput.DesktopBoth:PrototypeInput.XRLocalBoth;Retry();}
            if(retry.WasPressedThisFrame()||(Keyboard.current!=null&&Keyboard.current.rKey.wasPressedThisFrame))Retry();
        }
        void LateUpdate()
        {
            if(session.NetworkActive&&!session.Authority)
            {State=session.State.bossHealth<=0?RunState.Won:session.State.robotHealth<=0?RunState.Lost:RunState.Battle;}
            else if(State==RunState.Calibrating)
            {
                bool ready=session.Combat.arms[0].trajectory.Phase==MotionPhase.Ready&&session.Combat.arms[1].trajectory.Phase==MotionPhase.Ready;
                if(!ready)readySince=-1;else if(readySince<0)readySince=Time.time;
                if(readySince>=0&&Time.time-readySince>.35f){State=RunState.Battle;began=Time.time;boss.Begin(openingGraceSeconds);}
            }
            else if(State==RunState.Battle)
            {
                if(session.State.robotHealth<=0){State=RunState.Lost;boss.Stop();}
                else if(session.State.bossHealth<=0){State=RunState.Won;boss.Stop();}
                if(CurrentLesson==Lesson.Defence&&boss.DodgeCount>priorDodges)advance=true;
                priorDodges=boss.DodgeCount;
                if(advance){advance=false;if(CurrentLesson!=Lesson.Complete)CurrentLesson++;}
            }
            string tip=CurrentLesson==Lesson.Jab?"HOLD GRIP + STRAIGHT PUNCH":CurrentLesson==Lesson.Hook?"HOLD GRIP + OUTWARD THEN CURVED HOOK":CurrentLesson==Lesson.Defence?"READ THE THREAT / GUARD OR COOPERATIVE SIDESTEP":CurrentLesson==Lesson.CooperativeCombo?"RETURN READY / L JAB > R JAB > L HOOK (OR REVERSE)":"FINISH THE BOSS / TRIGGER AFTER FINAL HOOK FOR BOOST";
            Objective=State==RunState.Calibrating?(pilot.source==PrototypeInput.DesktopBoth?"KEYBOARD / HOLD F/G TO ATTACK / 1-4: PUNCH\nRETURN TO READY AFTER EACH STRIKE / F1: VR":"READY BOTH HANDS / Y: RECENTER\nGRIP: ATTACK / TRIGGER: BOOST / F1: KEYBOARD"):State==RunState.Won?"VICTORY / B OR R: RESTART":State==RunState.Lost?"ROBOT DEFEATED / B OR R: RETRY":"DEFEAT THE BOSS  |  "+Mathf.FloorToInt(Elapsed)+"s  /  TARGET ~"+Mathf.RoundToInt(targetDurationSeconds/60)+"m\n"+tip;
            if(objective){objective.text=Objective;objective.rectTransform.anchoredPosition=new Vector2(0,bridge.Hud.HelmetCanvas.GetComponent<RectTransform>().sizeDelta.y*.5f-65);objective.gameObject.SetActive(!bridge.Hud.MenuOpen);}
        }
    }
}