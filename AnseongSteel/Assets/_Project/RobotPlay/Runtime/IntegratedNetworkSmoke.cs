using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
namespace AnseongSteel.RobotPlay
{
    // Dormant unless explicitly launched by the local process validation harness.
    public sealed class IntegratedNetworkSmoke : MonoBehaviour
    {
        [Serializable] sealed class FrameReport
        {
            public uint epoch;public int run,lesson,kind,beat,attack;public float elapsed,punch,head,torso,leftArm,rightArm,leftLeg,rightLeg;public bool paired,leftReady,rightReady,active;
            public FrameReport(IntegratedFrame f){epoch=f.epoch;run=f.run;lesson=f.lesson;kind=f.kind;beat=f.beat;attack=f.attack;elapsed=f.elapsed;punch=f.punch;head=f.head;torso=f.torso;leftArm=f.leftArm;rightArm=f.rightArm;leftLeg=f.leftLeg;rightLeg=f.rightLeg;paired=f.paired;leftReady=f.leftReady;rightReady=f.rightReady;active=f.active;}
        }
        [Serializable] sealed class RobotReport
        {
            public int bossHealth,robotHealth,combo,next,hits,phase,attackSide;public bool both,leftGuard,rightGuard;public Vector3 left,right,body;
            public RobotReport(RobotSnapshot f){bossHealth=f.bossHealth;robotHealth=f.robotHealth;combo=f.combo;next=f.next;hits=f.hits;phase=f.phase;attackSide=f.attackSide;both=f.both;leftGuard=f.leftGuard;rightGuard=f.rightGuard;left=f.left;right=f.right;body=f.body;}
        }
        [Serializable] sealed class Telemetry
        {
            public string role,connection,error;
            public bool spawned,authority,remote,canRun,ready;
            public uint connectionEpoch;public int starts,restarts,rejectedControls,rejectedInputs,impact,damage,guard,dodge,command;
            public FrameReport frame;public RobotReport robot;public Vector3 camera,seatOffset;
        }
        RobotPlaySession session;IntegratedOnlineState online;IntegratedPlayFlow flow;RobotPilotInput pilot;IntegratedBossEncounter boss;
        string root,role,action="neutral",error="";bool host,pendingBoost;uint sequence;int command;float actionAt,nextSend,nextReport,boot;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--integrated-online-check");
            if(flag<0||flag+2>=args.Length)return;
            var go=new GameObject("Opt-in integrated network validator");var run=go.AddComponent<IntegratedNetworkSmoke>();run.role=args[flag+1];run.root=args[flag+2];run.host=run.role=="host";
        }
        void Start()
        {
            session=FindFirstObjectByType<RobotPlaySession>();online=session.GetComponent<IntegratedOnlineState>();flow=session.GetComponent<IntegratedPlayFlow>();pilot=session.GetComponent<RobotPilotInput>();boss=session.GetComponent<IntegratedBossEncounter>();
            pilot.enabled=false;flow.openingGraceSeconds=60;Application.runInBackground=true;Application.targetFrameRate=60;session.port=17877;Directory.CreateDirectory(root);boot=Time.realtimeSinceStartup;
            session.target.onHit.AddListener(()=>{int side=session.target.LastArm=="L"?0:1;var a=session.Combat.arms[side];Debug.Log("ONLINE_CONTACT action="+action+" age="+(Time.time-actionAt)+" part="+session.contact.arms[side].ContactPart+" body="+session.robotRoot.position+" phase="+a.trajectory.Phase+" candidate="+JsonUtility.ToJson(a.trajectory.Candidate)+" completed="+JsonUtility.ToJson(a.trajectory.LastCompleted));});
            session.StartNetwork(host);
        }
        void Update()
        {
            if(Time.realtimeSinceStartup-boot>210){Application.Quit(2);return;}
            string path=Path.Combine(root,role+"-command.txt");
            try
            {
                if(File.Exists(path))
                {
                    var parts=File.ReadAllText(path).Trim().Split('|');
                    if(parts.Length==2&&int.TryParse(parts[0],out int id)&&id>command)
                    {command=id;var oldAction=action;var oldAt=actionAt;action=parts[1];actionAt=Time.time;Execute();if(parts[1]=="boost"){action=oldAction;actionAt=oldAt;}}
                }
                if(session.IsSpawned&&session.ConnectionState==RobotPlaySession.LanState.Connected&&Time.time>=nextSend)
                {
                    nextSend=Time.time+.02f;float age=Time.time-actionAt;
                    Vector3 pose=action=="jab"?RobotPilotInput.DesktopPose(host?0:1,age,false):action=="hook"?RobotPilotInput.DesktopPose(host?0:1,age,true):action=="guard"?Vector3.up*.65f:Vector3.zero;
                    session.Submit(host?0:1,pose,pendingBoost,action=="move"?Vector2.right:Vector2.zero,++sequence,true);pendingBoost=false;
                    // Exercise the actual right/left authored camera offset independently of input automation.
                    pilot.cockpitCamera.transform.localPosition=pilot.CameraOrigin+pilot.SeatOffset;
                }
                if(Time.unscaledTime>=nextReport){nextReport=Time.unscaledTime+.1f;Report();}
            }
            catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;Report();}
        }
        void Execute()
        {
            if(action=="connect"){if(session.CanStartNetwork)sequence=0;session.StartNetwork(host);session.StartNetwork(host);}
            if(action=="dupstart"){session.StartNetwork(host);session.StartNetwork(host);}
            if(action=="stop"){session.StopNetwork();session.StopNetwork();}
            if(action=="run"){online.RequestStart();online.RequestStart();}
            if(action=="restart"){online.RequestRestart();online.RequestRestart();}
            if(action=="boost")pendingBoost=true;
            if(action=="bossattack"&&host){boss.recoverySeconds=1000;boss.Begin(.1f);}
            if(action=="crossarm")
            {
                var rpc=typeof(RobotPlaySession).GetMethod("SubmitRpc",BindingFlags.NonPublic|BindingFlags.Instance);
                rpc.Invoke(session,new object[]{host?1:0,Vector3.forward*1.2f,false,Vector2.zero,++sequence,true,default(RpcParams)});
            }
            if(action=="fakefeedback")
            {
                try{typeof(RobotPlaySession).GetMethod("FeedbackRpc",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,new object[]{(int)FeedbackKind.Damage,0,99f});error="Feedback RPC attempted; verify authoritative state";}
                catch(TargetInvocationException){error="Feedback RPC denied as expected";}
            }
            if(action=="quit"){Report();Application.Quit(0);}
        }
        void Report()
        {
            var data=new Telemetry{role=role,connection=session.ConnectionState.ToString(),error=error,spawned=session.IsSpawned,authority=session.Authority,remote=session.RemoteConnected,canRun=online.CanRun,ready=online.ReadyBoth,connectionEpoch=session.ConnectionEpoch,starts=online.AcceptedStarts,restarts=online.AcceptedRestarts,rejectedControls=online.RejectedControls,rejectedInputs=session.RejectedInputs,impact=boss.ImpactCount,damage=boss.DamageCount,guard=boss.GuardCount,dodge=boss.DodgeCount,command=command,frame=new FrameReport(online.State),robot=new RobotReport(session.State),camera=pilot.cockpitCamera.transform.localPosition,seatOffset=pilot.SeatOffset};
            var path=Path.Combine(root,role+"-state.json");File.WriteAllText(path+".tmp",JsonUtility.ToJson(data,true));File.Copy(path+".tmp",path,true);
        }
    }
}
