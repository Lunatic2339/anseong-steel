using System;
using System.Collections.Generic;
using System.IO;
using AnseongSteel.PlayerMotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace AnseongSteel.Hud.Editor
{
    [InitializeOnLoad]
    public static class HudValidation
    {
        const string Active="Hud.Validation.Active", Root="Assets/_Project/HudUI";
        static readonly List<string> checks=new List<string>();
        static double began=-1;
        static Camera cleanupCamera;
        static bool validated;
        static string Output=>Environment.GetEnvironmentVariable("HUD_REVIEW_OUTPUT") ?? Path.Combine(Application.dataPath,"../HudReview");
        static HudValidation() { EditorApplication.update+=Tick; }
        [MenuItem("Anseong Steel/HUD/Open V02 HUD Test")]
        public static void BuildTestScene()
        {
            // Open the existing authored V02/punch scene and save a new HUD-only test copy.
            EditorSceneManager.OpenScene("Assets/_Project/04_Scenes/ArmMotionTest.unity");
            Directory.CreateDirectory(Root+"/Scenes");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Root+"/Scenes/Hud_V02_Test.unity");
            AssetDatabase.Refresh();
        }
        [MenuItem("Anseong Steel/HUD/Open sunwoo HUD Test")]
        public static void BuildSunwooScene()
        {
            const string path="Assets/_Project/04_Scenes/sunwoo_HUDTest.unity";
            // Never overwrite an existing authored scene.
            if(File.Exists(path)) { EditorSceneManager.OpenScene(path); return; }
            EditorSceneManager.OpenScene(Root+"/Scenes/Hud_V02_Test.unity");
            if(!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),path))
                throw new Exception("Could not save sunwoo HUD scene");
            AssetDatabase.Refresh();
        }
        public static void RunSunwoo()
        {
            Directory.CreateDirectory(Output);
            BuildSunwooScene(); SessionState.SetBool(Active,true); EditorApplication.isPlaying=true;
        }
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            BuildTestScene(); SessionState.SetBool(Active,true); EditorApplication.isPlaying=true;
        }
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks.Add("PASS: "+message); }
        static void Tick()
        {
            if(!SessionState.GetBool(Active,false) || !EditorApplication.isPlaying) return;
            if(began<0) { began=EditorApplication.timeSinceStartup; return; }
            if(EditorApplication.timeSinceStartup-began<2) return;
            try
            {
                if(!validated) { Validate(); validated=true; return; }
                Check(cleanupCamera.transform.Find("Helmet HUD - HMD relative")==null,"deferred cleanup removes helmet UI on next frame");
                SessionState.SetBool(Active,false);
                File.WriteAllText(Path.Combine(Output,"hud-checks.txt"),string.Join("\n",checks)+"\nHMD: NOT TESTED\nDAMAGE: TEST DATA ONLY\n");
                Debug.Log("HUD_VALIDATION_PASS "+checks.Count); EditorApplication.isPlaying=false;
                EditorApplication.delayCall+=()=>{ if(Application.isBatchMode) EditorApplication.Exit(0); };
            }
            catch(Exception ex)
            {
                SessionState.SetBool(Active,false);
                File.WriteAllText(Path.Combine(Output,"hud-checks.txt"),string.Join("\n",checks)+"\nFAIL: "+ex);
                Debug.LogException(ex); EditorApplication.isPlaying=false;
                EditorApplication.delayCall+=()=>{ if(Application.isBatchMode) EditorApplication.Exit(2); };
            }
        }
        static void Validate()
        {
            var hud=UnityEngine.Object.FindFirstObjectByType<SuitHudController>();
            Check(hud && hud.Input && hud.Feed,"HUD attached to existing V02 feed and punch input");
            var input=hud.Input; var cam=input.cockpitCamera; var feed=hud.Feed;
            Check(hud.HelmetCanvas.GetComponentsInChildren<SuitHudDetail>(true).Length==4,"reference-sheet line modules present without background plates");
            Check(hud.HelmetCanvas.GetComponentsInChildren<SuitHudDetail>(true)[0].raycastTarget==false,"battle ornaments do not consume pointer input");
            Check(hud.HelmetCanvas.transform.parent==cam.transform,"status anchored to HMD, not display mesh");
            Check(hud.HelmetCanvas.renderMode==RenderMode.WorldSpace,"stereo-compatible world canvas, not screen overlay");
            hud.Wireframe.Rebuild(CanvasUpdate.PreRender); var mesh=hud.Wireframe.canvasRenderer.GetMesh();
            Check(mesh && mesh.vertexCount>100,"wireframe line mesh contains body-part geometry");
            Check((cam.cullingMask&(1<<BattlefieldGuide.GuideLayer))==0,"HMD excludes robot-world guide layer");
            foreach(var c in new[]{feed.exteriorCamera,feed.leftCamera,feed.rightCamera})
                Check((c.cullingMask&(1<<SuitHudController.HelmetLayer))==0 && (c.cullingMask&(1<<BattlefieldGuide.GuideLayer))!=0,"feed includes guide and excludes helmet: "+c.name);
            Check(hud.Guide.Line.useWorldSpace && hud.Guide.transform.parent!=cam.transform,"guide geometry remains in robot world");
            Check(Shader.Find("AnseongSteel/HudWorldLine") && hud.Guide.Line.sharedMaterial.shader.isSupported,"transparent guide shader compiled and supported");
            var state=hud.Damage; state.Reset(); state.AddDamage(SuitPart.LeftArm,.2f); state.AddDamage(SuitPart.LeftArm,.25f);
            Check(Mathf.Abs(state[SuitPart.LeftArm]-.45f)<.00001f,"damage accumulates on selected body part");
            state.AddDamage(SuitPart.LeftArm,-1); state.AddDamage(SuitPart.LeftArm,float.NaN); state.SetDamage(SuitPart.Head,float.PositiveInfinity);
            Check(state[SuitPart.LeftArm]==.45f && state[SuitPart.Head]==0,"negative and non-finite damage ignored");
            state.AddDamage(SuitPart.RightLeg,10); Check(state[SuitPart.RightLeg]==1,"damage saturates without affecting other body parts");
            Check(SuitDamageState.ColorFor(0).b>.9f && SuitDamageState.ColorFor(.45f).g>.7f && SuitDamageState.ColorFor(.85f).g<.3f,"persistent blue / yellow / red thresholds");
            hud.SendMessage("LateUpdate");
            var condition=Array.Find(hud.HelmetCanvas.GetComponentsInChildren<Text>(true),t=>t.name=="Condition value");
            Check(condition.text.Contains("78%") && condition.text.Contains("50%"),"suit readout reflects accumulated damage instead of invented health");
            state.Reset(); Check(state[SuitPart.RightLeg]==0 && state[SuitPart.LeftArm]==0,"explicit damage reset");

            var originalInputSettings=InputSystem.settings;
            var restoreInputSettings=UnityEngine.Object.Instantiate(originalInputSettings);
            restoreInputSettings.hideFlags=HideFlags.DontSave;
            var testInputSettings=UnityEngine.Object.Instantiate(originalInputSettings);
            testInputSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testInputSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings=testInputSettings;
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                hud.SetMenuOpen(false);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape)); InputSystem.Update(); hud.SendMessage("Update");
                Check(hud.MenuOpen,"ESC key opens settings through actual input path");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape)); InputSystem.Update(); hud.SendMessage("Update");
                Check(!hud.MenuOpen,"second ESC key closes settings through actual input path");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                // The Input System may discard its generated default when swapping settings.
                InputSystem.settings=originalInputSettings ? originalInputSettings : restoreInputSettings;
                if(originalInputSettings) UnityEngine.Object.DestroyImmediate(restoreInputSettings);
                UnityEngine.Object.DestroyImmediate(testInputSettings);
            }

            for(int i=0;i<6;i++) { hud.SetMenuOpen(true); Check(!hud.Guide.gameObject.activeSelf,"settings hides tutorial guide"); hud.SetMenuOpen(false); }
            Check(!hud.MenuOpen && hud.HelmetCanvas.GetComponentsInChildren<Canvas>(true).Length==1,"repeated open / close reuses one canvas");
            hud.SetGuideOpacity(2); hud.SetHudScale(.1f); hud.SetGuideEnabled(false); hud.ResetSettings();
            Check(hud.GuideOpacity==.25f && hud.HudScale==1 && hud.GuideEnabled,"settings reset restores defaults");
            hud.SetMenuOpen(true);
            var resume=Array.Find(hud.HelmetCanvas.GetComponentsInChildren<Button>(true),b=>b.GetComponent<Text>().text=="RESUME");
            ExecuteEvents.Execute(resume.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
            Check(!hud.MenuOpen,"resume button pointer event closes menu");
            var opacity=hud.HelmetCanvas.GetComponentsInChildren<Slider>(true)[1]; opacity.value=.42f;
            Check(Mathf.Abs(hud.GuideOpacity-.42f)<.001f,"slider event updates guide opacity"); hud.ResetSettings();
            foreach(var image in hud.HelmetCanvas.GetComponentsInChildren<Image>(true))
                Check(image.rectTransform.sizeDelta.y<=24,"no opaque battle panel: "+image.name);

            // One known robot-world sample shared by both feed views. No HMD projection is used.
            var start=feed.exteriorCamera.transform.TransformPoint(new Vector3(-.65f,-.3f,3.5f));
            var end=feed.exteriorCamera.transform.TransformPoint(new Vector3(.25f,0,4.3f));
            hud.SupplyWorldGuide(new[]{start,Vector3.Lerp(start,end,.5f)+Vector3.up*.1f,end});
            Check(BattlefieldGuide.Project(feed.exteriorCamera,end,out var uv),"sample guide target inside robot feed");
            var points=new Vector3[hud.Guide.Line.positionCount]; hud.Guide.Line.GetPositions(points);
            var wireLocal=hud.Wireframe.rectTransform.localPosition;
            var originalRotation=cam.transform.localRotation;
            cam.transform.localRotation=Quaternion.Euler(0,28,0);
            Check(BattlefieldGuide.Project(feed.exteriorCamera,end,out var turnedUV) && (uv-turnedUV).sqrMagnitude<1e-10f,"head rotation leaves feed target UV unchanged");
            Check(hud.Wireframe.rectTransform.localPosition==wireLocal,"head turn preserves helmet widget location");
            cam.transform.localRotation=originalRotation;
            Capture(hud,"01_normal_left.png");
            input.SwitchPilot();
            Check(BattlefieldGuide.Project(feed.exteriorCamera,end,out var rightUV) && (uv-rightUV).sqrMagnitude<1e-10f,"right pilot references identical shared feed target UV");
            for(int i=0;i<points.Length;i++) Check((hud.Guide.Line.GetPosition(i)-points[i]).sqrMagnitude<1e-10f,"pilot switch preserves path vertex "+i);
            Capture(hud,"02_normal_right.png"); input.SwitchPilot();
            hud.ShowDamageDemo(); float damageBefore=state[SuitPart.RightLeg];
            int events=hud.ObservedPunchCount;
            hud.enabled=false; hud.enabled=true; hud.enabled=false; hud.enabled=true;
            // Real recognizer completion drives the subscribed integration, exactly once.
            var r=input.Recognizer; r.Calibrate(Vector3.zero); float t=0;
            for(int i=0;i<30;i++) { r.Sample(Vector3.zero,Quaternion.identity,t); t+=1f/90; }
            for(int i=1;i<=36;i++) { r.Sample(new Vector3(0,0,.4f*i/36),Quaternion.identity,t); t+=1f/90; }
            for(int i=0;i<24;i++) { r.Sample(new Vector3(0,0,.4f),Quaternion.identity,t); t+=1f/90; }
            Check(hud.ObservedPunchCount==events+1,"re-enable keeps one punch completion subscription");
            Check(state[SuitPart.RightLeg]==damageBefore,"outgoing punch and UI enable do not reset accumulated damage");
            Capture(hud,"03_damage_left.png");
            hud.SetMenuOpen(true); Capture(hud,"04_settings_left.png"); hud.SetMenuOpen(false);
            Check(Time.timeScale==1,"settings does not pause shared combat simulation");
            int originalMask=cam.cullingMask;
            UnityEngine.Object.DestroyImmediate(hud);
            cleanupCamera=cam;
            Check(!cam.transform.Find("Helmet HUD - HMD relative").gameObject.activeSelf,"cleanup immediately hides helmet UI before deferred destruction");
            Check(input.showDebugPanels,"cleanup restores original diagnostics");
        }
        static void Capture(SuitHudController hud,string name)
        {
            hud.SendMessage("LateUpdate");
            var camera=hud.Input.cockpitCamera; float priorAspect=camera.aspect;
            camera.aspect=1.44f; hud.LayoutHelmet(1.44f); Canvas.ForceUpdateCanvases();
            // Refresh feeds first, then the actual authored cockpit HMD camera (URP supported path).
            foreach(var c in new[]{hud.Feed.exteriorCamera,hud.Feed.leftCamera,hud.Feed.rightCamera})
                RenderPipeline.SubmitRenderRequest(c,new UniversalRenderPipeline.SingleCameraRequest { destination=c.targetTexture });
            var rt=new RenderTexture(1440,1000,24,RenderTextureFormat.ARGB32); rt.Create(); var prior=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(hud.Input.cockpitCamera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                RenderTexture.active=rt;
                var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); tex.Apply();
                File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.active=prior; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); camera.aspect=priorAspect; hud.LayoutHelmet(priorAspect); }
        }
    }
}
