using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AnseongSteel.PlayerMotion;
namespace AnseongSteel.Cockpit.Editor
{
    [InitializeOnLoad] public static class CockpitFeedValidation
    {
        static readonly string Out = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Audit/2026-10-14/evidence/cockpit-feed"));
        const string Key = "CockpitFeedValidation.active";
        static CockpitLiveView feed;
        static Camera cockpit;
        static RenderTexture output;
        static Texture2D readback;
        static int step, lastFrame = -1, startFrame, passed, failed;
        static double started;
        static string sceneHash; static bool routedDesktopReceiver;
        static readonly string Scene = "Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity";
        static string Hash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        static CockpitFeedValidation() { EditorApplication.update += Tick; }
        public static void Run()
        {
            SessionState.SetBool(Key + ".optionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Key + ".options", (int)EditorSettings.enterPlayModeOptions);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Directory.CreateDirectory(Out); File.WriteAllText(Out + "/validation.txt", "Actual Unity/URP desktop render; no HMD performance claim.\n");
            sceneHash = Hash(Scene); started = EditorApplication.timeSinceStartup;
            SessionState.SetString(Key + ".hash", sceneHash);
            SessionState.SetString(Key + ".started", started.ToString(System.Globalization.CultureInfo.InvariantCulture));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        static void RestoreOptions()
        {
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + ".options", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + ".optionsEnabled", false);
        }
        static void Check(bool value, string name)
        {
            if (value) passed++; else failed++;
            File.AppendAllText(Out + "/validation.txt", (value ? "PASS " : "FAIL ") + name + "\n");
        }
        static void Setup()
        {
            var holder = new GameObject("Cockpit rendering test");
            cockpit = new GameObject("Cockpit fixture camera").AddComponent<Camera>();
            cockpit.transform.position = new Vector3(0, 0, -3); cockpit.depth = 0; cockpit.cullingMask = 1 << 26;
            output = new RenderTexture(64, 64, 24); output.Create(); cockpit.targetTexture = output;
            readback = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad); screen.layer = 26; screen.transform.localScale = Vector3.one * 5;
            var mat = new Material(Shader.Find("AnseongSteel/Fixed Robot Panorama"));
            mat.SetFloat("_AzimuthSpan", 0); mat.SetFloat("_ElevationMin", 0); mat.SetFloat("_ElevationMax", 0);
            screen.GetComponent<Renderer>().sharedMaterial = mat;
            Camera Feed(string name)
            {
                var camera = new GameObject(name).AddComponent<Camera>(); camera.cullingMask = 0;
                camera.depth = 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.red;
                return camera;
            }
            feed = holder.AddComponent<CockpitLiveView>(); feed.enabled = false;
            feed.exteriorCamera = Feed("Center"); feed.leftCamera = Feed("Left"); feed.rightCamera = Feed("Right");
            feed.cockpitCamera = cockpit; feed.screen = screen.GetComponent<Renderer>();
            feed.centerFeed = new RenderTexture(128, 128, 24) { antiAliasing = 4 };
            feed.leftFeed = new RenderTexture(128, 128, 24) { antiAliasing = 4 };
            feed.rightFeed = new RenderTexture(128, 128, 24) { antiAliasing = 4 };
            feed.centerFeed.Create(); feed.leftFeed.Create(); feed.rightFeed.Create();
            feed.collectTiming = true; feed.enabled = true;
            RenderPipelineManager.endCameraRendering += Capture;
            startFrame = Time.frameCount;
            for (int i = 0; i < 3; i++)
            {
                Check(feed.ActiveFeed(i).antiAliasing == 1 && feed.ActiveFeed(i).width == 128, "runtime single sample keeps resolution " + i);
                var c = i == 0 ? feed.exteriorCamera : i == 1 ? feed.leftCamera : feed.rightCamera;
                Check(c.depth < cockpit.depth && !c.GetUniversalAdditionalCameraData().allowXRRendering, "feed order and explicit mono " + i);
            }
        }
        static void Capture(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cockpit || !feed || step > 3) return;
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = output; readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                var pixel = readback.GetPixel(32, 32); var expected = feed.exteriorCamera.backgroundColor;
                Check(expected.r > .5 ? pixel.r > .8 && pixel.g < .2 : pixel.g > .8 && pixel.r < .2,
                    "same-frame color pixel frame=" + Time.frameCount + " mode=" + feed.sampling + " pixel=" + pixel);
            }
            finally { RenderTexture.active = previous; }
        }
        const string MainKey = "CockpitFeedValidation.main";
        [MenuItem("Anseong Steel/Cockpit/Verify saved feed A-B (no settings changes)")]
        public static void RunMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorSceneManager.GetActiveScene().path != Scene || EditorSceneManager.GetActiveScene().isDirty)
            { Debug.LogError("Cockpit check needs the saved integrated scene, stopped and clean."); return; }
            Directory.CreateDirectory(Out);
            File.WriteAllText(Out + "/main-validation.txt", "Main saved scene automatic desktop rendering, no HMD claim. No settings changes or pixel readback.\n");
            SessionState.SetString(MainKey + ".hash", Hash(Scene));
            SessionState.SetInt(MainKey + ".stage", 1);
            SessionState.SetString(MainKey + ".start", EditorApplication.timeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
            EditorApplication.isPlaying = true;
        }
        static void MainCheck(bool ok, string name)
        {
            File.AppendAllText(Out + "/main-validation.txt", (ok ? "PASS " : "FAIL ") + name + "\n");
            if (!ok) SessionState.SetBool(MainKey + ".failed", true);
        }
        static void TickMain()
        {
            int stage = SessionState.GetInt(MainKey + ".stage", 0);
            if (stage == 0) return;
            try
            {
                if (stage == 5 && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    MainCheck(Hash(Scene) == SessionState.GetString(MainKey + ".hash", ""), "saved scene bytes unchanged");
                    MainCheck(Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t.name.EndsWith(" (runtime single sample)")) == 0, "runtime targets destroyed after Play");
                    SessionState.SetInt(MainKey + ".stage", 0);
                    Debug.Log("Cockpit main A/B check finished. See " + Out + "/main-validation.txt"); return;
                }
                double.TryParse(SessionState.GetString(MainKey + ".start", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double begin);
                if (EditorApplication.timeSinceStartup - begin > 180) throw new TimeoutException("main feed check timeout");
                if (!EditorApplication.isPlaying || Time.frameCount < 15) return;
                EditorApplication.QueuePlayerLoopUpdate();
                var live = UnityEngine.Object.FindFirstObjectByType<CockpitLiveView>();
                if (!live) throw new Exception("saved live feed missing");
                if (stage == 1)
                {
                    SessionState.SetBool(MainKey + ".failed", false);
                    live.collectTiming = true;
                    MainCheck(live.sampling == CockpitLiveView.Sampling.FullResolutionSingleSample, "default runtime 1x");
                    live.SetSampling(CockpitLiveView.Sampling.AuthoredTextures);
                    SessionState.SetInt(MainKey + ".frame", Time.frameCount); SessionState.SetInt(MainKey + ".stage", 2); return;
                }
                if (Time.frameCount - SessionState.GetInt(MainKey + ".frame", Time.frameCount) < 12) return;
                for (int i = 0; i < 3; i++)
                    MainCheck(live.ActiveFeed(i).width == 2048 && live.ActiveFeed(i).antiAliasing == (stage == 2 ? 4 : 1), "actual sector " + i + " sampling " + live.sampling);
                MainCheck(live.CockpitSamples > 0 && live.StaleFeedSamples == 0, "same-frame automatic receiver " + live.sampling);
                if (stage == 2)
                {
                    live.SetSampling(CockpitLiveView.Sampling.FullResolutionSingleSample);
                    SessionState.SetInt(MainKey + ".frame", Time.frameCount); SessionState.SetInt(MainKey + ".stage", 3); return;
                }
                var retained = new[] { live.ActiveFeed(0), live.ActiveFeed(1), live.ActiveFeed(2) };
                int made = live.RuntimeTexturesCreated;
                for (int i = 0; i < 64; i++) { live.SetSampling(CockpitLiveView.Sampling.AuthoredTextures); live.SetSampling(CockpitLiveView.Sampling.FullResolutionSingleSample); }
                File.AppendAllText(Out + "/main-validation.txt", "runtime created before/after=" + made + "/" + live.RuntimeTexturesCreated + "\n");
                MainCheck(live.RuntimeTexturesCreated == made && retained.Distinct().Count() == 3 && Enumerable.Range(0,3).All(i => retained[i] == live.ActiveFeed(i)), "64 A/B cycles reuse only three active runtime targets" );
                live.collectTiming = false;
                SessionState.SetInt(MainKey + ".stage", 5); EditorApplication.isPlaying = false;
            }
            catch (Exception e)
            {
                File.AppendAllText(Out + "/main-validation.txt", "ERROR " + e + "\n");
                SessionState.SetInt(MainKey + ".stage", 5);
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            }
        }
        static void Tick()
        {
            TickMain();
            if (!SessionState.GetBool(Key, false)) return;
            if (started == 0) { double.TryParse(SessionState.GetString(Key + ".started", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out started); sceneHash = SessionState.GetString(Key + ".hash", ""); }
            try
            {
                if (step == 4 && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorSceneManager.OpenScene(Scene);
                    startFrame = -1; step = 5; EditorApplication.isPlaying = true; return;
                }
                if (step == 6 && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Check(Hash(Scene) == sceneHash, "saved integrated scene bytes unchanged");
                    File.AppendAllText(Out + "/validation.txt", "TOTAL PASS=" + passed + " FAIL=" + failed + "\n");
                    RestoreOptions(); SessionState.SetBool(Key, false); EditorApplication.Exit(failed == 0 ? 0 : 1); return;
                }
                if (EditorApplication.timeSinceStartup - started > 220) throw new TimeoutException("fixture timeout");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount < 10 || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (step == 5)
                {
                    if (startFrame < 0)
                    {
                        feed = UnityEngine.Object.FindFirstObjectByType<CockpitLiveView>();
                        Check(feed && feed.cockpitCamera, "saved scene live feed connected");
                        if (!feed) throw new Exception("saved scene feed missing");
                        feed.collectTiming = true; startFrame = Time.frameCount; return;
                    }
                    if (Time.frameCount - startFrame < 12) return;
                    if (feed.CockpitSamples == 0 && !routedDesktopReceiver)
                    {
                        File.AppendAllText(Out + "/validation.txt", "N/A batch has no HMD/Game screen receiver submissions; use temporary desktop RT receiver.\n");
                        output = new RenderTexture(1000, 700, 24); output.Create();
                        feed.cockpitCamera.targetTexture = output; routedDesktopReceiver = true;
                        startFrame = Time.frameCount; return;
                    }
                    Check(feed.CockpitSamples > 0 && feed.StaleFeedSamples == 0, "saved integrated automatic desktop RT receiver order");
                    File.AppendAllText(Out + "/validation.txt", "saved cockpit submissions=" + feed.CockpitSamples + " stale=" + feed.StaleFeedSamples + "\n");
                    var previous = RenderTexture.active;
                    if (output)
                    {
                        RenderTexture.active = output;
                        var image = new Texture2D(1000,700,TextureFormat.RGB24,false);
                        image.ReadPixels(new Rect(0,0,1000,700),0,0); image.Apply();
                        File.WriteAllBytes(Out + "/saved-desktop-receiver.png", image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = previous;
                    }
                    Check(feed.FeedSubmissions.All(v => v > 0), "saved integrated all sectors updated");
                    for (int i = 0; i < 3; i++) Check(feed.ActiveFeed(i).width == 2048 && feed.ActiveFeed(i).antiAliasing == 1, "saved full-resolution runtime feed " + i);
                    File.AppendAllText(Out + "/validation.txt", "saved intervals ms=" + string.Join(",", feed.FeedIntervalMs) + " CPU submissions ms=" + string.Join(",", feed.FeedCpuSubmissionMs) + " beforeRender samples=" + feed.BeforeRenderSamples + "\n");
                    EditorApplication.isPlaying = false; step = 6; return;
                }
                if (step == 0) { Setup(); step = 1; return; }
                if (step < 4)
                {
                    // Change every game frame so a stale feed cannot pass with a static picture.
                    var color = Time.frameCount % 2 == 0 ? Color.red : Color.green;
                    feed.exteriorCamera.backgroundColor = feed.leftCamera.backgroundColor = feed.rightCamera.backgroundColor = color;
                    if (Time.frameCount - startFrame < 8) return;
                    Check(feed.StaleFeedSamples == 0 && feed.CockpitSamples > 0, "all three feed callbacks complete before cockpit " + feed.sampling);
                    Check(feed.FeedSubmissions.All(v => v > 0), "every sector renders " + feed.sampling);
                    if (step == 1)
                    {
                        feed.SetSampling(CockpitLiveView.Sampling.AuthoredTextures);
                        Check(feed.ActiveFeed(0) == feed.centerFeed && feed.centerFeed.antiAliasing == 4, "authored MSAA A/B restores source binding");
                    }
                    else if (step == 2)
                    {
                        feed.SetSampling(CockpitLiveView.Sampling.FullResolutionSingleSample);
                        Check(feed.ActiveFeed(0) != feed.AuthoredFeed(0) && feed.ActiveFeed(0).antiAliasing == 1, "A/B returns to single sample");
                    }
                    else
                    {
                        var retained = new[] { feed.ActiveFeed(0), feed.ActiveFeed(1), feed.ActiveFeed(2) };
                        int created = feed.RuntimeTexturesCreated;
                        for (int n = 0; n < 64; n++)
                        {
                            feed.SetSampling(CockpitLiveView.Sampling.AuthoredTextures);
                            feed.SetSampling(CockpitLiveView.Sampling.FullResolutionSingleSample);
                        }
                        Check(feed.RuntimeTexturesCreated == created && created == 3, "64 A/B cycles reuse three allocations");
                        Check(Enumerable.Range(0,3).All(i => feed.ActiveFeed(i) == retained[i]), "A/B texture identities stable");
                        feed.enabled = false;
                        Check(retained.All(t => !t.IsCreated()), "disable releases all cached GPU targets");
                        Check(feed.exteriorCamera.targetTexture == null && feed.exteriorCamera.depth == 5, "disable restores original target/depth");
                        Check(feed.exteriorCamera.GetUniversalAdditionalCameraData().allowXRRendering, "disable restores original URP XR flag");
                        Check(feed.centerFeed.antiAliasing == 4 && feed.centerFeed.width == 128, "authored RT never mutated");
                        RenderPipelineManager.endCameraRendering -= Capture;
                        EditorApplication.isPlaying = false; step = 4; return;
                    }
                    step++; startFrame = Time.frameCount;
                }
            }
            catch (Exception e)
            {
                File.AppendAllText(Out + "/validation.txt", "ERROR " + e + "\n"); RestoreOptions(); SessionState.SetBool(Key, false); EditorApplication.Exit(2);
            }
        }
    }
}

namespace AnseongSteel.Cockpit.Editor
{
    [CustomEditor(typeof(CockpitLiveView))]
    public sealed class CockpitFeedInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "sampling");
            serializedObject.ApplyModifiedProperties();
            var feed = (CockpitLiveView)target;
            var chosen = (CockpitLiveView.Sampling)EditorGUILayout.EnumPopup("Requested sampling", feed.sampling);
            if (chosen != feed.sampling)
            {
                if (Application.isPlaying) feed.SetSampling(chosen);
                else { Undo.RecordObject(feed, "Cockpit sampling"); feed.sampling = chosen; EditorUtility.SetDirty(feed); }
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !feed.isActiveAndEnabled))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("A / authored 4x")) feed.SetSampling(CockpitLiveView.Sampling.AuthoredTextures);
                if (GUILayout.Button("B / runtime 1x")) feed.SetSampling(CockpitLiveView.Sampling.FullResolutionSingleSample);
                EditorGUILayout.EndHorizontal();
            }
            for (int i = 0; i < 3; i++)
            {
                var texture = feed.ActiveFeed(i);
                EditorGUILayout.LabelField("Actual sector " + i, texture ? texture.width + " x " + texture.height + " / MSAA " + texture.antiAliasing : "inactive");
            }
            EditorGUILayout.LabelField("Runtime targets created this instance", feed.RuntimeTexturesCreated.ToString());
            EditorGUILayout.HelpBox("PC operator controls only. No new HMD keys or XR ray UI. Performance A/B: do not run the pixel validation; discard switch warm-up frames.", MessageType.Info);
        }
    }
}
