using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;

namespace AnseongSteel.RobotPlay
{
    // Opt-in two-process regression runner, dormant during normal play.
    public sealed class RobotNetworkSmoke : MonoBehaviour
    {
        RobotPlaySession session;RobotPilotInput input;
        bool host,started,finished;int phase;float clock,start=-100,nextSend,completedAt=-1;uint sequence;
        string report;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args=Environment.GetCommandLineArgs();bool host=Array.IndexOf(args,"--robot-check-host")>=0;bool client=Array.IndexOf(args,"--robot-check-client")>=0;
            if(!host&&!client)return;
            var go=new GameObject("Opt-in network smoke runner");var run=go.AddComponent<RobotNetworkSmoke>();run.host=host;
            int path=Array.IndexOf(args,"--robot-check-report");run.report=path>=0&&path+1<args.Length?args[path+1]:"network-check.txt";
        }
        void Start()
        {
            session=FindFirstObjectByType<RobotPlaySession>();input=FindFirstObjectByType<RobotPilotInput>();input.enabled=false;
            Application.runInBackground=true;session.trainingWindow=true;session.StartNetwork(host);clock=Time.realtimeSinceStartup;
        }
        void Update()
        {
            if(finished)return;
            if(Time.realtimeSinceStartup-clock>55){Finish(false,"timeout");return;}
            if(!session.IsSpawned)return;
            if(host&&!started&&NetworkManager.Singleton.ConnectedClientsIds.Count<2)return;
            if(!started){started=true;start=Time.time+1;phase=0;}
            float age=Time.time-start;int side=host?0:1;
            if(host&&phase==0&&age>=0){phase=1;start=Time.time;age=0;}
            if(!host&&phase==0&&session.State.hits==1){phase=1;start=Time.time+.35f;age=-.35f;}
            if(host&&phase==1&&session.State.hits==2){phase=2;start=Time.time+.35f;age=-.35f;}
            Vector3 pose=Vector3.zero;
            if(phase>0)pose=RobotPilotInput.DesktopPose(side,age,host&&phase==2);
            if(Time.time>=nextSend)
            {
                nextSend=Time.time+.02f;
                session.Submit(side,pose,host&&phase==2&&age>=.32f&&age<.4f,Vector2.zero,++sequence);
            }
            if(session.State.hits==3)
            {
                if(completedAt<0)completedAt=Time.time;
                if(Time.time-completedAt>(host?2:1))Finish(session.State.bossHealth==104&&session.State.combo==0,"three-hit shared state");
            }
        }
        void Finish(bool pass,string detail)
        {
            finished=true;string result=(pass?"PASS":"FAIL")+" "+(host?"HOST":"CLIENT")+" "+detail+"\n"+JsonUtility.ToJson(session.State,true);
            File.WriteAllText(report,result);Debug.Log(result);Application.Quit(pass?0:1);
        }
    }
}
