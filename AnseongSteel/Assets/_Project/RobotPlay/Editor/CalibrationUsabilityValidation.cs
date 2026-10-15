using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AnseongSteel.PlayerMotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace AnseongSteel.RobotPlay.Editor
{
    [InitializeOnLoad]
    public static class CalibrationUsabilityValidation
    {
        const string Key="CalibrationUsability.",Scene="Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity";
        static readonly List<string> lines=new List<string>();
        static RobotPlaySession session;static RobotPilotInput pilot;static IntegratedPlayFlow flow;
        static ReachCalibrationGuide guide;static RobotRigVisual visual;
        static XRHMD head;static ReachTestController left,right;
        static int stage,stageTicks,frame=-1;static float began;static double deadline;
        static Vector3 targetOriginal,leftGuard,rightGuard;static string output;
        static CalibrationUsabilityValidation(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;}
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);lines.Add("PASS: "+message);}
        static string Hash(){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Scene)));}
        [MenuItem("Anseong Steel/Player Motion/Verify calibration visibility and authored reach")]
        public static void Interactive()=>Begin(false);
        public static void Run()=>Begin(true);
        static void Begin(bool batch)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scenes are preserved.");
            string restore=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if(!batch&&(UnityEngine.SceneManagement.SceneManager.sceneCount!=1||string.IsNullOrEmpty(restore)))throw new InvalidOperationException("Start from one saved scene.");
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--calibration-output");
            output=index>=0?args[index+1]:Path.GetFullPath("../Audit/2026-10-14/evidence/21_hmd_calibration/main");Directory.CreateDirectory(output);
            SessionState.SetString(Key+"Output",output);SessionState.SetString(Key+"Restore",restore);SessionState.SetString(Key+"Hash",Hash());
            SessionState.SetBool(Key+"Batch",batch);SessionState.SetBool(Key+"Done",false);SessionState.SetBool(Key+"Active",true);
            SessionState.SetString(Key+"Settings",JsonUtility.ToJson(InputSystem.settings));
            // In-memory only. This isolated synthetic run must not acquire the user's HMD.
            if(batch&&XRGeneralSettings.Instance){SessionState.SetBool(Key+"XRStart",XRGeneralSettings.Instance.InitManagerOnStart);XRGeneralSettings.Instance.InitManagerOnStart=false;}
            EditorSceneManager.OpenScene(Scene);EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange value)
        {
            if(value==PlayModeStateChange.ExitingPlayMode&&SessionState.GetBool(Key+"Active",false)&&!SessionState.GetBool(Key+"Done",false))
            {lines.Add("FAIL: verification interrupted; fixture cleaned up.");Finish();}
        }
        static void Next(){stage++;stageTicks=0;began=Time.time;}
        static ControllerArmFollower F(int side)=>session.contact.arms[side].upperArm.GetComponent<ControllerArmFollower>();
        static float Bend(int side)
        {var a=session.contact.arms[side];return Vector3.Angle(a.forearm.position-a.upperArm.position,a.hand.position-a.forearm.position);}
        static void StabilitySweep()
        {
            var root=session.robotRoot;var original=root.rotation;
            try
            {
                foreach(float yaw in new[]{0f,90f,180f})
                {
                    root.rotation=Quaternion.Euler(0,yaw,0)*original;
                    for(int side=0;side<2;side++)
                    {
                        var a=session.contact.arms[side];var f=F(side);var p=pilot.ReachProfile(side);
                        float maxElbowStep=0,maxWristStep=0;Vector3 lastElbow=Vector3.zero,lastHand=Vector3.zero;
                        for(int n=0;n<721;n++)
                        {
                            float phase=n*Mathf.PI/180;
                            // Slow yaw/roll sweep, a 1mm tracked-position ripple,
                            // then guard/full/overreach transitions on both rigs.
                            float radius=n<360?1f:Mathf.Lerp(.4f,1.2f,(n-360)/360f);
                            var direction=Quaternion.Euler(10*Mathf.Sin(phase),20*Mathf.Sin(phase),0)*Vector3.forward;
                            var point=p.Shoulder+direction*(p.EffectiveReach*radius)+Vector3.right*(.001f*Mathf.Sin(phase*17));
                            f.Follow(point-p.Guard,Quaternion.Euler(0,25*Mathf.Sin(phase),40*Mathf.Sin(phase)));
                            if(!PunchTrajectoryRecognizer.Finite(a.hand.position)||!PunchTrajectoryRecognizer.Finite(a.forearm.position)||!PunchTrajectoryRecognizer.Finite(a.forearm.rotation))throw new Exception("Nonfinite IK pose");
                            if(n>1&&n!=360){maxElbowStep=Mathf.Max(maxElbowStep,Vector3.Distance(lastElbow,a.forearm.position));maxWristStep=Mathf.Max(maxWristStep,Vector3.Distance(lastHand,a.hand.position));}
                            if(Vector3.Distance(a.upperArm.position,a.hand.position)>=1.9001f)throw new Exception("Reach exceeds limb length");
                            lastElbow=a.forearm.position;lastHand=a.hand.position;
                        }
                        Check(maxElbowStep<.045f&&maxWristStep<.045f,"continuous near-straight IK/no elbow flip through noisy reach and controller rotation, side="+side+" yaw="+yaw+" max elbow/wrist step="+maxElbowStep+"/"+maxWristStep);
                    }
                }
            }
            finally{root.rotation=original;session.contact.ResetContactState();}
        }
        static void FitFistEnvelopes()
        {
            visual.Synchronize();
            for(int side=0;side<2;side++)
            {
                var hand=visual.arms[side].hand;hand.GetComponent<DefaultFistPose>().Apply();
                var volume=session.contact.arms[side].volumes.Single(c=>c.transform.parent==session.contact.arms[side].hand);
                bool found=false;var bounds=new Bounds();int count=0;
                foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled))
                {
                    var weights=skin.sharedMesh.boneWeights;var mesh=new Mesh();skin.BakeMesh(mesh,true);
                    try
                    {
                        bool Belongs(int index)=>skin.bones[index]==hand||skin.bones[index].IsChildOf(hand);
                        for(int i=0;i<mesh.vertexCount;i++)
                        {
                            var w=weights[i];float influence=(Belongs(w.boneIndex0)?w.weight0:0)+(w.weight1>0&&Belongs(w.boneIndex1)?w.weight1:0)+(w.weight2>0&&Belongs(w.boneIndex2)?w.weight2:0)+(w.weight3>0&&Belongs(w.boneIndex3)?w.weight3:0);
                            if(influence<=.75f)continue;
                            var p=volume.transform.InverseTransformPoint(skin.transform.TransformPoint(mesh.vertices[i]));
                            if(!found){bounds=new Bounds(p,Vector3.zero);found=true;}else bounds.Encapsulate(p);count++;
                        }
                    }
                    finally{UnityEngine.Object.DestroyImmediate(mesh);}
                }
                Check(found&&count>100,"closed fist weighted envelope has enough authored vertices, side="+side);
                lines.Add("ENVELOPE before side="+side+" center="+volume.center+" size="+volume.size);
                volume.center=bounds.center;volume.size=bounds.size;
                lines.Add("ENVELOPE after side="+side+" center="+volume.center+" size="+volume.size+" vertices="+count);
            }
            Physics.SyncTransforms();session.contact.ResetContactState();
        }
        static void PoseRecord(string label)
        {
            if(label=="original-enemy-forward")
            {
                foreach(float x in new[]{-.2732f,0f,.2732f})lines.Add("BOSS SURFACE x="+x+" y=1.3164 frontZ="+VisibleBossFront(new Vector3(x,1.3164f,-2))+" proxyFrontZ="+session.target.GetComponent<BoxCollider>().bounds.min.z);
                foreach(var a in session.contact.arms)foreach(var c in a.volumes.Where(c=>c.transform.parent==a.hand))lines.Add("FIST PROXY "+a.label+" center="+c.bounds.center+" extents="+c.bounds.extents+" localCenter="+c.center+" localSize="+c.size+" scale="+c.transform.lossyScale);
            }
            for(int i=0;i<2;i++)
            {
                var a=session.contact.arms[i];var v=visual.arms[i];
                lines.Add("OBS "+label+" side="+i+" requested="+F(i).RequestedTarget.ToString("F5")+" accepted="+a.hand.position.ToString("F5")+" elbowFlexDegrees="+Bend(i)+" extension="+F(i).Extension+" blocked="+a.IsBlocked+" part="+a.ContactPart);
                Check(Vector3.Distance(v.hand.position,a.hand.position)<.003f&&Vector3.Distance(v.lower.position,a.forearm.position)<.003f,label+" visual wrist/elbow match accepted contact, side "+i);
                var fist=v.hand.GetComponent<DefaultFistPose>();
                Check(Vector3.Angle(v.hand.TransformDirection(fist.palmForwardLocal),v.hand.position-v.lower.position)<1,label+" straight wrist on visible fist, side "+i);
            }
            foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled))
            {
                var mesh=new Mesh();try{skin.BakeMesh(mesh,true);Check(mesh.vertices.All(PunchTrajectoryRecognizer.Finite),label+" finite actual skin "+skin.name);
                    int side=skin.name.Contains("Left")?0:1;var a=visual.arms[side];var axis=(a.hand.position-a.lower.position).normalized;
                    var weights=skin.sharedMesh.boneWeights;bool HandBone(int index)=>skin.bones[index]==a.hand||skin.bones[index].IsChildOf(a.hand);
                    float HandWeight(BoneWeight w)=>(HandBone(w.boneIndex0)?w.weight0:0)+(w.weight1>0&&HandBone(w.boneIndex1)?w.weight1:0)+(w.weight2>0&&HandBone(w.boneIndex2)?w.weight2:0)+(w.weight3>0&&HandBone(w.boneIndex3)?w.weight3:0);
                    var fistPoints=mesh.vertices.Where((v,i)=>HandWeight(weights[i])>.75f).Select(skin.transform.TransformPoint).ToArray();
                    Check(fistPoints.Length>100,label+" visible fist has hand/finger-weighted vertices "+skin.name);
                    float tip=fistPoints.Select(v=>Vector3.Dot(v-a.hand.position,axis)).Max();float radius=fistPoints.Select(v=>Vector3.Distance(v,a.hand.position)).Max();
                    lines.Add("MESH "+label+" "+skin.name+" rendererScale="+skin.transform.lossyScale+" bakedBounds="+mesh.bounds+" rendererWorldBounds="+skin.bounds+" fistTip="+tip+" fistRadius="+radius+" fistWorldFrontZ="+fistPoints.Max(v=>v.z)+" proxyFrontZ="+session.target.GetComponent<BoxCollider>().bounds.min.z);
                    Check(tip>.02f&&radius<1.5f,label+" actual hand-weighted fist geometry remains near wrist "+skin.name+" (tip="+tip+" radius="+radius+")");
                }finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        static float VisibleBossFront(Vector3 origin)
        {
            float nearest=float.PositiveInfinity;
            foreach(var renderer in session.target.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy))
            {
                Mesh mesh=null;bool owned=false;
                if(renderer is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh,true);owned=true;}
                else {var filter=renderer.GetComponent<MeshFilter>();if(filter)mesh=filter.sharedMesh;}
                if(!mesh)continue;
                try
                {
                    var v=mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();var t=mesh.triangles;
                    for(int i=0;i<t.Length;i+=3)
                    {
                        var e1=v[t[i+1]]-v[t[i]];var e2=v[t[i+2]]-v[t[i]];var p=Vector3.Cross(Vector3.forward,e2);float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<1e-8f)continue;
                        float inv=1/det;var q=origin-v[t[i]];float u=Vector3.Dot(q,p)*inv;if(u<0||u>1)continue;
                        var cross=Vector3.Cross(q,e1);float w=Vector3.Dot(Vector3.forward,cross)*inv;if(w<0||u+w>1)continue;float distance=Vector3.Dot(e2,cross)*inv;if(distance>=0)nearest=Mathf.Min(nearest,origin.z+distance);
                    }
                }finally{if(owned)UnityEngine.Object.DestroyImmediate(mesh);}
            }
            return nearest;
        }
        static void Setup()
        {
            lines.Clear();output=SessionState.GetString(Key+"Output","");stage=0;frame=-1;deadline=EditorApplication.timeSinceStartup+180;
            session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();pilot=session.GetComponent<RobotPilotInput>();flow=session.GetComponent<IntegratedPlayFlow>();guide=session.GetComponent<ReachCalibrationGuide>();
            visual=session.robotRoot.GetComponentsInChildren<RobotRigVisual>(true).Single(v=>v.gameObject.name==IntegratedArmRepairBuilder.VisualName);
            Check(pilot.source==PrototypeInput.XRLocalBoth&&guide&&guide.Panel,"saved XR scene creates independent calibration guide");
            Check(!session.contact.enabled,"authored contact is session-driven, no second LateUpdate constraint");
            flow.openingGraceSeconds=1000;targetOriginal=session.target.transform.position;
            Check(session.target.ContactColliders.Any(c=>c is BoxCollider),"original authored target BoxCollider retained");
            lines.Add("BASELINE original target transform="+targetOriginal+" box="+session.target.GetComponent<BoxCollider>().bounds);
            EditorXRFixtureScope.Begin();lines.Add("FIXTURE original enabled XR devices suspended="+EditorXRFixtureScope.SuspendedCount);InputSystem.RegisterLayout<ReachTestController>();head=InputSystem.AddDevice<XRHMD>();left=InputSystem.AddDevice<ReachTestController>();right=InputSystem.AddDevice<ReachTestController>();
            InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);EditorInputReplayScope.Begin();
            if(Environment.GetCommandLineArgs().Contains("--fit-fist-envelope"))FitFistEnvelopes();
            Check(guide.Panel.renderMode==RenderMode.WorldSpace&&guide.Panel.worldCamera==pilot.cockpitCamera&&guide.Panel.transform.parent==pilot.cockpitCamera.transform,"guide rendered by actual cockpit/HMD camera, not relay feed or screen overlay");
            Check(pilot.cockpitCamera.targetTexture==null&&pilot.cockpitCamera.stereoTargetEye==StereoTargetEyeMask.Both,"authored HMD camera targets both eyes without relay texture");
            lines.Add("GUIDE mask="+pilot.cockpitCamera.cullingMask+" layer="+guide.Panel.gameObject.layer+" root="+guide.Panel.isRootCanvas+" override="+guide.Panel.overrideSorting+" order="+guide.Panel.sortingOrder);
            Check((pilot.cockpitCamera.cullingMask&(1<<guide.Panel.gameObject.layer))!=0&&(guide.Panel.isRootCanvas||guide.Panel.overrideSorting)&&guide.Panel.sortingOrder>30000,"guide layer included and independent high canvas order");
            foreach(var c in new[]{flow.bridge.feed.exteriorCamera,flow.bridge.feed.leftCamera,flow.bridge.feed.rightCamera})Check((c.cullingMask&(1<<guide.Panel.gameObject.layer))==0,"robot relay feed excludes head-relative guide: "+c.name);
            Check(guide.Label.material.renderQueue>=4000&&guide.Label.material.shader.name=="AnseongSteel/ReachCalibrationOverlay","depth-independent overlay shader selected");
        }
        static void Feed(Vector3 lp,Vector3 rp,bool y=false,bool x=false,bool a=false,bool lt=true,bool rt=true,bool ht=true,bool grip=false)
        {
            InputSystem.QueueDeltaStateEvent(head.GetChildControl<Vector3Control>("centerEyePosition"),new Vector3(0,1.65f,0));
            InputSystem.QueueDeltaStateEvent(head.GetChildControl<QuaternionControl>("centerEyeRotation"),Quaternion.identity);
            InputSystem.QueueDeltaStateEvent(head.GetChildControl<IntegerControl>("trackingState"),ht?3:0);
            InputSystem.QueueStateEvent(left,new ReachTestState{position=lp,rotation=Quaternion.identity,isTracked=lt?1:0,trackingState=lt?3:0,secondaryButton=y?1:0,primaryButton=x?1:0,gripPressed=grip?1:0});
            InputSystem.QueueStateEvent(right,new ReachTestState{position=rp,rotation=Quaternion.identity,isTracked=rt?1:0,trackingState=rt?3:0,primaryButton=a?1:0,gripPressed=grip?1:0});
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key+"Active",false))return;
            try
            {
                if(SessionState.GetBool(Key+"Done",false))
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    output=SessionState.GetString(Key+"Output","");lines.Clear();lines.AddRange(SessionState.GetString(Key+"Lines","").Split('\n'));
                    Check(Hash()==SessionState.GetString(Key+"Hash",""),"authored scene bytes preserved");Check(InputSystem.settings&&JsonUtility.ToJson(InputSystem.settings)==SessionState.GetString(Key+"Settings",""),"InputSystem settings values preserved");
                    Check(!InputSystem.devices.Any(d=>d is ReachTestController),"synthetic controllers removed after Play");
                    if(SessionState.GetBool(Key+"Batch",false)&&XRGeneralSettings.Instance)XRGeneralSettings.Instance.InitManagerOnStart=SessionState.GetBool(Key+"XRStart",true);
                    File.WriteAllLines(Path.Combine(output,"usability.txt"),lines);SessionState.SetBool(Key+"Active",false);
                    string restore=SessionState.GetString(Key+"Restore","");if(!string.IsNullOrEmpty(restore))EditorSceneManager.OpenScene(restore);
                    if(SessionState.GetBool(Key+"Batch",false))EditorApplication.Exit(lines.Any(l=>l.StartsWith("FAIL:"))?1:0);else Debug.Log(string.Join("\n",lines));return;
                }
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<20)return;
                EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
                if(!session){Setup();Next();}
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("calibration usability timeout");
                int processedStage=stage;bool pulse=stageTicks<3,settled=stageTicks>=6;float age=Time.time-began;var lp=new Vector3(-.25f,1.3f,.3f);var rp=new Vector3(.25f,1.3f,.3f);
                bool y=false,x=false,a=false,lt=true,rt=true,ht=true,grip=false;
                if(stage==1){lt=false;y=pulse;if(settled&&age>.45f){Check(!pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(1).HasGuard&&pilot.LastCalibrationMessage.Contains("가드 저장 실패"),"Y with untracked hand rejects immediately with Korean feedback, preserves profiles");Next();}}
                else if(stage==2&&settled&&age>.45f){Check(!pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(1).HasGuard,"reacquired hand cannot capture a deferred Y guard");guide.Refresh();Visibility("tracking-ready");Next();}
                else if(stage==3){y=pulse;if(settled&&age>.45f){Check(pilot.ReachProfile(0).HasGuard&&pilot.ReachProfile(1).HasGuard,"tracked Y saves both guards; head="+pilot.HeadTracked+" hands="+pilot.HandTracked(0)+"/"+pilot.HandTracked(1)+" feedback="+pilot.LastCalibrationMessage);Next();}}
                else if(stage==4){lt=false;x=pulse;if(settled&&age>.45f){Check(!pilot.ReachProfile(0).IsComplete&&pilot.LastCalibrationMessage.Contains("왼팔 저장 실패"),"X during hand loss rejects visibly instead of disappearing before CaptureReach");Next();}}
                else if(stage==5&&settled&&age>.4f){Check(!pilot.ReachProfile(0).IsComplete,"X edge is not replayed later after tracking returns");Next();}
                else if(stage==6){x=pulse;if(settled&&age>.4f){Check(!pilot.ReachProfile(0).IsComplete&&pilot.LastCalibrationMessage.Contains("가드보다"),"insufficient reach reports distance/guard failure in Korean");guide.Refresh();Visibility("rejected-reach");Next();}}
                else if(stage==7){lp=new Vector3(-.18f,1.45f,.65f);x=pulse;if(settled&&age>.45f){Check(pilot.ReachProfile(0).IsComplete&&!pilot.ReachProfile(1).IsComplete,"X completes left only, right A still required for battle");Next();}}
                else if(stage==8){lp+=new Vector3(-.12f,0,0);if(settled&&age>.55f){Check(Vector3.Distance(session.contact.arms[0].hand.position,session.robotRoot.TransformPoint(RobotPlaySession.Neutral(0)))>.1f&&flow.State==IntegratedPlayFlow.RunState.Calibrating,"left arm responds before right A without opening battle gate");Next();}}
                else if(stage==9){rp=new Vector3(.18f,1.45f,.95f);a=pulse;if(settled&&age>.45f){Check(pilot.ReachProfile(1).IsComplete,"A saves right effective reach");Next();}}
                else if(stage==10&&(flow.State==IntegratedPlayFlow.RunState.Battle||settled&&age>3)){Check(pilot.PersonalInputArmed&&flow.State==IntegratedPlayFlow.RunState.Battle,"both release and ready dwell open existing battle gate");flow.bridge.Hud.SetMenuOpen(true);guide.Refresh();Check(guide.Visible,"calibration feedback remains visible while existing HUD menu is open");Visibility("menu-open");flow.bridge.Hud.SetMenuOpen(false);Next();}
                else if(stage==11){lp=new Vector3(-.18f,1.45f,.65f);rp=new Vector3(.18f,1.45f,.95f);grip=true;if(settled&&age>1.6f){Check(session.target.transform.position==targetOriginal,"forward reach first tested at original saved enemy placement");Check(session.contact.arms.Any(v=>v.IsBlocked),"original enemy contact legitimately limits forward reach");CaptureArms("original-contact");PoseRecord("original-enemy-forward");guide.Refresh();Check(guide.Label.text.Contains("敵")==false&&guide.Label.text.Contains("적에 닿아"),"contact limit shown separately from calibration failure");Visibility("original-contact");session.target.transform.position=targetOriginal+Vector3.forward*4;Physics.SyncTransforms();session.contact.ResetContactState();lines.Add("CONTROL ONLY: target runtime instance parked 4m AFTER original-layout contact evidence; no scene save.");Next();}}
                else if(stage==12){lp=new Vector3(-.18f,1.45f,.65f);rp=new Vector3(.18f,1.45f,.95f);grip=true;if(settled&&age>1.4f){for(int i=0;i<2;i++)Check(!session.contact.arms[i].IsBlocked&&Bend(i)>2.5f&&Bend(i)<3.5f&&F(i).Extension>.99f&&F(i).Extension<1,"free full reach nearly straight with positive elbow bend (2.5-3.5 degrees), side "+i);CaptureArms("free-forward");PoseRecord("free-forward");Next();}}
                else if(stage==13){lp=new Vector3(-.18f,1.45f,.25f);rp=new Vector3(.18f,1.45f,.15f);y=pulse;if(settled&&age>.45f){Check(!pilot.ReachProfile(0).IsComplete&&!pilot.ReachProfile(1).IsComplete,"new guard clears both previous reaches");Next();}}
                else if(stage==14){lp=new Vector3(-.18f,1.45f,1.3f);rp=new Vector3(.18f,1.45f,.42f);x=a=pulse;if(settled&&age>.55f){Check(pilot.ReachProfile(0).IsComplete&&pilot.ReachProfile(1).IsComplete,"long 1.30m and short .42m effective reach capture");Next();}}
                else if(stage==15){lp=new Vector3(-.18f,1.45f,1.3f);rp=new Vector3(.18f,1.45f,.42f);if(settled&&age>1.5f){Check(session.FreshInput(0)&&Bend(0)>2.5f&&Bend(0)<3.5f&&Bend(1)>2.5f&&Bend(1)<3.5f,"long guard-to-reach delta1.05m is not truncated by legacy .942m transport cap; both sizes achieve near-straight geometry");PoseRecord("long-short-forward");StabilitySweep();Next();}}
                else if(stage==16){lp=new Vector3(-.18f,1.45f,.25f)+new Vector3(.10f,.03f,.08f);rp=new Vector3(.18f,1.45f,.15f)+new Vector3(-.07f,.01f,.04f);grip=true;if(settled&&age>.7f){Check(Bend(0)>45&&Bend(1)>45&&F(0).Extension<.9f&&F(1).Extension<.9f,"near hooks preserve substantial elbow flexion for long and short reach");PoseRecord("long-short-hooks");Next();}}
                else if(stage==17){lt=false;grip=true;if(settled&&age>.45f){Check(pilot.ReachProfile(0).IsComplete&&!session.Combat.arms[0].attackHeld,"hand loss preserves profile but stops attack");guide.Refresh();Check(guide.Visible&&guide.Label.text.Contains("왼손 없음"),"hand-loss guide reappears during battle with per-device status");Next();}}
                else if(stage==18){ht=false;if(settled&&age>.45f){Check(!pilot.ReachProfile(0).HasGuard&&!pilot.ReachProfile(1).HasGuard,"head loss invalidates calibration frame");guide.Refresh();Check(guide.Visible&&guide.Label.text.Contains("머리 없음"),"head-loss guide remains visible and requests recalibration");Finish();return;}}
                Feed(lp,rp,y,x,a,lt,rt,ht,grip);if(stage==processedStage)stageTicks++;
            }
            catch(Exception e){lines.Add("FAIL: "+e);if(SessionState.GetBool(Key+"Done",false)){File.WriteAllLines(Path.Combine(SessionState.GetString(Key+"Output",""),"usability.txt"),lines);SessionState.SetBool(Key+"Active",false);if(SessionState.GetBool(Key+"Batch",false))EditorApplication.Exit(1);else Debug.LogException(e);}else Finish();}
        }
        static void Visibility(string label)
        {
            guide.Refresh();Canvas.ForceUpdateCanvases();
            Check(guide.Visible,label+" independent guide is active");
            Check(guide.Label.text.Where(c=>!char.IsWhiteSpace(c)).All(c=>guide.Label.font.HasCharacter(c)),label+" every Korean glyph exists in bundled font");
            Check(guide.Label.preferredHeight<=guide.Label.rectTransform.rect.height,label+" all guidance fits without vertical truncation");
            foreach(bool rightView in new[]{false,true})
            {
                session.GetComponent<IntegratedCockpitSelector>().View(rightView);guide.Refresh();
                foreach(int eye in new[]{-1,1})
                {
                    var go=new GameObject("Synthetic eye validation");var cam=go.AddComponent<Camera>();cam.CopyFrom(pilot.cockpitCamera);cam.stereoTargetEye=StereoTargetEyeMask.None;
                    cam.transform.SetPositionAndRotation(pilot.cockpitCamera.transform.position+pilot.cockpitCamera.transform.right*(eye*.032f),pilot.cockpitCamera.transform.rotation);
                    float n=cam.nearClipPlane;cam.projectionMatrix=Matrix4x4.Frustum(-n*(eye<0?1f:.75f),n*(eye<0?.75f:1f),-n*.75f,n*.9f,n,cam.farClipPlane);
                    try{var corners=new Vector3[4];((RectTransform)guide.Panel.transform).GetWorldCorners(corners);Check(corners.Select(cam.WorldToViewportPoint).All(v=>v.z>n&&v.x>.03f&&v.x<.97f&&v.y>.03f&&v.y<.97f),label+" panel fits conservative asymmetric eye frustum, seat="+rightView+" eye="+eye);
                    if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)Capture(cam,label+"-seat"+(rightView?"R":"L")+"-eye"+eye);
                    }finally{UnityEngine.Object.DestroyImmediate(go);}
                }
            }
            session.GetComponent<IntegratedCockpitSelector>().View(false);guide.Refresh();
            var context=flow.bridge.cockpitContext;var comparison=context.GetComponent<RobotViewComparison>();bool added=!comparison;
            if(added){comparison=context.gameObject.AddComponent<RobotViewComparison>();comparison.input=context;}
            try{comparison.SetDirect(true);guide.Refresh();Check(guide.Visible&&guide.AttachedCamera==comparison.directCamera&&guide.Panel.worldCamera==comparison.directCamera,label+" guide follows active direct XR view rather than disabled cockpit camera");
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)Capture(comparison.directCamera,label+"-direct-view");
            }finally{comparison.SetDirect(false);guide.Refresh();if(added)UnityEngine.Object.DestroyImmediate(comparison);}
        }
        static void Capture(Camera camera,string name)
        {
            var rt=new RenderTexture(1000,1000,24);rt.Create();var prior=RenderTexture.active;
            try{foreach(var c in new[]{flow.bridge.feed.exteriorCamera,flow.bridge.feed.leftCamera,flow.bridge.feed.rightCamera})RenderPipeline.SubmitRenderRequest(c,new UniversalRenderPipeline.SingleCameraRequest{destination=c.targetTexture});
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;var tex=new Texture2D(1000,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1000,1000),0,0);tex.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());
                if(!name.Contains("actual-skins"))
                {
                    var visible=tex.GetPixels32();guide.Panel.gameObject.SetActive(false);
                    try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1000,1000),0,0);tex.Apply();var hidden=tex.GetPixels32();
                        int contributed=visible.Select((c,i)=>c.r>180&&c.g>180&&c.b>180&&(Mathf.Abs(c.r-hidden[i].r)+Mathf.Abs(c.g-hidden[i].g)+Mathf.Abs(c.b-hidden[i].b)>40)?1:0).Sum();
                        Check(contributed>200,"guide-only text changes rendered pixels "+name+" count="+contributed);
                    }finally{guide.Panel.gameObject.SetActive(true);}
                }
                UnityEngine.Object.DestroyImmediate(tex);
            }finally{RenderTexture.active=prior;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }
        static void CaptureArms(string label)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            var go=new GameObject("Arm geometry inspection camera");var cam=go.AddComponent<Camera>();cam.stereoTargetEye=StereoTargetEyeMask.None;cam.cullingMask=1<<27;cam.nearClipPlane=.03f;cam.farClipPlane=15;cam.fieldOfView=55;
            cam.transform.position=new Vector3(-2.4f,2.0f,2.8f);cam.transform.LookAt(new Vector3(0,1.5f,.8f));
            try{Capture(cam,label+"-actual-skins");}finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        static void Finish()
        {
            if(head!=null&&head.added)InputSystem.RemoveDevice(head);if(left!=null&&left.added)InputSystem.RemoveDevice(left);if(right!=null&&right.added)InputSystem.RemoveDevice(right);EditorInputReplayScope.End();
            int restored=EditorXRFixtureScope.SuspendedCount;EditorXRFixtureScope.End();lines.Add("PASS: original XR enabled states restored before Play exit; device count="+restored);
            SessionState.SetString(Key+"Lines",string.Join("\n",lines));SessionState.SetBool(Key+"Done",true);EditorApplication.isPlaying=false;
        }
    }
}
