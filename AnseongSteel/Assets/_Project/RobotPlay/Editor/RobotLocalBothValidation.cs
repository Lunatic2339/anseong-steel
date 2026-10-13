using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
namespace AnseongSteel.RobotPlay.Editor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RobotLocalControllerState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('R','L','O','C');
        [InputControl(name="devicePosition",layout="Vector3")] public Vector3 position;
        [InputControl(name="deviceRotation",layout="Quaternion")] public Quaternion rotation;
        [InputControl(layout="Button",format="FLT")] public float isTracked;
        [InputControl(layout="Integer")] public int trackingState;
        [InputControl(layout="Button",format="FLT")] public float trigger;
        [InputControl(layout="Vector2")] public Vector2 thumbstick;
        [InputControl(layout="Button",format="FLT")] public float secondaryButton;
    }
    [InputControlLayout(stateType=typeof(RobotLocalControllerState))]
    public class RobotLocalTestController : XRController { }
    [InitializeOnLoad]
    public static class RobotLocalBothValidation
    {
        const string Active="Robot.LocalBoth.Active",Done="Robot.LocalBoth.Done",Report="Robot.LocalBoth.Report";
        static RobotPlaySession session; static RobotPilotInput input;
        static RobotLocalTestController left,right; static InputSettings original,temporary;
        static int stage,frame=-1; static float at; static double began;
        static readonly List<string> checks=new List<string>();
        static RobotLocalBothValidation(){EditorApplication.update+=Tick;began=EditorApplication.timeSinceStartup;}
        [MenuItem("Anseong Steel/Robot Play/Validate local two-controller input")]
        public static void Run()
        {
            RobotCombatChecks.Run();
            RobotLocalPunchScene.Build();
            SessionState.SetBool(Active,true);SessionState.SetBool(Done,false);EditorApplication.isPlaying=true;
        }
        static void Check(bool ok,string message)
        {if(!ok)throw new Exception(message+" "+(session?JsonUtility.ToJson(session.State):"no session"));checks.Add("PASS: "+message);}
        static void Next(){stage++;at=Time.time;}
        static void Feed(RobotLocalTestController device,bool isLeft,Vector3 offset,bool tracked=true,bool boost=false)
        {
            var baseline=new Vector3(isLeft?-.25f:.25f,1.3f,.3f);
            InputSystem.QueueStateEvent(device,new RobotLocalControllerState{position=baseline+offset/input.motionScale,
                rotation=Quaternion.identity,isTracked=tracked?1:0,trackingState=tracked?3:0,trigger=boost?1:0});
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Active,false))return;
            try
            {
                if(SessionState.GetBool(Done,false))
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    Directory.CreateDirectory("RobotPlayValidation");var result=SessionState.GetString(Report,"");
                    File.WriteAllText("RobotPlayValidation/local-both-checks.txt",result);SessionState.SetBool(Active,false);
                    Debug.Log(result);EditorApplication.Exit(result.Contains("FAIL:")?1:0);return;
                }
                if(EditorApplication.timeSinceStartup-began>180)throw new Exception("local validation timeout");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<15)return;
                EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
                float elapsed=Time.time-at;Vector3 l=Vector3.zero,r=Vector3.zero;bool tracked=true,boost=false;
                switch(stage)
                {
                    case 0:
                        session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();input=UnityEngine.Object.FindFirstObjectByType<RobotPilotInput>();
                        Check(session&&input&&session.contact.arms.Length==2,"existing gameplay scene and both arms loaded");
                        original=InputSystem.settings;temporary=UnityEngine.Object.Instantiate(original);temporary.hideFlags=HideFlags.DontSave;
                        temporary.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        temporary.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=temporary;
                        InputSystem.RegisterLayout<RobotLocalTestController>();
                        left=InputSystem.AddDevice<RobotLocalTestController>();right=InputSystem.AddDevice<RobotLocalTestController>();
                        InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);
                        input.source=PrototypeInput.XRLocalBoth;session.trainingWindow=true;session.ResetSimulation();
                        session.target.onHit.AddListener(()=>{int side=session.target.LastArm=="L"?0:1;var a=session.Combat.arms[side];checks.Add("CONTACT stage="+stage+" time="+(Time.time-at)+" part="+session.contact.arms[side].ContactPart+" phase="+a.trajectory.Phase+" candidate="+JsonUtility.ToJson(a.trajectory.Candidate)+" completed="+JsonUtility.ToJson(a.trajectory.LastCompleted));});Next();break;
                    case 1:if(elapsed>.45f)Next();break;
                    case 2:
                        l=RobotPilotInput.DesktopPose(0,elapsed,false);
                        if(elapsed>1.05f){Check(session.State.hits==1,"left XR controller jab damages exactly once");Next();}break;
                    case 3:
                        r=RobotPilotInput.DesktopPose(1,elapsed,false);
                        if(elapsed>1.05f){Check(session.State.hits==2&&session.State.combo==2,"right XR controller advances cooperative combo");Next();}break;
                    case 4:
                        l=RobotPilotInput.DesktopPose(0,elapsed,true);boost=false;
                        if(elapsed>1.05f){Check(session.State.hits==3&&session.State.bossHealth==124,"recognized left-controller hook reaches contact and damages");Next();}break;
                    case 5:
                        l=r=new Vector3(0,.65f,0);
                        if(elapsed>.4f){Check(session.State.leftGuard&&session.State.rightGuard,"two independent tracked hands can guard together");Next();}break;
                    case 6:
                        tracked=false;r=new Vector3(0,.65f,0);
                        if(elapsed>.4f){Check(!session.State.leftGuard&&session.State.rightGuard,"one hand tracking loss clears only its guard");session.ResetSimulation();Next();}break;
                    case 7:if(elapsed>.5f)Next();break;
                    case 8:
                        r=RobotPilotInput.DesktopPose(1,elapsed,true);
                        if(elapsed>1.05f){Check(session.State.hits==1&&session.State.bossHealth==140,"standalone right-controller hook damages exactly once");Complete();return;}break;
                }
                Feed(left,true,l,tracked,boost);Feed(right,false,r);
            }
            catch(Exception ex){checks.Add("FAIL: "+ex);Complete();}
        }
        static void Complete()
        {
            if(left!=null&&left.added)InputSystem.RemoveDevice(left);if(right!=null&&right.added)InputSystem.RemoveDevice(right);
            if(original)InputSystem.settings=original;if(temporary)UnityEngine.Object.DestroyImmediate(temporary);
            if(session&&session.NetworkActive)session.StopNetwork();
            SessionState.SetString(Report,string.Join("\n",checks));SessionState.SetBool(Done,true);EditorApplication.isPlaying=false;
        }
    }
}