using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Utilities;
using AnseongSteel.PlayerMotion;

namespace AnseongSteel.RobotPlay.Editor
{
    // Prevent connected native XR poses from winning generic pose bindings during synthetic verification.
    // Devices and their prior enabled state are retained, and restored before Play exit.
    internal static class EditorXRFixtureScope
    {
        static readonly List<InputDevice> suspended=new List<InputDevice>();
        static bool started;
        public static int SuspendedCount=>suspended.Count;
        public static void Begin()
        {
            if(started)throw new InvalidOperationException("An XR fixture scope is already active.");
            started=true;suspended.Clear();
            foreach(var d in InputSystem.devices.ToArray())if(d.enabled&&(d is XRHMD||d is XRController))
            {suspended.Add(d);InputSystem.DisableDevice(d);}
        }
        public static void End()
        {
            if(!started)return;
            foreach(var d in suspended)if(d.added)InputSystem.EnableDevice(d);
            if(suspended.Any(d=>d.added&&!d.enabled))throw new InvalidOperationException("Original XR device enabled state was not restored.");
            suspended.Clear();started=false;
        }
    }
    // Use the installed package's replay focus scope, not a replacement settings
    // asset. Reflection is isolated here because the package exposes it internally.
    // Queued events still go through normal player InputSystem and MonoBehaviour updates.
    internal static class EditorInputReplayScope
    {
        static bool started;static int prior;
        static readonly Type manager=typeof(InputSystem).Assembly.GetType("UnityEngine.InputSystem.InputManager");
        static object Instance=>typeof(InputSystem).GetField("s_Manager",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        static int Count=>(int)manager.GetField("m_EditorEventPassthroughCount",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Instance);
        public static void Begin(){if(started)throw new InvalidOperationException("A synthetic input replay is already active.");prior=Count;manager.GetMethod("StartEditorEventPassthrough",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);started=true;}
        public static void End(){if(!started)return;manager.GetMethod("StopEditorEventPassthrough",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);started=false;if(Count!=prior)throw new InvalidOperationException("Input replay focus counter was not restored.");}
    }
    // The authored scene is only opened and played; no builder, repair or save is called.
    [InitializeOnLoad]
    public static class SavedSceneReachValidation
    {
        const string Prefix="SavedSceneReach.",ScenePath="Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity";
        static readonly List<string> lines=new List<string>(),samples=new List<string>();
        static RobotPlaySession session;static RobotPilotInput pilot;static IntegratedPlayFlow flow;
        static RobotRigVisual rig;static SkinnedMeshRenderer[] skins;
        static XRHMD head;static ReachTestController left,right;static Keyboard keyboard;
        static Vector3[][] guardSkin;static Vector3[] guardHands,guardElbows;
        static int stage,frame=-1,hitsBefore,combatBefore;static float began;static double deadline;
        static bool contactSeen,keyboardOwned;static Key[] recoveryKeys;
        static SavedSceneReachValidation(){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.playModeStateChanged+=PlayChanged;}
        static void PlayChanged(PlayModeStateChange change)
        {
            if(change==PlayModeStateChange.ExitingPlayMode&&SessionState.GetBool(Prefix+"Active",false)&&!SessionState.GetBool(Prefix+"Done",false))
            {lines.Add("FAIL: Play was stopped before the verification finished; fixture cleanup executed.");Finish();}
        }
        static string Out=>SessionState.GetString(Prefix+"Output","");
        static string Hash(){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ScenePath)));}
        static void Log(string message,string stack,LogType type)
        {
            if(SessionState.GetBool(Prefix+"Active",false)&&(type==LogType.Assert||type==LogType.Exception))
                SessionState.SetString(Prefix+"Diagnostics",SessionState.GetString(Prefix+"Diagnostics","")+type+": "+message+"\n"+stack+"\n");
        }
        static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);lines.Add("PASS: "+name);}
        [MenuItem("Anseong Steel/Player Motion/Verify saved scene XR reach and keyboard")]
        public static void Interactive()=>Begin(false,false);
        public static void Run()=>Begin(true,false);
        public static void Baseline()=>Begin(true,true);
        static void Begin(bool batch,bool baseline)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene changes are preserved; finish them before testing.");
            string restore=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if(!batch&&UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new InvalidOperationException("Keep a single saved scene open; multiple-scene workspace is preserved.");
            if(!batch&&string.IsNullOrEmpty(restore))throw new InvalidOperationException("An interactive test requires a saved starting scene.");
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--reach-output");
            string output=index>=0?args[index+1]:Path.GetFullPath("../Audit/2026-10-14/evidence/18_tracking/followup/main");
            Directory.CreateDirectory(output);SessionState.SetString(Prefix+"Output",output);
            SessionState.SetString(Prefix+"Restore",restore);SessionState.SetBool(Prefix+"Batch",batch);SessionState.SetBool(Prefix+"Baseline",baseline);
            SessionState.SetString(Prefix+"Settings",JsonUtility.ToJson(InputSystem.settings));
            SessionState.SetString(Prefix+"SettingsPath",AssetDatabase.GetAssetPath(InputSystem.settings));
            SessionState.SetInt(Prefix+"Keyboard",Keyboard.current?.deviceId??-1);
            SessionState.SetString(Prefix+"Hash",Hash());SessionState.SetBool(Prefix+"Active",true);
            SessionState.SetBool(Prefix+"Done",false);SessionState.SetBool(Prefix+"Recovery",false);SessionState.SetString(Prefix+"Diagnostics","");
            if(baseline)EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);else EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying=true;
        }
        static void Setup()
        {
            lines.Clear();samples.Clear();samples.Add("stage,side,requestedX,requestedY,requestedZ,handX,handY,handZ,elbowX,elbowY,elbowZ,extension,visualError,blocked,part,queries,health,hits");
            deadline=EditorApplication.timeSinceStartup+150;stage=0;frame=-1;began=Time.time;
            if(SessionState.GetBool(Prefix+"Baseline",false))return;
            session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();pilot=session.GetComponent<RobotPilotInput>();flow=session.GetComponent<IntegratedPlayFlow>();
            rig=session.robotRoot.GetComponentsInChildren<RobotRigVisual>(true).Single(r=>r.gameObject.name==IntegratedArmRepairBuilder.VisualName);
            Check(session.enabled&&pilot.enabled&&flow.enabled&&rig.enabled,"saved scene runtime input/session/flow/visual components remain enabled");
            Check(pilot.source==PrototypeInput.XRLocalBoth&&pilot.RequiresPersonalCalibration&&!session.NetworkActive,"saved source is local personal XR");
            Check(session.contact.arms.All(a=>a.volumes.Length>0&&a.volumes.All(v=>v))&&session.target.ContactColliders.Length>0,"authored contact and target volumes present");
            flow.openingGraceSeconds=1000; // Deterministic test window, runtime instance only.
            skins=rig.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled&&s.gameObject.activeInHierarchy).ToArray();
            Check(skins.Length==2&&skins.All(s=>s.sharedMesh&&s.bones.All(b=>b)),"both authored v026 skins have live bones");
            EditorXRFixtureScope.Begin();InputSystem.RegisterLayout<ReachTestController>();head=InputSystem.AddDevice<XRHMD>();left=InputSystem.AddDevice<ReachTestController>();right=InputSystem.AddDevice<ReachTestController>();
            InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);keyboard=InputSystem.AddDevice<Keyboard>();keyboardOwned=true;
            SessionState.SetString(Prefix+"FixtureIds",string.Join(",",head.deviceId,left.deviceId,right.deviceId,keyboard.deviceId));
            EditorInputReplayScope.Begin();
            lines.Add("SETTINGS during fixture="+JsonUtility.ToJson(InputSystem.settings));
            foreach(var collider in session.target.ContactColliders)lines.Add("TARGET "+collider.name+" center="+collider.bounds.center+" size="+collider.bounds.size);
            // Separate unobstructed workspace from contact. Move only this Play
            // instance of the authored target, then bring its actual collider back
            // for the contact stage; no collider is replaced or disabled.
            session.target.transform.position+=Vector3.forward*4;Physics.SyncTransforms();session.contact.ResetContactState();
            lines.Add("FIXTURE authored target temporarily parked 4m forward for unobstructed reach checks; all authored volumes retained.");
        }
        static void Next(){stage++;began=Time.time;}
        static ControllerArmFollower F(int side)=>session.contact.arms[side].upperArm.GetComponent<ControllerArmFollower>();
        static Vector3[] Bake(SkinnedMeshRenderer skin)
        {
            var mesh=new Mesh();try{skin.BakeMesh(mesh,true);return mesh.vertices.Select(v=>skin.transform.TransformPoint(v)).ToArray();}finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        static void Observe(string label,bool deformation=false)
        {
            for(int side=0;side<2;side++)
            {
                var a=session.contact.arms[side];var b=rig.arms[side];var f=F(side);
                float error=Mathf.Max(Vector3.Distance(b.upper.position,a.upperArm.position),Vector3.Distance(b.lower.position,a.forearm.position),Vector3.Distance(b.hand.position,a.hand.position));
                Check(error<.003f,label+" side "+side+" visual shoulder/elbow/hand matches accepted contact pose");
                Check(Vector3.Angle(b.hand.position-b.lower.position,b.lower.TransformDirection(b.lowerAxis))<1,label+" side "+side+" visual forearm axis follows contact");
                var fist=b.hand.GetComponent<DefaultFistPose>();
                Check(Vector3.Angle(b.hand.TransformDirection(fist.palmForwardLocal),b.hand.position-b.lower.position)<1&&fist.joints.Select((j,i)=>Quaternion.Angle(j.localRotation,fist.closedRotations[i])).Max()<.1f,label+" side "+side+" wrist straight and default fingers closed");
                samples.Add(string.Join(",",label,side,f.RequestedTarget.x,f.RequestedTarget.y,f.RequestedTarget.z,a.hand.position.x,a.hand.position.y,a.hand.position.z,a.forearm.position.x,a.forearm.position.y,a.forearm.position.z,f.Extension,error,a.IsBlocked,a.ContactPart,session.contact.QueriesLastFrame,session.Combat.BossHealth,session.Combat.HitCount));
            }
            for(int i=0;i<skins.Length;i++)
            {
                var points=Bake(skins[i]);Check(points.All(PunchTrajectoryRecognizer.Finite),label+" finite deformed skin "+skins[i].name);
                if(deformation)Check(points.Select((p,j)=>Vector3.Distance(p,guardSkin[i][j])).Max()>.03f,label+" actual skin vertices move from guard "+skins[i].name);
            }
        }
        static void Feed(Vector3 lp,Vector3 rp,Quaternion lq,Quaternion rq,bool held,bool guard,bool lreach,bool rreach,bool tracked,bool headTracked,params Key[] keys)
        {
            InputSystem.QueueDeltaStateEvent(head.GetChildControl<Vector3Control>("centerEyePosition"),new Vector3(0,1.65f,0));
            InputSystem.QueueDeltaStateEvent(head.GetChildControl<QuaternionControl>("centerEyeRotation"),Quaternion.identity);
            InputSystem.QueueDeltaStateEvent(head.GetChildControl<IntegerControl>("trackingState"),headTracked?3:0);
            InputSystem.QueueStateEvent(left,new ReachTestState{position=lp,rotation=lq,isTracked=tracked?1:0,trackingState=tracked?3:0,gripPressed=held?1:0,secondaryButton=guard?1:0,primaryButton=lreach?1:0});
            InputSystem.QueueStateEvent(right,new ReachTestState{position=rp,rotation=rq,isTracked=1,trackingState=3,gripPressed=held?1:0,primaryButton=rreach?1:0});
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Prefix+"Active",false))return;
            try
            {
                if(SessionState.GetBool(Prefix+"Done",false))
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    lines.Clear();lines.AddRange(SessionState.GetString(Prefix+"Lines","").Split('\n'));
                    Check(InputSystem.settings&&JsonUtility.ToJson(InputSystem.settings)==SessionState.GetString(Prefix+"Settings",""),"Play exit preserves InputSystem settings values");
                    Check(AssetDatabase.GetAssetPath(InputSystem.settings)==SessionState.GetString(Prefix+"SettingsPath",""),"Play exit preserves settings asset identity/path");
                    var fixtureIds=SessionState.GetString(Prefix+"FixtureIds","").Split(',').Where(s=>!string.IsNullOrEmpty(s)).Select(int.Parse).ToArray();
                    Check(!InputSystem.devices.Any(d=>d is ReachTestController||fixtureIds.Contains(d.deviceId)),"all fixture XR and keyboard devices removed after Play");
                    int oldKeyboard=SessionState.GetInt(Prefix+"Keyboard",-1);
                    Check(oldKeyboard<0||Keyboard.current!=null&&Keyboard.current.deviceId==oldKeyboard,"original current keyboard restored after Play");
                    Check(Hash()==SessionState.GetString(Prefix+"Hash",""),"saved integrated scene bytes unchanged");
                    if(!SessionState.GetBool(Prefix+"Baseline",false)&&!SessionState.GetBool(Prefix+"Recovery",false)&&!lines.Any(l=>l.StartsWith("FAIL:")))
                    {
                        SessionState.SetString(Prefix+"Lines",string.Join("\n",lines));SessionState.SetBool(Prefix+"Recovery",true);SessionState.SetBool(Prefix+"Done",false);
                        EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;return;
                    }
                    string diagnostics=SessionState.GetString(Prefix+"Diagnostics","");
                    lines.Add("ASSERT/EXCEPTION DIAGNOSTICS recorded separately; characters="+diagnostics.Length);
                    File.WriteAllText(Path.Combine(Out,"diagnostics.txt"),diagnostics);
                    Check(!diagnostics.Contains("Assert:"),"no Assertions during fixture or subsequent Play lifecycle");
                    File.WriteAllLines(Path.Combine(Out,"saved-scene-reach.txt"),lines);
                    SessionState.SetBool(Prefix+"Active",false);
                    string restore=SessionState.GetString(Prefix+"Restore","");if(!string.IsNullOrEmpty(restore))EditorSceneManager.OpenScene(restore);
                    if(SessionState.GetBool(Prefix+"Batch",false))EditorApplication.Exit(lines.Any(l=>l.StartsWith("FAIL:"))?1:0);else Debug.Log(string.Join("\n",lines));return;
                }
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<15)return;
                EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
                if(SessionState.GetBool(Prefix+"Recovery",false)){RecoveryTick();return;}
                if(!session&&!SessionState.GetBool(Prefix+"Recovery",false)&&stage==0){Setup();Next();}
                if(SessionState.GetBool(Prefix+"Baseline",false)){if(Time.frameCount>40){lines.Add("BASELINE empty scene Play: no synthetic devices or settings writes");Finish();}return;}
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("saved scene XR fixture timed out");
                float age=Time.time-began;var lp=new Vector3(-.25f,1.3f,.3f);var rp=new Vector3(.25f,1.3f,.3f);var lq=Quaternion.identity;var rq=Quaternion.identity;
                if(stage==6&&age>.15f&&Environment.GetCommandLineArgs().Contains("--reach-stop-early")){EditorApplication.isPlaying=false;return;}
                bool held=false,guard=false,lreach=false,rreach=false,tracked=true,headTracked=true;Key[] keys=Array.Empty<Key>();
                if(stage==1&&age>.4f){Check(!pilot.ReachProfile(0).HasGuard&&flow.State==IntegratedPlayFlow.RunState.Calibrating,"tracked pose alone cannot bypass saved scene personal gate");Next();}
                else if(stage==2){guard=age<.12f;if(age>.45f){Check(pilot.ReachProfile(0).HasGuard&&pilot.ReachProfile(1).HasGuard,"Y events capture both saved scene guards");Next();}}
                else if(stage==3){lp=new Vector3(-.18f,1.45f,.65f);lreach=age<.12f;if(age>.45f){Check(pilot.ReachProfile(0).IsComplete,"X completes saved scene left effective reach");Next();}}
                else if(stage==4){rp=new Vector3(.18f,1.45f,.95f);rreach=age<.12f;if(age>.45f){Check(pilot.ReachProfile(1).IsComplete,"A completes saved scene right effective reach");Next();}}
                else if(stage==5&&(flow.State==IntegratedPlayFlow.RunState.Battle||age>3)){Check(pilot.PersonalInputArmed&&flow.State==IntegratedPlayFlow.RunState.Battle,"both profiles and release open actual scene battle gate; armed="+pilot.PersonalInputArmed+" phases="+session.Combat.arms[0].trajectory.Phase+"/"+session.Combat.arms[1].trajectory.Phase+" flow="+flow.State+" objective="+flow.Objective);guardHands=session.contact.arms.Select(a=>a.hand.position).ToArray();guardElbows=session.contact.arms.Select(a=>a.forearm.position).ToArray();guardSkin=skins.Select(Bake).ToArray();Observe("guard");Next();}
                else if(stage==6){held=true;lp+=new Vector3(-.12f,0,-.2f)*Mathf.Clamp01(age/.4f);rp+=new Vector3(.12f,0,-.2f)*Mathf.Clamp01(age/.4f);if(age>.75f){Check(Enumerable.Range(0,2).All(i=>F(i).Extension<.85f&&Vector3.Distance(guardHands[i],session.contact.arms[i].hand.position)>.1f),"both rear pulls retain travel and bent elbows");Observe("rear",true);Next();}}
                else if(stage==7){held=true;lp+=Vector3.Lerp(new Vector3(-.12f,0,-.2f),new Vector3(.18f,.03f,.1f),Mathf.Clamp01(age/.4f));rp+=Vector3.Lerp(new Vector3(.12f,0,-.2f),new Vector3(-.18f,.03f,.1f),Mathf.Clamp01(age/.4f));if(age>.75f){Check(F(0).Extension<.92f&&F(1).Extension<.92f,"close left/right hooks avoid full extension");Observe("hooks",true);Next();}}
                else if(stage==8){held=true;lp=new Vector3(-.18f,1.45f,.65f);rp=new Vector3(.18f,1.45f,.95f);if(age>1.2f){Observe("maxima",true);Check(Enumerable.Range(0,2).All(i=>F(i).Extension>.99f&&F(i).Extension<1f&&!session.contact.arms[i].IsBlocked),"measured maxima nearly straighten actual contact limbs without overshooting length; extensions="+F(0).Extension+"/"+F(1).Extension+" blocked="+session.contact.arms[0].ContactPart+"/"+session.contact.arms[1].ContactPart);Next();}}
                else if(stage==9){held=true;lq=Quaternion.Euler(0,0,-55);rq=Quaternion.Euler(0,0,55);if(age>1.2f){Check(Enumerable.Range(0,2).All(i=>F(i).punchPose&&session.Combat.arms[i].attackHeld&&Vector3.Distance(session.contact.arms[i].hand.position,guardHands[i])<.01f&&Vector3.Distance(session.contact.arms[i].forearm.position,guardElbows[i])>.05f),"held tracked rotations move elbows while preserving both guard hand points");Observe("held-rotation");Next();}}
                else if(stage==10){tracked=false;held=true;if(age>.45f){Check(!session.FreshInput(0)&&!session.Combat.arms[0].attackHeld&&pilot.ReachProfile(0).IsComplete,"saved scene hand loss clears attack but retains profile");Observe("hand-loss");Next();}}
                else if(stage==11){held=true;if(age>.4f){Check(!session.Combat.arms[0].attackHeld,"held reacquisition remains blocked in saved scene");Next();}}
                else if(stage==12&&age>.5f){Check(pilot.PersonalInputArmed,"release restores saved scene input");Next();}
                else if(stage==13){headTracked=false;if(age>.45f){Check(!pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(1).HasGuard&&!session.FreshInput(0)&&!session.FreshInput(1),"head loss clears both saved scene profiles and input");Next();}}
                else if(stage==14){keys=age<.12f?new[]{Key.F1}:Array.Empty<Key>();if(age>.45f){Check(pilot.source==PrototypeInput.DesktopBoth&&!pilot.RequiresPersonalCalibration&&flow.Objective.Contains("KEYBOARD"),"Keyboard F1 event reaches flow and selects desktop objective");Next();}}
                else if(stage==15&&age>.6f){Check(flow.State==IntegratedPlayFlow.RunState.Battle&&session.FreshInput(0)&&session.FreshInput(1),"desktop neutral input opens scene battle gate");Next();}
                else if(stage==16){keys=age<.12f?new[]{Key.F1}:Array.Empty<Key>();if(age>.5f){Check(pilot.source==PrototypeInput.XRLocalBoth&&flow.State==IntegratedPlayFlow.RunState.Calibrating&&!pilot.ReachProfile(0).HasGuard&&!pilot.PersonalInputArmed,"F1 returns to XR, Retry invalidates profiles and closes gate");Next();}}
                else if(stage==17){guard=age<.12f;if(age>.45f){Check(pilot.ReachProfile(0).HasGuard,"recalibration after bidirectional F1 transition");Next();}}
                else if(stage==18){lp=new Vector3(-.18f,1.45f,.65f);rp=new Vector3(.18f,1.45f,.95f);lreach=rreach=age<.12f;if(age>.45f){Check(pilot.ReachProfile(0).IsComplete&&pilot.ReachProfile(1).IsComplete,"both scene profiles recaptured");Next();}}
                else if(stage==19&&(flow.State==IntegratedPlayFlow.RunState.Battle||age>3)){Check(pilot.PersonalInputArmed&&flow.State==IntegratedPlayFlow.RunState.Battle,"recalibration restores battle gate; armed="+pilot.PersonalInputArmed+" phases="+session.Combat.arms[0].trajectory.Phase+"/"+session.Combat.arms[1].trajectory.Phase+" flow="+flow.State);
                    var target=session.target.ContactColliders.Where(c=>c.enabled&&c.gameObject.activeInHierarchy).OrderBy(c=>c.bounds.size.z).First();
                    Vector3 goal=pilot.ReachProfile(0).Map(new Vector3(.07f,.15f,.35f),session.robotRoot.InverseTransformPoint(session.contact.arms[0].upperArm.position),RobotPlaySession.Neutral(0),Vector3.Distance(session.contact.arms[0].upperArm.position,session.contact.arms[0].forearm.position)+Vector3.Distance(session.contact.arms[0].forearm.position,session.contact.arms[0].hand.position));
                    session.GetComponent<IntegratedBossEncounter>().driver.animator.enabled=false;
                    session.target.transform.position+=session.robotRoot.TransformPoint(goal)+Vector3.forward*(target.bounds.extents.z-.08f)-target.bounds.center;
                    Physics.SyncTransforms();lines.Add("CONTACT selected authored collider="+target.name+" center="+target.bounds.center+" size="+target.bounds.size);hitsBefore=session.target.HitCount;combatBefore=session.Combat.HitCount;Next();}
                else if(stage==20){held=true;lp+=new Vector3(.07f,.15f,.35f)*Mathf.Clamp01(age/.35f);contactSeen|=session.contact.arms[0].IsBlocked;
                    if(age>1.3f){Observe("contact");Check(contactSeen&&session.contact.arms[0].IsBlocked&&session.contact.QueriesLastFrame>0,"actual saved target collider blocks XR-driven arm through normal solver");
                    Check(Vector3.Distance(F(0).RequestedTarget,session.contact.arms[0].hand.position)>.01f,"contact accepted hand stops short of requested XR goal");
                    Check(session.target.HitCount>hitsBefore&&session.contact.arms[0].ContactPart.Contains("Fist"),"actual fist volume produces target impact callback");
                    Check(session.Combat.HitCount>combatBefore,"classified XR fist contact reaches combat authority");Next();}}
                else if(stage==21){keys=age<.12f?new[]{Key.R}:Array.Empty<Key>();if(age>.5f){Check(flow.State==IntegratedPlayFlow.RunState.Calibrating&&!pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(1).HasGuard&&session.Combat.HitCount==0,"Keyboard R Retry clears profiles, combat and battle gate");Finish();return;}}
                Feed(lp,rp,lq,rq,held,guard,lreach,rreach,tracked,headTracked,keys);
            }
            catch(Exception e)
            {
                lines.Add("FAIL: "+e);
                if(SessionState.GetBool(Prefix+"Done",false)){File.WriteAllLines(Path.Combine(Out,"saved-scene-reach.txt"),lines);SessionState.SetBool(Prefix+"Active",false);string restore=SessionState.GetString(Prefix+"Restore","");if(!string.IsNullOrEmpty(restore))EditorSceneManager.OpenScene(restore);if(SessionState.GetBool(Prefix+"Batch",false))EditorApplication.Exit(1);else Debug.LogException(e);}
                else Finish();
            }
        }
        static void Finish()
        {
            if(head!=null&&head.added)InputSystem.RemoveDevice(head);if(left!=null&&left.added)InputSystem.RemoveDevice(left);if(right!=null&&right.added)InputSystem.RemoveDevice(right);if(keyboardOwned&&keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            EditorInputReplayScope.End();EditorXRFixtureScope.End();
            lines.Add("PASS: fixture replay focus counter restored before Play exit");
            int old=SessionState.GetInt(Prefix+"Keyboard",-1);var native=InputSystem.devices.OfType<Keyboard>().FirstOrDefault(k=>k.deviceId==old);if(native!=null)native.MakeCurrent();
            if(!SessionState.GetBool(Prefix+"Recovery",false))File.WriteAllLines(Path.Combine(Out,"pose-observations.csv"),samples);SessionState.SetString(Prefix+"Lines",string.Join("\n",lines));SessionState.SetBool(Prefix+"Done",true);EditorApplication.isPlaying=false;
        }
        static void RecoveryTick()
        {
            if(!session)
            {
                lines.Clear();lines.AddRange(SessionState.GetString(Prefix+"Lines","").Split('\n'));
                session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();pilot=session.GetComponent<RobotPilotInput>();flow=session.GetComponent<IntegratedPlayFlow>();flow.openingGraceSeconds=1000;
                keyboard=Keyboard.current;keyboardOwned=keyboard==null;if(keyboardOwned)keyboard=InputSystem.AddDevice<Keyboard>();
                recoveryKeys=keyboard.allKeys.Where(k=>k.isPressed).Select(k=>k.keyCode).ToArray();
                lines.Add("RECOVERY keyboard device="+keyboard.deviceId+" native="+keyboard.native+" createdOnlyForBatch="+keyboardOwned);
                Check(!InputSystem.devices.Any(d=>d is ReachTestController),"next ordinary Play has no previous synthetic XR devices");
                EditorInputReplayScope.Begin();stage=1;began=Time.time;deadline=EditorApplication.timeSinceStartup+40;
            }
            if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("keyboard recovery test timeout");
            float age=Time.time-began;var keys=Array.Empty<Key>();
            if(stage==1&&age>.4f){Check(pilot.source==PrototypeInput.XRLocalBoth&&flow.State==IntegratedPlayFlow.RunState.Calibrating,"ordinary Play starts in XR calibration after fixture removal");Next();}
            else if(stage==2){if(age<.12f)keys=new[]{Key.F1};if(age>.45f){Check(pilot.source==PrototypeInput.DesktopBoth&&flow.Objective.Contains("KEYBOARD"),"next Play current Keyboard event still drives F1 after previous fixture cleanup");Next();}}
            else if(stage==3&&age>.65f){Check(flow.State==IntegratedPlayFlow.RunState.Battle&&session.FreshInput(0)&&session.FreshInput(1),"keyboard mode still opens battle in the next ordinary Play");Next();}
            else if(stage==4){if(age<.12f)keys=new[]{Key.R};if(age>.45f){Check(session.Combat.HitCount==0&&pilot.source==PrototypeInput.DesktopBoth&&flow.Elapsed<1,"next Play keyboard Retry resets encounter clock and keeps desktop mode");Next();}}
            else if(stage==5){if(age<.12f)keys=new[]{Key.F1};if(age>.5f){Check(pilot.source==PrototypeInput.XRLocalBoth&&flow.State==IntegratedPlayFlow.RunState.Calibrating&&!pilot.PersonalInputArmed,"next Play keyboard returns to XR calibration after fixture cleanup");Next();}}
            else if(stage==6){keys=recoveryKeys;if(age>.3f){Check(keyboard.allKeys.Where(k=>k.isPressed).Select(k=>k.keyCode).SequenceEqual(recoveryKeys),"recovery keyboard pressed-key state restored before exit");Finish();return;}}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        }
    }
}
