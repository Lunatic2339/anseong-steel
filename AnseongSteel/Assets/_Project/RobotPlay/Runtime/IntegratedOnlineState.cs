using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using AnseongSteel.Hud;
using AnseongSteel.PlayerMotion;
namespace AnseongSteel.RobotPlay
{
    [Serializable]
    public struct IntegratedFrame : INetworkSerializable, IEquatable<IntegratedFrame>
    {
        public uint epoch;
        public int run,lesson,kind,beat,attack;
        public float elapsed,punch,head,torso,leftArm,rightArm,leftLeg,rightLeg;
        public bool paired,leftReady,rightReady,active;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref epoch);s.SerializeValue(ref run);s.SerializeValue(ref lesson);
            s.SerializeValue(ref kind);s.SerializeValue(ref beat);s.SerializeValue(ref attack);
            s.SerializeValue(ref elapsed);s.SerializeValue(ref punch);s.SerializeValue(ref paired);
            s.SerializeValue(ref leftReady);s.SerializeValue(ref rightReady);s.SerializeValue(ref active);
            s.SerializeValue(ref head);s.SerializeValue(ref torso);s.SerializeValue(ref leftArm);
            s.SerializeValue(ref rightArm);s.SerializeValue(ref leftLeg);s.SerializeValue(ref rightLeg);
        }
        public bool Equals(IntegratedFrame o)=>epoch==o.epoch&&run==o.run&&lesson==o.lesson&&kind==o.kind&&beat==o.beat&&attack==o.attack&&elapsed==o.elapsed&&punch==o.punch&&paired==o.paired&&leftReady==o.leftReady&&rightReady==o.rightReady&&active==o.active&&head==o.head&&torso==o.torso&&leftArm==o.leftArm&&rightArm==o.rightArm&&leftLeg==o.leftLeg&&rightLeg==o.rightLeg;
        public float Part(int i)=>i==0?head:i==1?torso:i==2?leftArm:i==3?rightArm:i==4?leftLeg:rightLeg;
    }
    // Only the host writes progression and persistent suit condition. RPC accepts commands, never damage.
    [DefaultExecutionOrder(800)]
    public sealed class IntegratedOnlineState : NetworkBehaviour
    {
        readonly NetworkVariable<IntegratedFrame> replicated=new NetworkVariable<IntegratedFrame>();
        IntegratedFrame frame;
        RobotPlaySession session;IntegratedPlayFlow flow;IntegratedBossEncounter boss;IntegratedCombatHud bridge;RobotPilotInput pilot;
        uint command,lastCommand,lastEpoch;int lastAttack=-1;float nextPublish,lastControl=-100;
        bool priorRemote;
        public bool StartRequested { get; private set; }
        public int AcceptedStarts { get; private set; }
        public int AcceptedRestarts { get; private set; }
        public int RejectedControls { get; private set; }
        public IntegratedFrame State=>session&&session.NetworkActive&&!session.Authority?replicated.Value:frame;
        public bool CanRun=>session&&session.ConnectionState==RobotPlaySession.LanState.Connected&&session.IsSpawned&&(session.Authority?session.RemoteConnected:State.paired);
        public bool IsBattle=>CanRun&&flow.State==IntegratedPlayFlow.RunState.Battle;
        public override void OnNetworkSpawn(){if(IsServer)flow.Retry();}
        bool Ready(int side)=>session.FreshInput(side)&&session.Combat.arms[side].trajectory.Phase==MotionPhase.Ready;
        public bool ReadyBoth=>CanRun&&session.Authority&&Ready(0)&&Ready(1);
        public string Cue=>!CanRun?"WAITING FOR TWO PILOTS":State.beat==(int)IntegratedBossEncounter.Beat.Warning||State.beat==(int)IntegratedBossEncounter.Beat.Punch?State.kind==2?"UNBLOCKABLE / MATCH STICKS L OR R":State.kind==1?"HEAVY / BOTH GUARDS":"NORMAL / RIGHT GUARD":"RECOVERY / COUNTER";
        void Awake(){session=GetComponent<RobotPlaySession>();flow=GetComponent<IntegratedPlayFlow>();boss=GetComponent<IntegratedBossEncounter>();bridge=GetComponent<IntegratedCombatHud>();pilot=GetComponent<RobotPilotInput>();}
        public void AuthorityReset()
        {
            if(!session.Authority)return;
            frame=new IntegratedFrame{epoch=frame.epoch+1};StartRequested=false;lastAttack=-1;
        }
        public void RecordFeedback(FeedbackKind kind,PilotSide side,float amount)
        {
            if(!session.Authority||kind!=FeedbackKind.Damage)return;
            float value=amount/Mathf.Max(1,session.tuning.robotHealth);
            // The existing attack-to-region mapping is authoritative; not anatomical collision detection.
            if(boss.Kind==IntegratedBossEncounter.AttackKind.Heavy)frame.torso=Mathf.Clamp01(frame.torso+value);
            else if(side==PilotSide.Left)frame.leftArm=Mathf.Clamp01(frame.leftArm+value);
            else frame.rightArm=Mathf.Clamp01(frame.rightArm+value);
        }
        public void RequestStart()=>SendControl(true);
        public void RequestRestart()=>SendControl(false);
        void SendControl(bool start)
        {
            if(!session.NetworkActive){if(!start)flow.Retry();return;}
            if(!IsSpawned)return;
            ControlRpc(start,State.epoch,++command);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        void ControlRpc(bool start,uint expectedEpoch,uint sequence,RpcParams rpc=default)
        {
            if(!IsServer||rpc.Receive.SenderClientId!=NetworkManager.ServerClientId||expectedEpoch!=frame.epoch||!IntegratedLanRules.IsNewSequence(sequence,lastCommand)||Time.realtimeSinceStartup-lastControl<.35f)
            {RejectedControls++;return;}
            if(start&&(!ReadyBoth||flow.State==IntegratedPlayFlow.RunState.Battle||flow.Terminal))
            {RejectedControls++;return;}
            lastCommand=sequence;lastControl=Time.realtimeSinceStartup;
            if(start){StartRequested=true;AcceptedStarts++;}else{flow.Retry();AcceptedRestarts++;}
        }
        void Update()
        {
            if(session.NetworkActive&&session.LocalSide==0&&Keyboard.current!=null&&Keyboard.current.enterKey.wasPressedThisFrame)RequestStart();
        }
        void LateUpdate()
        {
            if(!session.NetworkActive)return;
            if(session.Authority)
            {
                if(!boss.driver.enabled){boss.driver.enabled=true;boss.driver.ResetMotion();}
                if(priorRemote&&!session.RemoteConnected)flow.Retry();
                priorRemote=session.RemoteConnected;
                frame.run=(int)flow.State;frame.lesson=(int)flow.CurrentLesson;frame.elapsed=flow.Elapsed;
                frame.paired=CanRun;frame.leftReady=Ready(0);frame.rightReady=Ready(1);
                frame.kind=(int)boss.Kind;frame.beat=(int)boss.Phase;frame.active=boss.Active;
                frame.attack=boss.AttackSerial;frame.punch=boss.PunchSeconds;
                if(IsSpawned&&Time.unscaledTime>=nextPublish){replicated.Value=frame;nextPublish=Time.unscaledTime+.033f;}
            }
            else
            {
                // Visual playback only. Client animation events cannot apply damage or advance learning.
                if(boss.driver.enabled){boss.driver.ResetMotion();boss.driver.enabled=false;}
                var state=State;
                if(state.epoch!=lastEpoch||!state.active){boss.driver.animator.Play("Idle",0,0);lastEpoch=state.epoch;lastAttack=-1;}
                else if(state.attack!=lastAttack&&state.punch<boss.driver.punchDuration)
                {lastAttack=state.attack;boss.driver.animator.Play("Punch",0,Mathf.Clamp01(state.punch/boss.driver.punchDuration));}
            }
            if(bridge.Hud)for(int i=0;i<6;i++)bridge.Hud.Damage.SetDamage((SuitPart)i,State.Part(i));
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12,12,380,235),GUI.skin.box);
            GUILayout.Label("LAN / "+session.ConnectionState+" / "+(session.LocalSide==0?"LEFT":"RIGHT"));
            GUILayout.Label(session.ConnectionMessage);
            if(session.OfflineAllowed)
            {
                session.hostAddress=GUILayout.TextField(session.hostAddress);
                if(GUILayout.Button("HOST / LEFT PILOT"))session.StartNetwork(true);
                if(GUILayout.Button("JOIN / RIGHT PILOT"))session.StartNetwork(false);
                GUILayout.EndArea();return;
            }
            GUILayout.Label("Two pilots: "+State.paired+" / ready L "+State.leftReady+" R "+State.rightReady);
            GUI.enabled=session.Authority;
            if(GUILayout.Button("START RUN (HOST)"))RequestStart();
            if(GUILayout.Button("RESTART RUN (HOST)"))RequestRestart();
            GUI.enabled=true;
            if(session.CanStartNetwork)
            {
                if(GUILayout.Button("RECONNECT"))session.StartNetwork(session.LocalSide==0);
                if(GUILayout.Button("OFFLINE TEST")&&session.UseOfflineTest()){boss.driver.enabled=true;flow.Retry();}
            }
            else if(GUILayout.Button("DISCONNECT"))session.StopNetwork();
            GUILayout.EndArea();
        }
    }
}
