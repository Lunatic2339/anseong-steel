using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Utilities;
using System.Runtime.InteropServices;
using AnseongSteel.PlayerMotion;
using Unity.Netcode;

namespace AnseongSteel.RobotPlay.Editor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ReachTestState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('R','E','A','C');
        [InputControl(name="devicePosition",layout="Vector3")] public Vector3 position;
        [InputControl(name="deviceRotation",layout="Quaternion")] public Quaternion rotation;
        [InputControl(layout="Button",format="FLT")] public float isTracked;
        [InputControl(layout="Integer")] public int trackingState;
        [InputControl(layout="Button",format="FLT")] public float gripPressed;
        [InputControl(layout="Button",format="FLT")] public float trigger;
        [InputControl(layout="Button",format="FLT")] public float secondaryButton;
        [InputControl(layout="Button",format="FLT")] public float primaryButton;
        [InputControl(layout="Button",format="FLT")] public float thumbstickClicked;
        [InputControl(layout="Vector2")] public Vector2 thumbstick;
    }
    [InputControlLayout(stateType=typeof(ReachTestState))]
    public class ReachTestController : XRController { }

    [InitializeOnLoad]
    public static class PersonalReachValidation
    {
        const string Active="PersonalReach.Active",Done="PersonalReach.Done",Output="PersonalReach.Output",Report="PersonalReach.Report";
        static readonly List<string> lines=new List<string>();
        static RobotPlaySession session;static RobotPilotInput pilot;
        static XRHMD head;static ReachTestController left,right;
        static int stage,frame=-1;static float at;static double deadline;
        static Vector3 guardElbow,guardHand,backHand;static float maxExtension;
        static PersonalReachValidation(){EditorApplication.update+=Tick;deadline=EditorApplication.timeSinceStartup+120;EditorApplication.playModeStateChanged+=PlayChanged;}
        static void PlayChanged(PlayModeStateChange change)
        {
            if(change==PlayModeStateChange.ExitingPlayMode&&SessionState.GetBool(Active,false)&&!SessionState.GetBool(Done,false))
            {lines.Add("FAIL: Play stopped before input verification completed; fixture cleanup executed.");Finish();}
        }
        static void Check(bool condition,string label){if(!condition)throw new Exception(label);lines.Add("PASS: "+label);}
        [MenuItem("Anseong Steel/Player Motion/Verify personal reach (synthetic input)")]
        public static void RunInteractive()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty||string.IsNullOrEmpty(scene.path)||UnityEngine.SceneManagement.SceneManager.sceneCount!=1)
            {Debug.LogError("Save or finish the current scene before running synthetic reach verification.");return;}
            SessionState.SetBool("PersonalReach.Interactive",true);SessionState.SetString("PersonalReach.RestoreScene",scene.path);Run();
        }
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--reach-output");string output=i>=0?args[i+1]:"PersonalReachValidation";
            if(SessionState.GetBool("PersonalReach.Interactive",false))output=Path.GetFullPath("../Audit/2026-10-14/evidence/18_tracking/main");
            Directory.CreateDirectory(output);SessionState.SetString(Output,output);
            try{Core();File.WriteAllLines(Path.Combine(output,"reach-core.txt"),lines);RobotCombatChecks.Run();IntegratedRulesChecks.Run();
                var type=typeof(AnseongSteel.PlayerMotion.Editor.ArmMotionValidation);type.GetMethod("Core",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                File.WriteAllLines(Path.Combine(output,"legacy-motion-core.txt"),(List<string>)type.GetField("checks",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);SessionState.SetBool(Active,true);SessionState.SetBool(Done,false);EditorApplication.isPlaying=true;
            }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());if(!SessionState.GetBool("PersonalReach.Interactive",false))EditorApplication.Exit(1);else {SessionState.SetBool("PersonalReach.Interactive",false);Debug.LogException(e);}}
        }
        static ArmReachCalibration Profile(float reach)
        {
            var p=new ArmReachCalibration();Check(p.CaptureGuard(Vector3.zero,Vector3.forward*reach*.45f,Quaternion.identity,ShoulderReference.HeadRelativeEstimate,out _),"guard captured for effective reach "+reach);
            Check(p.CaptureReach(Vector3.forward*reach,out _),"reach captured "+reach);return p;
        }
        static void Core()
        {
            var invalid=new ArmReachCalibration();Check(!invalid.CaptureGuard(Vector3.zero,Vector3.forward*.3f,new Quaternion(0,0,0,0),ShoulderReference.HeadRelativeEstimate,out _),"invalid rotation cannot calibrate");
            Check(!invalid.CaptureReach(Vector3.forward*.6f,out _),"reach requires explicit guard");
            var shortArm=Profile(.42f);var longArm=Profile(.95f);var shoulder=new Vector3(-.8f,1.6f,0);var guard=RobotPlaySession.Neutral(0);const float length=1.9f;
            foreach(var normalized in new[]{new Vector3(-.15f,0,-.18f),new Vector3(.18f,.03f,.10f),Vector3.forward*.55f})
            {
                var a=shortArm.Map(normalized*.42f,shoulder,guard,length);var b=longArm.Map(normalized*.95f,shoulder,guard,length);
                Check(Vector3.Distance(a,b)<.0001f,"short/long users share relative workspace at "+normalized);
            }
            Check(Vector3.Distance(shortArm.Map(Vector3.zero,shoulder,guard,length),guard)<.0001f,"guard anchor retained");
            var back=shortArm.Map(new Vector3(-.12f,0,-.20f),shoulder,guard,length);
            Check(Vector3.Distance(back,guard)>.15f&&Vector3.Distance(back,shoulder)/length<.85f,"rearward pull retains travel and elbow bend");
            var compact=shortArm.Map(new Vector3(.06f,0,0),shoulder,guard,length);
            Check(Vector3.Distance(compact,shoulder)/length<.85f,"small lateral motion cannot fully extend compact hook");
            Check(Vector3.Distance(shortArm.Map(Vector3.forward*.42f*.55f,shoulder,guard,length),shoulder)/length>.99f&&Vector3.Distance(shortArm.Map(Vector3.forward*.42f*.55f,shoulder,guard,length),shoulder)<length,"explicit maximum reach is nearly straight without exceeding bone length");
            foreach(float rg in new[]{.1f,.45f,.8f})foreach(float vg in new[]{.1f,.28f,.9f})
            {
                float previous=-1;for(int n=0;n<=2000;n++){float r=n*.001f,v=ArmReachCalibration.MapRadius(r,rg,vg);if(v+1e-6f<previous||v>=.99996f)throw new Exception("nonmonotonic reach curve");previous=v;}
                float e=.0001f;float lm=(ArmReachCalibration.MapRadius(rg,rg,vg)-ArmReachCalibration.MapRadius(rg-e,rg,vg))/e;
                float rm=(ArmReachCalibration.MapRadius(rg+e,rg,vg)-ArmReachCalibration.MapRadius(rg,rg,vg))/e;
                Check(Mathf.Abs(lm-rm)<.04f,"continuous guard derivative "+rg+"/"+vg);
            }
            Check(!shortArm.CaptureReach(new Vector3(float.NaN,0,0),out _)&&shortArm.IsComplete,"invalid recapture does not replace a valid profile");
            shortArm.Reset();Check(!shortArm.IsComplete&&!shortArm.HasGuard,"reset clears guard and reach");
        }
        static void Next(){stage++;at=Time.time;}
        static void Setup()
        {
            var body=new GameObject("Test contact body").transform;var target=new GameObject("Test target").AddComponent<MechaVRBossTarget>();
            var contact=body.gameObject.AddComponent<MechaVRBossContact>();contact.boss=target;contact.arms=new MechaVRBossContact.Arm[2];
            for(int side=0;side<2;side++){
                var upper=new GameObject("Upper").transform;upper.SetParent(body,false);upper.localPosition=new Vector3(side==0?-.8f:.8f,1.6f,0);
                var lower=new GameObject("Lower").transform;lower.SetParent(upper,false);lower.localPosition=Vector3.forward*.95f;
                var hand=new GameObject("Hand").transform;hand.SetParent(lower,false);hand.localPosition=Vector3.forward*.95f;
                contact.arms[side]=new MechaVRBossContact.Arm{label=side==0?"L":"R",upperArm=upper,forearm=lower,hand=hand,volumes=Array.Empty<BoxCollider>()};
            }
            var go=new GameObject("Personal reach fixture");go.SetActive(false);go.AddComponent<NetworkObject>();session=go.AddComponent<RobotPlaySession>();session.contact=contact;session.target=target;session.robotRoot=body;
            pilot=go.AddComponent<RobotPilotInput>();pilot.session=session;pilot.source=PrototypeInput.XRLocalBoth;pilot.cockpitCamera=new GameObject("Test camera").AddComponent<Camera>();pilot.cockpitCamera.enabled=false;pilot.usePersonalCalibration=true;
            go.SetActive(true);EditorXRFixtureScope.Begin();InputSystem.RegisterLayout<ReachTestController>();head=InputSystem.AddDevice<XRHMD>();left=InputSystem.AddDevice<ReachTestController>();right=InputSystem.AddDevice<ReachTestController>();InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);
            EditorInputReplayScope.Begin();
        }
        static void Feed(ReachTestController d,bool leftSide,Vector3 position,Quaternion rotation,bool tracked,bool held,bool guard=false,bool reach=false,bool trigger=false,bool shoulder=false)
        {
            InputSystem.QueueStateEvent(d,new ReachTestState{position=position,rotation=rotation,isTracked=tracked?1:0,trackingState=tracked?3:0,gripPressed=held?1:0,secondaryButton=guard?1:0,primaryButton=reach?1:0,trigger=trigger?1:0,thumbstickClicked=shoulder?1:0});
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Active,false))return;
            if(SessionState.GetBool(Done,false))
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)return;string report=SessionState.GetString(Report,"");File.WriteAllText(Path.Combine(SessionState.GetString(Output,""),"reach-input.txt"),report);SessionState.SetBool(Active,false);
                if(SessionState.GetBool("PersonalReach.Interactive",false)){SessionState.SetBool("PersonalReach.Interactive",false);EditorSceneManager.OpenScene(SessionState.GetString("PersonalReach.RestoreScene",""));Debug.Log(report);}
                else EditorApplication.Exit(report.Contains("FAIL:")?1:0);return;
            }
            if(!EditorApplication.isPlaying||Time.frameCount<5)return;EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
            try{
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("input fixture timeout");
                if(stage==0){Setup();Next();}
                float age=Time.time-at;bool held=false,tracked=true,headTracked=true,guardPulse=false,leftReach=false,rightReach=false;
                Vector3 lp=new Vector3(-.25f,1.3f,.3f),rp=new Vector3(.25f,1.3f,.3f);Quaternion q=Quaternion.identity,hq=Quaternion.identity;
                if(stage==1&&age>.3f){Check(!pilot.ReachProfile(0).HasGuard&&!session.FreshInput(0),"first tracked pose cannot silently calibrate or attack");Next();}
                else if(stage==2){guardPulse=age<.06f;if(age>.3f){Check(pilot.ReachProfile(0).HasGuard&&pilot.ReachProfile(1).HasGuard&&!pilot.ReachProfile(0).IsComplete,"Y binding captures both guards explicitly");Next();}}
                else if(stage==3){lp=new Vector3(-.18f,1.45f,.65f);leftReach=age<.06f;if(age>.3f){Check(pilot.ReachProfile(0).IsComplete&&Mathf.Abs(pilot.ReachProfile(0).EffectiveReach-.65f)<.001f,"X binding captures effective left control-point reach");Next();}}
                else if(stage==4){rp=new Vector3(.18f,1.45f,.95f);rightReach=age<.06f;if(age>.3f){Check(pilot.ReachProfile(1).IsComplete&&Mathf.Abs(pilot.ReachProfile(1).EffectiveReach-.95f)<.001f,"A binding captures different right reach");Next();}}
                else if(stage==5&&age>.5f){Check(pilot.PersonalInputArmed&&session.Combat.arms[0].trajectory.Phase==MotionPhase.Ready,"release and return guard rearms personal input");guardElbow=session.contact.arms[0].forearm.position;guardHand=session.contact.arms[0].hand.position;Next();}
                else if(stage==6){held=true;lp+=Vector3.Lerp(Vector3.zero,new Vector3(-.12f,0,-.2f),Mathf.Clamp01(age/.35f));if(age>.55f){backHand=session.contact.arms[0].hand.position;var f=session.contact.arms[0].upperArm.GetComponent<ControllerArmFollower>();Check(f.PersonalReachActive&&f.Extension<.85f&&Vector3.Distance(guardHand,backHand)>.15f,"real input rearward pull stays bent with substantial travel");Next();}}
                else if(stage==7){held=true;lp+=Vector3.Lerp(new Vector3(-.12f,0,-.2f),new Vector3(.18f,.03f,.1f),Mathf.Clamp01(age/.35f));var f=session.contact.arms[0].upperArm.GetComponent<ControllerArmFollower>();maxExtension=Mathf.Max(maxExtension,f.Extension);if(age>.55f){Check(maxExtension<.92f&&Vector3.Distance(backHand,session.contact.arms[0].hand.position)>.3f,"actual input lateral sweep remains a bent continuous hook workspace");Next();}}
                else if(stage==8){held=true;q=Quaternion.Euler(0,0,-55);if(age>.9f){var f=session.contact.arms[0].upperArm.GetComponent<ControllerArmFollower>();Check(f.punchPose&&session.Combat.arms[0].attackHeld,"held alignment starts without requiring a classified attack");Check(session.contact.arms[0].forearm.position.y>guardElbow.y+.05f,"held rotation still guides elbow instead of becoming identity");Check(Vector3.Distance(session.contact.arms[0].hand.position,guardHand)<.003f&&Vector3.Angle(f.HandFrame*Vector3.forward,session.contact.arms[0].hand.position-session.contact.arms[0].forearm.position)<.5f,"held rigid wrist keeps requested position and forearm alignment");Next();}}
                else if(stage==9){q=Quaternion.Euler(0,0,-55);if(age>.5f){Check(!session.contact.arms[0].upperArm.GetComponent<ControllerArmFollower>().punchPose,"button release exits attack modifier");Next();}}
                else if(stage==10){tracked=false;if(age>.25f){Check(!session.FreshInput(0)&&pilot.ReachProfile(0).IsComplete,"hand loss cancels input while retaining measured workspace");Next();}}
                else if(stage==11){held=true;if(age>.3f){Check(!session.Combat.arms[0].attackHeld,"held reacquisition cannot bypass grip release");Next();}}
                else if(stage==12&&age>.4f){Check(pilot.PersonalInputArmed&&Mathf.Abs(pilot.ReachProfile(0).EffectiveReach-.65f)<.001f,"release rearms without inventing a new baseline or arm length");guardHand=session.contact.arms[0].hand.position;Next();}
                else if(stage==13){hq=Quaternion.Euler(0,90,0);if(age>.3f){Check(Vector3.Distance(guardHand,session.contact.arms[0].hand.position)<.003f,"looking sideways does not rotate body/shoulder workspace");Next();}}
                else if(stage==14){headTracked=false;if(age>.25f){Check(!session.FreshInput(0)&&!pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(1).HasGuard,"HMD loss invalidates both calibration frames and attack input");Next();}}
                else if(stage==15&&age>.3f){Check(!pilot.ReachProfile(0).IsComplete&&!session.FreshInput(0),"HMD recovery still requires explicit recalibration");Next();}
                else if(stage==16){rp=new Vector3(-.18f,1.45f,0);guardPulse=age>.2f&&age<.3f;if(age>.55f){Check(pilot.ReachProfile(0).HasGuard&&pilot.ReachProfile(0).Reference==ShoulderReference.AuxiliaryControlPoint,"optional auxiliary shoulder proxy is distinct from anatomical measurement");Next();}}
                else if(stage==17){lp=new Vector3(-.18f,1.45f,.65f);leftReach=age<.06f;if(age>.3f){Check(pilot.ReachProfile(0).IsComplete,"shoulder proxy can complete a new measured workspace");Next();}}
                else if(stage==18){guardPulse=age<.06f;if(age>.3f){Check(pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(0).IsComplete&&!session.contact.arms[0].upperArm.GetComponent<ControllerArmFollower>().PersonalReachActive,"new guard capture invalidates the old reach and solver profile");pilot.CaptureReach(0);session.ResetSimulation();Check(!pilot.ReachProfile(0).HasGuard&&!pilot.PersonalInputArmed,"restart clears calibration and rearming state");Finish();return;}}
                InputSystem.QueueDeltaStateEvent(head.GetChildControl<Vector3Control>("centerEyePosition"),new Vector3(0,1.65f,0));InputSystem.QueueDeltaStateEvent(head.GetChildControl<QuaternionControl>("centerEyeRotation"),hq);InputSystem.QueueDeltaStateEvent(head.GetChildControl<IntegerControl>("trackingState"),headTracked?3:0);
                Feed(left,true,lp,q,tracked,held,guardPulse,leftReach,false,stage==16&&age<.06f);Feed(right,false,rp,Quaternion.identity,true,false,false,rightReach);
            }catch(Exception e){lines.Add("FAIL: "+e);Finish();}
        }
        static void Finish(){if(head!=null&&head.added)InputSystem.RemoveDevice(head);if(left!=null&&left.added)InputSystem.RemoveDevice(left);if(right!=null&&right.added)InputSystem.RemoveDevice(right);EditorInputReplayScope.End();EditorXRFixtureScope.End();SessionState.SetString(Report,string.Join("\n",lines));SessionState.SetBool(Done,true);EditorApplication.isPlaying=false;}
    }
}
