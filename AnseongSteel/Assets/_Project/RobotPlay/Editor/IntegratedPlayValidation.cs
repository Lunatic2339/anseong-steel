using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using UnityEngine.UI;
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
    public struct IntegrationControllerState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('R','L','O','C');
        [InputControl(name="devicePosition",layout="Vector3")] public Vector3 position;
        [InputControl(name="deviceRotation",layout="Quaternion")] public Quaternion rotation;
        [InputControl(layout="Button",format="FLT")] public float isTracked;
        [InputControl(layout="Integer")] public int trackingState;
        [InputControl(layout="Button",format="FLT")] public float trigger;
        [InputControl(layout="Button",format="FLT")] public float gripPressed;
        [InputControl(layout="Vector2")] public Vector2 thumbstick;
        [InputControl(layout="Button",format="FLT")] public float secondaryButton;
    }
    [InputControlLayout(stateType=typeof(IntegrationControllerState))]
    public class IntegrationTestController : XRController { }
    [InitializeOnLoad]
    public static class IntegratedPlayValidation
    {
        const string Active="Robot.Integration.Active",Done="Robot.Integration.Done",Report="Robot.Integration.Report";
        static RobotPlaySession session; static RobotPilotInput input;
        static IntegrationTestController left,right; static InputSettings original,temporary;
        static int stage,frame=-1; static float at; static double began;
        static bool peakLogged;
        static IntegratedPlayFlow flow;static IntegratedBossEncounter boss;static int guardsBefore;static float bodyBefore;static int dodgeBefore,impactBefore,hpBefore;
        static readonly List<string> checks=new List<string>();
        static IntegratedPlayValidation(){EditorApplication.update+=Tick;began=EditorApplication.timeSinceStartup;}
        [MenuItem("Anseong Steel/Robot Play/Validate integrated two-controller play")]
        public static void Run()
        {
            RobotCombatChecks.Run();IntegratedRulesChecks.Run();
            IntegratedPlayBuilder.UpdateOwnedScene();
            SessionState.SetBool(Active,true);SessionState.SetBool(Done,false);EditorApplication.isPlaying=true;
        }
        public static void RunFocusedCombat(){SessionState.SetBool("Robot.Integration.Focused",true);Run();}
        static void Check(bool ok,string message)
        {if(!ok)throw new Exception(message+" "+(session?JsonUtility.ToJson(session.State):"no session"));checks.Add("PASS: "+message);}
        static void Next(){stage++;at=Time.time;}
        static void Feed(IntegrationTestController device,bool isLeft,Vector3 offset,bool tracked=true,bool boost=false,bool hold=true,Vector2 stick=default)
        {
            var baseline=new Vector3(isLeft?-.25f:.25f,1.3f,.3f);
            InputSystem.QueueStateEvent(device,new IntegrationControllerState{position=baseline+offset/input.motionScale,
                rotation=Quaternion.identity,isTracked=tracked?1:0,trackingState=tracked?3:0,trigger=boost?1:0,gripPressed=hold?1:0,thumbstick=stick});
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
                    File.WriteAllText("RobotPlayValidation/integration-checks.txt",result);SessionState.SetBool(Active,false);
                    Debug.Log(result);EditorApplication.Exit(result.Contains("FAIL:")?1:0);return;
                }
                if(EditorApplication.timeSinceStartup-began>300)throw new Exception("local validation timeout");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<15)return;
                EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
                float elapsed=Time.time-at;Vector3 l=Vector3.zero,r=Vector3.zero;bool tracked=true,boost=false,hold=true;Vector2 ls=Vector2.zero,rs=Vector2.zero;
                switch(stage)
                {
                    case 0:
                        session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();input=UnityEngine.Object.FindFirstObjectByType<RobotPilotInput>();
                        Check(session&&input&&session.contact.arms.Length==2,"existing gameplay scene and both arms loaded");
                        original=InputSystem.settings;temporary=UnityEngine.Object.Instantiate(original);temporary.hideFlags=HideFlags.DontSave;
                        temporary.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        temporary.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=temporary;
                        InputSystem.RegisterLayout<IntegrationTestController>();
                        left=InputSystem.AddDevice<IntegrationTestController>();right=InputSystem.AddDevice<IntegrationTestController>();
                        InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);
                        input.source=PrototypeInput.XRLocalBoth;session.GetComponent<IntegratedPlayFlow>().enabled=false;session.GetComponent<IntegratedBossEncounter>().Stop();session.externalBossTimeline=false;session.trainingWindow=true;session.ResetSimulation();
                        session.target.onHit.AddListener(()=>{int side=session.target.LastArm=="L"?0:1;var a=session.Combat.arms[side];checks.Add("CONTACT stage="+stage+" time="+(Time.time-at)+" part="+session.contact.arms[side].ContactPart+" phase="+a.trajectory.Phase+" candidate="+JsonUtility.ToJson(a.trajectory.Candidate)+" completed="+JsonUtility.ToJson(a.trajectory.LastCompleted));});Next();if(SessionState.GetBool("Robot.Integration.Focused",false)){SessionState.SetBool("Robot.Integration.Focused",false);flow=session.GetComponent<IntegratedPlayFlow>();boss=session.GetComponent<IntegratedBossEncounter>();session.externalBossTimeline=true;flow.enabled=true;flow.Retry();boss.telegraphSeconds=.1f;stage=15;at=Time.time;}break;
                    case 1:hold=false;if(elapsed>.45f)Next();break;
                    case 2:
                        l=RobotPilotInput.DesktopPose(0,elapsed,false);
                        if(elapsed>1.05f){Check(session.State.hits==1,"left XR controller jab damages exactly once");Next();}break;
                    case 3:
                        r=RobotPilotInput.DesktopPose(1,elapsed,false);
                        if(elapsed>1.05f){Check(session.State.hits==2&&session.State.combo==2,"right XR controller advances cooperative combo");Next();}break;
                    case 4:
                        l=RobotPilotInput.DesktopPose(0,elapsed,true);boost=false;
                        if(elapsed>1.05f){Check(session.State.hits==3&&session.State.bossHealth==124,"recognized left-controller hook reaches contact and damages");Check(session.GetComponent<IntegratedCombatHud>().CombatText.Contains("BOSS 124"),"HUD reads same authoritative damage snapshot");Check(session.GetComponent<IntegratedPunchCue>().AcceptedReactions==0,"final hook without successful boost has no launch reaction");Capture("integrated-combat.png");Next();}break;
                    case 5:
                        l=r=new Vector3(0,.65f,0);
                        if(elapsed>.4f){Check(session.State.leftGuard&&session.State.rightGuard,"two independent tracked hands can guard together");Next();}break;
                    case 6:
                        tracked=false;r=new Vector3(0,.65f,0);
                        if(elapsed>.4f){Check(!session.State.leftGuard&&session.State.rightGuard,"one hand tracking loss clears only its guard");session.ResetSimulation();Next();}break;
                    case 7:hold=false;if(elapsed>.5f)Next();break;
                    case 8:
                        r=RobotPilotInput.DesktopPose(1,elapsed,true);boost=elapsed>.4f&&elapsed<.6f;
                        if(elapsed>1.05f){Check(session.State.hits==1&&session.State.bossHealth==140,"standalone right-controller hook damages exactly once");Check(session.GetComponent<IntegratedPunchCue>().AcceptedReactions==0,"standalone hook trigger cannot launch boss or push arm");Next();}break;
                    case 9:
                        hold=false;l=RobotPilotInput.DesktopPose(0,elapsed,false);
                        if(elapsed>1.05f){Check(session.State.hits==1&&session.State.bossHealth==140,"real XR contact without grip cannot damage");
                            flow=session.GetComponent<IntegratedPlayFlow>();boss=session.GetComponent<IntegratedBossEncounter>();
                            session.externalBossTimeline=true;flow.enabled=true;flow.Retry();boss.telegraphSeconds=.1f;boss.normalDamage=35;Next();}break;
                    case 10:
                        if(elapsed>3)throw new Exception("both-controller ready pose did not start encounter");
                        if(flow.State==IntegratedPlayFlow.RunState.Battle){Check(true,"ready calibration starts actual encounter with one learning objective");Next();}break;
                    case 11:
                        if(elapsed>12)throw new Exception("animated boss impact timeout: impact="+boss.ImpactCount+" damage="+boss.DamageCount+" hitpoint="+boss.driver.hitPoint.position);
                        if(boss.ImpactCount>0){Check(boss.DamageCount>0&&session.State.robotHealth<100,"authored Punch impact event applies real incoming damage");
                            Check(session.GetComponent<IntegratedCombatHud>().Hud.Damage[AnseongSteel.Hud.SuitPart.RightArm]>0,"incoming attack updates documented right-arm test damage region");
                            Capture("integrated-live-damage.png");guardsBefore=boss.GuardCount;Next();}break;
                    case 12:
                        l=r=new Vector3(0,.65f,0);
                        if(elapsed>15)throw new Exception("animated boss guard timeout");
                        if(boss.GuardCount>guardsBefore){Check(true,"tracked two-arm guard blocks authored heavy attack");bodyBefore=session.robotRoot.position.x;dodgeBefore=boss.DodgeCount;impactBefore=boss.ImpactCount;hpBefore=session.State.robotHealth;Next();}break;
                    case 13:
                        ls=Vector2.right;
                        if(elapsed>.4f){Check(Mathf.Abs(session.robotRoot.position.x-bodyBefore)<.02f,"one real XR stick alone does not move body");Next();}break;
                    case 14:
                        ls=rs=Vector2.right;
                        if(elapsed>1.05f){Check(session.robotRoot.position.x>.5f,"two matching real XR sticks move body sideways");stage=27;at=Time.time;}break;
                    case 15:
                        hold=false;
                        if(elapsed>3)throw new Exception("retry calibration timeout");
                        if(flow.State==IntegratedPlayFlow.RunState.Battle){boss.Stop();session.trainingWindow=true;session.SetBossWindow(true,1,false);Next();}break;
                    case 16:
                        l=RobotPilotInput.DesktopPose(0,elapsed,false);
                        if(elapsed>1.05f){Check(flow.CurrentLesson==IntegratedPlayFlow.Lesson.Hook&&session.State.hits==1,"actual jab success advances only the jab lesson");Next();}break;
                    case 17:
                        r=RobotPilotInput.DesktopPose(1,elapsed,true);
                        if(elapsed>1.05f){Check(flow.CurrentLesson==IntegratedPlayFlow.Lesson.Defence&&session.State.hits==2,"actual hook success advances to defence lesson");boss.Begin(.2f);Next();}break;
                    case 18:
                        l=r=new Vector3(0,.65f,0);
                        if(elapsed>12)throw new Exception("defence learning impact timeout");
                        if(flow.CurrentLesson==IntegratedPlayFlow.Lesson.CooperativeCombo){Check(session.State.robotHealth==100,"real guard success advances defence without damage");boss.Stop();session.trainingWindow=true;session.SetBossWindow(true,1,false);stage=26;at=Time.time;}break;
                    case 19:
                        l=RobotPilotInput.DesktopPose(0,elapsed,false);
                        if(elapsed>1.05f){Check(session.State.combo==1,"first cooperative jab starts combo");peakLogged=false;Next();}break;
                    case 20:
                        r=RobotPilotInput.DesktopPose(1,elapsed,false);
                        if(elapsed>.21f&&!peakLogged){peakLogged=true;var a=session.Combat.arms[1];Debug.Log("RIGHT_JAB_PEAK phase="+a.trajectory.Phase+" consumed="+a.contactConsumed+" candidate="+JsonUtility.ToJson(a.trajectory.Candidate)+" hand="+session.contact.arms[1].hand.position+" blocked="+session.contact.arms[1].IsBlocked+" part="+session.contact.arms[1].ContactPart);}
                        if(elapsed>1.05f){Check(session.State.combo==2,"second cooperative right jab advances combo | "+JsonUtility.ToJson(session.Combat.arms[1].trajectory.LastCompleted));peakLogged=false;Next();}break;
                    case 21:
                        l=RobotPilotInput.DesktopPose(0,elapsed,true);boost=session.State.hits==5&&elapsed>.55f&&elapsed<.85f;
                        var cue=session.GetComponent<IntegratedPunchCue>();
                        if(elapsed>.68f&&cue.AcceptedReactions==1&&!peakLogged)
                        {
                            peakLogged=true;var arm=session.contact.arms[0];var bossTransform=boss.driver.animator.transform;
                            var bossPosition=bossTransform.localPosition;var bossRotation=bossTransform.localRotation;
                            var upper=arm.upperArm.localRotation;var lower=arm.forearm.localRotation;var wrist=arm.hand.localRotation;
                            var camera= input.cockpitCamera.transform;var eye=camera.position;var eyeRotation=camera.rotation;var before=session.State;
                            cue.showBoostReaction=false;int renders=cue.RenderApplications;Capture("integrated-boost-disabled.png");
                            Check(cue.RenderApplications==renders,"optional boost presentation can be disabled without changing success damage");
                            cue.showBoostReaction=true;Capture("integrated-boost.png");
                            Check(cue.RenderApplications>renders&&cue.MaxBossOffset>.1f&&cue.MaxArmPush>.001f,"successful confirmed boost renders existing boss launch and extra arm push");
                            Check(cue.MaxWristDeviation<.5f,"extra visual push keeps wrist longitudinally aligned with forearm");
                            Check(Vector3.Distance(bossPosition,bossTransform.localPosition)<.00001f&&Quaternion.Angle(bossRotation,bossTransform.localRotation)<.01f&&Quaternion.Angle(upper,arm.upperArm.localRotation)<.01f&&Quaternion.Angle(lower,arm.forearm.localRotation)<.01f&&Quaternion.Angle(wrist,arm.hand.localRotation)<.01f,"each render restores boss and all arm joints before physics resumes");
                            Check(before.Equals(session.State)&&Vector3.Distance(eye,camera.position)<.00001f&&Quaternion.Angle(eyeRotation,camera.rotation)<.01f,"boost render leaves authoritative snapshot and HMD pose unchanged");
                        }
                        if(elapsed>1.05f){Check(flow.CurrentLesson==IntegratedPlayFlow.Lesson.Complete&&session.State.hits==5,"actual alternating hit combo completes learning objective");
                            Check(session.State.bossHealth==76,"trigger after confirmed combo hook adds actual bonus damage");Check(peakLogged&&cue.AcceptedReactions==1,"repeated trigger samples cannot duplicate successful boost reaction");
                            flow.targetDurationSeconds=.01f;Check(flow.State==IntegratedPlayFlow.RunState.Battle&&session.State.bossHealth>0,"elapsed target time never causes automatic victory");session.tuning.jabDamage=160;Next();}break;
                    case 22:
                        r=RobotPilotInput.DesktopPose(1,elapsed,false);
                        if(elapsed>1.05f){Check(flow.State==IntegratedPlayFlow.RunState.Won&&session.State.bossHealth==0,"actual killing contact transitions to victory");
                            int hp=session.State.robotHealth;session.Combat.ReceiveAttack(1,false,100);Check(session.Combat.RobotHealth==hp,"victory prevents further incoming damage");
                            Capture("integrated-victory.png");session.tuning.jabDamage=8;flow.Retry();Next();}break;
                    case 23:
                        if(elapsed>3)throw new Exception("victory retry timeout");
                        if(flow.State==IntegratedPlayFlow.RunState.Battle){Check(session.State.robotHealth==100&&session.State.bossHealth==160&&session.State.hits==0&&session.State.combo==0&&session.State.body==Vector3.zero,"restart resets authoritative combat and body state");
                            Check(Enumerable.Range(0,6).All(i=>session.GetComponent<IntegratedCombatHud>().Hud.Damage[(AnseongSteel.Hud.SuitPart)i]==0),"restart clears accumulated incoming part damage");Check(session.GetComponent<IntegratedPunchCue>().AcceptedReactions==0&&!session.GetComponent<IntegratedPunchCue>().ReactionActive,"restart clears successful boost visual state");
                            session.Combat.ReceiveAttack(1,false,100);Next();}break;
                    case 24:
                        if(elapsed>.25f){Check(flow.State==IntegratedPlayFlow.RunState.Lost&&session.State.robotHealth==0,"controlled fatal combat damage transitions to defeat");
                            Check(!session.Combat.Hit(0,Time.time,true)&&session.Combat.BossHealth==160,"defeat prevents further outgoing damage");Capture("integrated-defeat.png");
                            var hud=session.GetComponent<IntegratedCombatHud>().Hud;hud.SetMenuOpen(true);
                            var button=hud.HelmetCanvas.GetComponentsInChildren<Text>(true).First(t=>t.text=="RESTART RUN").GetComponent<Button>();button.onClick.Invoke();Next();}break;
                    case 26:
                        if(elapsed>.6f){Check(session.Combat.arms[0].trajectory.Phase==AnseongSteel.PlayerMotion.MotionPhase.Ready&&session.Combat.arms[1].trajectory.Phase==AnseongSteel.PlayerMotion.MotionPhase.Ready,"returning both hands to neutral rearms after guard");stage=19;at=Time.time;}break;
                    case 27:
                        if(elapsed>12)throw new Exception("unguardable evade timeout");
                        if(boss.Kind==IntegratedBossEncounter.AttackKind.Unguardable&&boss.ImpactCount>impactBefore)
                        {Check(boss.DodgeCount>dodgeBefore&&session.State.robotHealth==hpBefore,"matching stick sidestep avoids real unguardable impact without damage");Capture("integrated-evasion.png");flow.Retry();stage=15;at=Time.time;}break;
                    case 25:
                        if(elapsed>.3f){Check(!flow.Terminal&&session.State.robotHealth==100&&session.State.bossHealth==160&&!session.GetComponent<IntegratedCombatHud>().Hud.MenuOpen,"settings restart returns to clean playable state");Complete();return;}break;
                }
                Feed(left,true,l,tracked,boost,hold,ls);Feed(right,false,r,true,stage==8&&boost,hold,rs);
            }
            catch(Exception ex){checks.Add("FAIL: "+ex);Complete();}
        }
        static void Capture(string name)
        {
            var bridge=session.GetComponent<IntegratedCombatHud>();
            Debug.Log("INTEGRATED_RENDER_TARGET renderers="+session.target.GetComponentsInChildren<Renderer>().Length);
            Debug.Log("INTEGRATED_CAMERA "+bridge.feed.exteriorCamera.transform.position+" rotation="+bridge.feed.exteriorCamera.transform.eulerAngles+" mask="+bridge.feed.exteriorCamera.cullingMask);
            var hud=bridge.Hud;var cam=input.cockpitCamera;
            float aspect=cam.aspect;cam.aspect=1.44f;hud.LayoutHelmet(1.44f);Canvas.ForceUpdateCanvases();
            foreach(var c in new[]{hud.Feed.exteriorCamera,hud.Feed.leftCamera,hud.Feed.rightCamera})
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(c,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=c.targetTexture});
            var rt=new RenderTexture(1440,1000,24);rt.Create();var old=RenderTexture.active;
            try{UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                RenderTexture.active=rt;var tex=new Texture2D(1440,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,1000),0,0);tex.Apply();
                Directory.CreateDirectory("RobotPlayValidation");File.WriteAllBytes("RobotPlayValidation/"+name,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
            finally{RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);cam.aspect=aspect;hud.LayoutHelmet(aspect);}
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