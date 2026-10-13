using System;using System.IO;using System.Linq;using System.Collections.Generic;
using UnityEditor;using UnityEngine;using UnityEngine.InputSystem;using UnityEngine.InputSystem.Utilities;using UnityEngine.UI;using UnityEngine.InputSystem.XR;using UnityEngine.InputSystem.Controls;
using AnseongSteel.PlayerMotion;
namespace AnseongSteel.RobotPlay.Editor
{
 [InitializeOnLoad]public static class PunchPhaseValidation
 {
  const string Active="Punch.Review.Active",Done="Punch.Review.Done",Report="Punch.Review.Report";
  static readonly List<string> checks=new List<string>();static RobotPlaySession s;static RobotPilotInput input;static IntegratedCockpitSelector selector;static IntegrationTestController left,right;static XRHMD head;static InputSettings original,temp;
  static int stage,frame=-1,lastVariant=-1;static float at;static double started;static float maxWrist;
  static PunchPhaseValidation(){EditorApplication.update+=Tick;started=EditorApplication.timeSinceStartup;}
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add("PASS: "+text);}
  [MenuItem("Anseong Steel/Robot Play/Validate punch tracking and cockpit comparison")]
  public static void Run(){Core();Directory.CreateDirectory("RobotPlayValidation/PunchReview");File.WriteAllLines("RobotPlayValidation/PunchReview/core-checks.txt",checks);SessionState.SetString("Punch.Review.Core",string.Join("\n",checks));IntegratedPlayBuilder.UpdateOwnedScene();SessionState.SetBool(Active,true);SessionState.SetBool(Done,false);EditorApplication.isPlaying=true;}
  public static void RunMotionCore()
  {
   var type=typeof(AnseongSteel.PlayerMotion.Editor.ArmMotionValidation);type.GetMethod("Core",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);
   var result=(List<string>)type.GetField("checks",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);Directory.CreateDirectory("RobotPlayValidation/PunchReview");File.WriteAllLines("RobotPlayValidation/PunchReview/motion-core-checks.txt",result);Debug.Log("PUNCH_MOTION_CORE_PASS "+result.Count);
  }
  static void Core()
  {
   checks.Clear();Check(IntegratedPunchCue.ReactionEnvelope(-1,.9f)==0&&IntegratedPunchCue.ReactionEnvelope(0,.9f)==0&&IntegratedPunchCue.ReactionEnvelope(.9f,.9f)==0,"boost reaction begins and ends at unchanged visual pose");
   Check(Mathf.Abs(IntegratedPunchCue.ReactionEnvelope(.18f,.9f)-1)<.001f&&IntegratedPunchCue.ReactionEnvelope(.5f,.9f)>0,"boost reaction has a bounded rise and recovery");
   Check(IntegratedPunchCue.ReactionEnvelope(.1f,0)==0,"disabled duration cannot produce a boost reaction");Check(!RobotPilotInput.ValidRotation(new Quaternion(0,0,0,0))&&!RobotPilotInput.ValidRotation(new Quaternion(float.NaN,0,0,1)),"zero and nonfinite tracking rotations cannot enter calibration");Check(RobotPilotInput.ValidRotation(Quaternion.Euler(30,90,45)),"valid orientation remains accepted independently of headset yaw");var q=new PunchHapticQueue();q.Queue(0,FeedbackKind.Hit,0);q.Queue(0,FeedbackKind.Combo,0);q.Queue(0,FeedbackKind.Damage,0);
   Check(q.Take(0,0,out var pulse)&&pulse.kind==FeedbackKind.Damage,"same-frame events coalesce to one strongest pulse");q.Queue(0,FeedbackKind.Guard,.01f);Check(!q.Take(0,.01f,out pulse)&&q.Take(0,.1f,out pulse)&&pulse.kind==FeedbackKind.Guard,"haptic interval defers distinct feedback instead of double impulse");
   q.Reset();q.Queue(0,FeedbackKind.Hit,0);q.Take(0,0,out pulse);q.Queue(0,FeedbackKind.Hit,.02f);Check(!q.Take(0,.2f,out pulse),"duplicate hit feedback within interval is suppressed");
   q.Queue(1,FeedbackKind.Combo,.2f);q.Reset();Check(!q.Take(1,.3f,out pulse),"restart clears pending haptic pulses");
   Check(new[]{FeedbackKind.Hit,FeedbackKind.Guard,FeedbackKind.Damage,FeedbackKind.Combo,FeedbackKind.Boost}.Select(k=>PunchHapticQueue.Pattern(k).amplitude).Distinct().Count()==5,"hit guard damage cooperative success and boost have distinct configurable feedback");
   float t=0;var c=new RobotCombat(new CombatTuning{requiresAttackButton=true,strictComboRules=true,postContactBoost=true,standaloneHookDamage=true,wrongKindEndsCombo=false,comboSeconds=10});
   Stroke(c,0,false,ref t);Stroke(c,1,true,ref t);Check(c.ComboStep==1,"expected pilot wrong-kind handling is a separate provisional preserve policy");
   t=0;c=new RobotCombat(new CombatTuning{requiresAttackButton=true,strictComboRules=true,standaloneHookDamage=true,wrongKindEndsCombo=false,comboSeconds=10});Stroke(c,0,false,ref t);Stroke(c,0,true,ref t);Check(c.ComboStep==0,"wrong pilot order ends combo regardless of provisional wrong-kind policy");
  }
  static void Stroke(RobotCombat c,int side,bool hook,ref float t){for(int i=0;i<50;i++){t+=.02f;c.Sample(side,Vector3.zero,false,t,true);}for(int i=1;i<=24;i++){t+=.02f;c.Sample(side,hook?RobotPilotInput.DesktopPose(side,i*.02f,true):new Vector3(0,0,.06f*Mathf.Min(i,10)),false,t,true);if(!hook&&i==10)break;}c.Hit(side,t,true);}
  static void Feed(IntegrationTestController d,bool l,Vector3 offset,bool tracked,bool grip,Quaternion rotation){InputSystem.QueueStateEvent(d,new IntegrationControllerState{position=new Vector3(l?-.25f:.25f,1.3f,.3f)+offset/input.motionScale,rotation=rotation,isTracked=tracked?1:0,trackingState=l&&stage==2?2:l&&stage==12?1:tracked?3:0,gripPressed=grip?1:0});}
  static void Next(){stage++;at=Time.time;}
  static void Tick()
  {
   if(!SessionState.GetBool(Active,false))return;
   try{
    if(SessionState.GetBool(Done,false)){if(EditorApplication.isPlayingOrWillChangePlaymode)return;Directory.CreateDirectory("RobotPlayValidation/PunchReview");var report=SessionState.GetString(Report,"");File.WriteAllText("RobotPlayValidation/PunchReview/punch-checks.txt",report);Debug.Log(report);SessionState.SetBool(Active,false);EditorApplication.Exit(report.Contains("FAIL:")?1:0);return;}
    if(EditorApplication.timeSinceStartup-started>240)throw new Exception("punch review timeout");if(!EditorApplication.isPlaying||Time.frameCount<15)return;EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
    float age=Time.time-at;Vector3 pose=Vector3.zero;bool tracked=true,grip=true;
    if(stage==0){checks.AddRange(SessionState.GetString("Punch.Review.Core","").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries));s=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();input=s.GetComponent<RobotPilotInput>();selector=s.GetComponent<IntegratedCockpitSelector>();s.GetComponent<IntegratedPlayFlow>().enabled=false;s.GetComponent<IntegratedBossEncounter>().Stop();s.trainingWindow=true;s.ResetSimulation();input.source=PrototypeInput.XRLocalBoth;original=InputSystem.settings;temp=UnityEngine.Object.Instantiate(original);temp.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;temp.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=temp;head=InputSystem.AddDevice<XRHMD>();InputSystem.RegisterLayout<IntegrationTestController>();left=InputSystem.AddDevice<IntegrationTestController>();right=InputSystem.AddDevice<IntegrationTestController>();InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);Next();}
    else if(stage==1&&age>.6f)Next();
    else if(stage==2){tracked=true;if(age>.15f){Check(!s.FreshInput(0)&&s.State.hits==0,"missing position flag clears input immediately even if overall tracking is true");Next();}}
    else if(stage==3){if(age>.15f)pose=RobotPilotInput.DesktopPose(0,age-.15f,false);if(age>1.25f){Check(s.State.hits==0,"reacquisition with grip held cannot create ghost punch");Next();}}
    else if(stage==4){grip=false;if(age>.55f)Next();}
    else if(stage==5){pose=RobotPilotInput.DesktopPose(0,age,false);if(age>1.1f){Check(s.State.hits==1,"explicit release and new grip allows a genuine tracked jab despite head yaw");Check(maxWrist<.5f,"tracked jab wrist remains longitudinally aligned to forearm under rotation noise");maxWrist=0;Next();}}
    else if(stage==6){pose=RobotPilotInput.DesktopPose(0,age,true);if(age>1.2f){Check(s.State.hits==2,"tracked noisy rotation does not reject a genuine hook contact");Check(maxWrist<.5f,"hook and return keep virtual wrist rigid with forearm");Next();}}
    else if(stage>=7&&stage<12){int index=stage-7;if(lastVariant!=index){lastVariant=index;Check(selector.Select(index),"existing cockpit loads with display and authored pilot anchors: "+selector.variants[index].name);}if(age>.3f){var camera=input.cockpitCamera;var body=s.State.body;int hp=s.State.bossHealth,hits=s.State.hits;selector.View(false);Vector3 lp=camera.transform.position;Capture("cockpit-"+index+"-left.png");selector.View(true);Vector3 rp=camera.transform.position;Capture("cockpit-"+index+"-right.png");Check(Vector3.Distance(lp,rp)>2&&Mathf.Abs(lp.y-rp.y)<.01f,"distinct symmetric authored left/right viewpoints: "+selector.CurrentName);Check(s.State.bossHealth==hp&&s.State.hits==hits&&s.State.body==body,"cockpit/view switch preserves authoritative combat: "+selector.CurrentName);Check(selector.feed.screen.enabled&&selector.feed.exteriorCamera.targetTexture==selector.feed.centerFeed&&s.GetComponent<IntegratedCombatHud>().Hud.HelmetCanvas.gameObject.activeInHierarchy,"panorama display and unchanged helmet HUD remain connected: "+selector.CurrentName);Next();}}
    else if(stage==12){if(age>.15f){Check(!s.FreshInput(0)&&s.State.hits==2,"rotation tracking flag loss invalidates the arm immediately");Next();}}
    else if(stage==13){if(age>.15f)pose=RobotPilotInput.DesktopPose(0,age-.15f,false);if(age>1.25f){Check(s.State.hits==2,"rotation tracking reacquisition with grip held cannot create ghost punch");Next();}}
    else if(stage==14){grip=false;if(age>.55f)Next();}
    else if(stage==15){pose=RobotPilotInput.DesktopPose(0,age,false);if(age>1.1f){Check(s.State.hits==3,"rotation flag recovery accepts fresh punch only after release");input.leftRotation.Disable();Next();}}
    else if(stage==16){if(age>.15f){Check(!s.FreshInput(0)&&s.State.hits==3,"missing rotation action cannot substitute identity and keep attacking");input.leftRotation.Enable();Next();}}
    else if(stage==17){if(age>.15f)pose=RobotPilotInput.DesktopPose(0,age-.15f,false);if(age>1.25f){Check(s.State.hits==3,"rotation control reacquisition with held grip cannot create ghost punch");Next();}}
    else if(stage==18){grip=false;if(age>.55f)Next();}
    else if(stage==19){pose=RobotPilotInput.DesktopPose(0,age,false);if(age>1.1f){Check(s.State.hits==4,"rotation control recovery accepts fresh punch after release");input.leftTrackingState.Disable();Next();}}
    else if(stage==20){if(age>.15f){Check(!s.FreshInput(0)&&s.State.hits==4,"missing tracking-state control is not assumed fully tracked");input.leftTrackingState.Enable();Next();}}
    else if(stage==21){if(age>.15f)pose=RobotPilotInput.DesktopPose(0,age-.15f,false);if(age>1.25f){Check(s.State.hits==4,"tracking-state control recovery with held grip cannot create ghost punch");Next();}}
    else if(stage==22){grip=false;if(age>.55f)Next();}
    else if(stage==23){pose=RobotPilotInput.DesktopPose(0,age,false);if(age>1.1f){Check(s.State.hits==5,"tracking-state recovery accepts fresh punch after release");Next();}}
    else if(stage==24){Complete();return;}
    foreach(var follower in s.robotRoot.GetComponentsInChildren<ControllerArmFollower>())if(follower.IsReady)maxWrist=Mathf.Max(maxWrist,Vector3.Angle((follower.hand.position-follower.forearm.position).normalized,follower.HandFrame*Vector3.forward));
    InputSystem.QueueDeltaStateEvent(head.GetChildControl<QuaternionControl>("centerEyeRotation"),Quaternion.Euler(0,stage==5?90:0,0));InputSystem.QueueDeltaStateEvent(head.GetChildControl<Vector3Control>("centerEyePosition"),Vector3.zero);
    Feed(left,true,pose,tracked,grip,Quaternion.Euler(Mathf.Sin(Time.time*23)*35,Mathf.Cos(Time.time*17)*25,Mathf.Sin(Time.time*31)*45));Feed(right,false,Vector3.zero,true,true,Quaternion.identity);
   }catch(Exception ex){checks.Add("FAIL: "+ex);Complete();}
  }
  static void Capture(string name)
  {
   Directory.CreateDirectory("RobotPlayValidation/PunchReview");var bridge=s.GetComponent<IntegratedCombatHud>();var cam=input.cockpitCamera;var hud=bridge.Hud;float aspect=cam.aspect;cam.aspect=1.44f;hud.LayoutHelmet(1.44f);Canvas.ForceUpdateCanvases();
   foreach(var c in new[]{hud.Feed.exteriorCamera,hud.Feed.leftCamera,hud.Feed.rightCamera})UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(c,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=c.targetTexture});
   var rt=new RenderTexture(1440,1000,24);rt.Create();var old=RenderTexture.active;
   try{UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;var tex=new Texture2D(1440,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,1000),0,0);tex.Apply();File.WriteAllBytes("RobotPlayValidation/PunchReview/"+name,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}finally{RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);cam.aspect=aspect;hud.LayoutHelmet(aspect);}
  }
  static void Complete(){if(head!=null&&head.added)InputSystem.RemoveDevice(head);if(left!=null&&left.added)InputSystem.RemoveDevice(left);if(right!=null&&right.added)InputSystem.RemoveDevice(right);if(original)InputSystem.settings=original;if(temp)UnityEngine.Object.DestroyImmediate(temp);SessionState.SetString(Report,string.Join("\n",checks));SessionState.SetBool(Done,true);EditorApplication.isPlaying=false;}
 }
}
