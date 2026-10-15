using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AnseongSteel.PlayerMotion;

namespace AnseongSteel.RobotPlay.Editor
{
    // Opens the authored scene as saved. No repair/build/update method is called.
    [InitializeOnLoad]
    public static class IntegratedArmRepairValidation
    {
        const string Key="IntegratedArmRepair.Validation.";
        static RobotPlaySession session;
        static RobotRigVisual rig;
        static IntegratedCockpitSelector selector;
        static IntegratedCombatHud bridge;
        static double began;
        static int frame=-1;
        static readonly List<string> lines=new List<string>();
        static IntegratedArmRepairValidation() { EditorApplication.update+=Tick; began=EditorApplication.timeSinceStartup; }
        public static void Before() => Begin(false);
        [MenuItem("Anseong Steel/Robot Play/Verify saved integrated v026 arms")]
        public static void After() => Begin(true);
        static string Hash() { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(IntegratedArmRepair.ScenePath))); }
        static void Begin(bool repaired)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before validating saved arms.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes are preserved; save or finish them before validation.");
            began=EditorApplication.timeSinceStartup;
            EditorSceneManager.OpenScene(IntegratedArmRepair.ScenePath);
            SessionState.SetString(Key+"Hash",Hash()); SessionState.SetBool(Key+"After",repaired);
            SessionState.SetInt(Key+"Stage",0); SessionState.SetBool(Key+"Done",false); SessionState.SetBool(Key+"Active",true);
            Directory.CreateDirectory(IntegratedArmRepair.Output);
            EditorApplication.isPlaying=true;
        }
        static void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); lines.Add("PASS "+message); }
        static void Tick()
        {
            if(!SessionState.GetBool(Key+"Active",false))return;
            try
            {
                if(SessionState.GetBool(Key+"Done",false))
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    bool unchanged=Hash()==SessionState.GetString(Key+"Hash","");
                    string result=SessionState.GetString(Key+"Result","")+"\nSaved scene hash unchanged by validation="+unchanged;
                    File.WriteAllText(IntegratedArmRepair.Output+(SessionState.GetBool(Key+"After",false)?"/after-validation.txt":"/before-validation.txt"),result);
                    SessionState.SetBool(Key+"Active",false); Debug.Log(result);
                    if(Application.isBatchMode) EditorApplication.Exit(result.Contains("FAIL")||!unchanged?1:0); return;
                }
                if(EditorApplication.timeSinceStartup-began>180) throw new TimeoutException("saved-scene visual validation timeout");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<15)return;
                EditorApplication.QueuePlayerLoopUpdate(); if(frame==Time.frameCount)return; frame=Time.frameCount;
                bool repaired=SessionState.GetBool(Key+"After",false);int stage=SessionState.GetInt(Key+"Stage",0);
                if(stage==0)
                {
                    lines.Clear();lines.Add("Unity="+Application.unityVersion+" mode="+(repaired?"after":"before")+"; synthetic contact-pose visual inspection, no HMD.");
                    session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();
                    bridge=session.GetComponent<IntegratedCombatHud>();selector=session.GetComponent<IntegratedCockpitSelector>();
                    session.GetComponent<RobotPilotInput>().enabled=false;session.GetComponent<IntegratedPlayFlow>().enabled=false;
                    session.GetComponent<IntegratedBossEncounter>().Stop();session.externalBossTimeline=true;session.enabled=false;
                    rig=session.robotRoot.GetComponentsInChildren<RobotRigVisual>(true).FirstOrDefault(r=>r.gameObject.name==IntegratedArmRepairBuilder.VisualName);
                    var skins=session.robotRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                    lines.Add("combat visual active skins="+skins.Length);
                    if(repaired)
                    {
                        Check(rig&&skins.Length==2,"saved scene contains both active v026 arm skins and visual binding");
                        Check(AssetDatabase.AssetPathToGUID(IntegratedArmRepair.ModelPath)=="5f2a703d67e284b49a38486f82cad5b1","normal v026 source GUID retained");
                        foreach(var skin in skins)
                        {
                            Check(skin.sharedMesh&&skin.sharedMesh.vertexCount>0&&skin.sharedMesh.triangles.Length>0,"nonempty mesh "+skin.name);
                            Check(skin.bones.All(b=>b)&&skin.bones.Length==skin.sharedMesh.bindposes.Length,"valid bone/bindpose array "+skin.name);
                            Check(skin.rootBone&&skin.sharedMaterials.Length==skin.sharedMesh.subMeshCount&&skin.sharedMaterials.All(m=>m&&!m.shader.name.Contains("Error")),"valid root/material/submesh slots "+skin.name);
                        }
                        var fists=rig.GetComponentsInChildren<DefaultFistPose>();Check(fists.Length==2,"two persistent default fists");
                        foreach(var fist in fists)Check(fist.joints.Length==15&&fist.closedRotations.Length==15&&fist.joints.All(j=>j),"15 actual finger bones per hand");
                    }
                    else Check(!rig&&skins.Length==0,"baseline confirms missing combat visual arms");
                    foreach(var missing in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>PrefabUtility.IsPrefabAssetMissing(t.gameObject)))
                        lines.Add("REMAINING MISSING "+missing.name+" active="+missing.gameObject.activeInHierarchy);
                    lines.Add("boss="+session.GetComponent<IntegratedBossEncounter>().driver.name+" pilots="+UnityEngine.Object.FindObjectsByType<CockpitPilotAvatar>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length+" cockpit="+selector.CurrentName);
                }
                else if(stage<=12)
                {
                    string[] names={"neutral","left-jab","right-jab","left-hook","right-hook","both-guard"};
                    int pose=(stage-1)/2;var l=Vector3.zero;var r=Vector3.zero;
                    if(stage%2==1)
                    {
                    if(pose==1)l=RobotPilotInput.DesktopPose(0,.22f,false);
                    if(pose==2)r=RobotPilotInput.DesktopPose(1,.22f,false);
                    if(pose==3)l=RobotPilotInput.DesktopPose(0,.4f,true);
                    if(pose==4)r=RobotPilotInput.DesktopPose(1,.4f,true);
                    if(pose==5)l=r=new Vector3(0,.65f,0);
                    session.Pose(0,l);session.Pose(1,r);if(rig)rig.Synchronize();
                    if(repaired)
                    {
                        for(int side=0;side<2;side++)
                        {
                            var binding=rig.arms[side];var source=session.contact.arms[side];
                            Check(Vector3.Distance(binding.upper.position,source.upperArm.position)<.001f&&Vector3.Distance(binding.lower.position,source.forearm.position)<.001f&&Vector3.Distance(binding.hand.position,source.hand.position)<.001f,"pose "+names[pose]+" side "+side+" follows contact shoulder/elbow/hand");
                            var fist=binding.hand.GetComponent<DefaultFistPose>();fist.Apply();
                            Check(fist.joints.Select((joint,i)=>Quaternion.Angle(joint.localRotation,fist.closedRotations[i])).Max()<.01f,"pose "+names[pose]+" closed fingers independent of attack button");
                            float straight=Vector3.Angle(binding.hand.TransformDirection(fist.palmForwardLocal),source.hand.position-source.forearm.position);
                            Check(straight<1,"pose "+names[pose]+" rigid wrist straightness <1 degree ("+straight+")");
                        }
                        foreach(var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled))
                        {
                            var baked=new Mesh();skin.BakeMesh(baked);Check(baked.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"pose "+names[pose]+" finite baked skin "+skin.name);UnityEngine.Object.DestroyImmediate(baked);
                        }
                    }
                    // Skinning buffers are evaluated by Unity once per frame. Capture on the next frame,
                    // after normal LateUpdate, rather than labeling the previous frame's GPU pose.
                    SessionState.SetInt(Key+"Stage",stage+1);return;
                    }
                    Capture(names[pose],false,repaired);Capture(names[pose],true,repaired);
                    if(repaired && pose==0) { CaptureHand(0); CaptureHand(1); }
                }
                else
                {
                    if(repaired){RobotCombatChecks.Run();IntegratedRulesChecks.Run();lines.Add("PASS existing pure combat and integrated rules executed on this worktree");}
                    Finish();return;
                }
                SessionState.SetInt(Key+"Stage",stage+1);
            }
            catch(Exception e) { lines.Add("FAIL "+e);Finish(); }
        }
        static void Capture(string pose,bool right,bool repaired)
        {
            selector.View(right);var cam=session.GetComponent<RobotPilotInput>().cockpitCamera;
            var rt=new RenderTexture(1440,1000,24);rt.Create();var previous=RenderTexture.active;float aspect=cam.aspect;
            try
            {
                cam.aspect=1.44f;bridge.Hud.LayoutHelmet(1.44f);Canvas.ForceUpdateCanvases();
                foreach(var feed in new[]{bridge.feed.exteriorCamera,bridge.feed.leftCamera,bridge.feed.rightCamera})
                    RenderPipeline.SubmitRenderRequest(feed,new UniversalRenderPipeline.SingleCameraRequest{destination=feed.targetTexture});
                RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();
                string name=(repaired?"after-":"before-")+pose+(right?"-right":"-left");
                File.WriteAllBytes(IntegratedArmRepair.Output+"/"+name+".png",tex.EncodeToPNG());
                int blue=tex.GetPixels32().Count(c=>c.b>65&&c.b>c.r*1.6f&&c.b>c.g*1.15f);
                lines.Add("CAPTURE "+name+" blue pixels="+blue);
                if(repaired)
                {
                    // Compare this same view with only the new arm skins hidden; HUD cyan is not arm evidence.
                    var skins=rig.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled).ToArray();
                    var visible=tex.GetPixels32();
                    try
                    {
                        foreach(var skin in skins)skin.forceRenderingOff=true;
                        foreach(var feed in new[]{bridge.feed.exteriorCamera,bridge.feed.leftCamera,bridge.feed.rightCamera})
                            RenderPipeline.SubmitRenderRequest(feed,new UniversalRenderPipeline.SingleCameraRequest{destination=feed.targetTexture});
                        RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();var hidden=tex.GetPixels32();
                        int contributed=visible.Select((c,i)=>Mathf.Abs(c.r-hidden[i].r)+Mathf.Abs(c.g-hidden[i].g)+Mathf.Abs(c.b-hidden[i].b)).Count(d=>d>35);
                        lines.Add("ARM-ONLY VISIBILITY DIFFERENCE "+name+" pixels="+contributed);
                        Check(contributed>300,"v026 arm geometry actually contributes pixels in "+name);
                    }
                    finally { foreach(var skin in skins)skin.forceRenderingOff=false; }
                }
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.active=previous;cam.aspect=aspect;bridge.Hud.LayoutHelmet(aspect);rt.Release();UnityEngine.Object.DestroyImmediate(rt); }
        }
        static void CaptureHand(int side)
        {
            var binding=rig.arms[side];
            var camera=new GameObject("Temporary hand review camera").AddComponent<Camera>();
            var rt=new RenderTexture(1024,768,24);rt.Create();var previous=RenderTexture.active;
            try
            {
                camera.cullingMask=1<<27;camera.nearClipPlane=.03f;camera.farClipPlane=4;camera.fieldOfView=50;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.03f,.045f);
                var fist=binding.hand.GetComponent<DefaultFistPose>();
                var forward=binding.hand.TransformDirection(fist.palmForwardLocal);
                var back=binding.hand.TransformDirection(-fist.palmInwardLocal);
                var target=binding.hand.position+forward*.18f;
                camera.transform.position=target+forward*1.3f+back*.35f;
                camera.transform.LookAt(target,back);
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                RenderTexture.active=rt;var tex=new Texture2D(1024,768,TextureFormat.RGB24,false);
                tex.ReadPixels(new Rect(0,0,1024,768),0,0);tex.Apply();
                File.WriteAllBytes(IntegratedArmRepair.Output+(side==0?"/after-fist-detail-left.png":"/after-fist-detail-right.png"),tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject); }
        }
        static void Finish()
        {
            SessionState.SetString(Key+"Result",string.Join("\n",lines));SessionState.SetBool(Key+"Done",true);EditorApplication.isPlaying=false;
        }
    }
}
