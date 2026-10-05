using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnseongSteel.PlayerMotion.Editor
{
    [InitializeOnLoad]
    public static class ArmMotionValidation
    {
        const string Active = "ArmMotion.Validate", Finished = "ArmMotion.Finished", Report = "ArmMotion.Report";
        static readonly List<string> checks = new List<string>();
        static int stage; static float at; static double start;
        static ArmMotionInput input; static CockpitViewPosition views;
        static XRHMD hmd; static XRController left, right;
        static InputSettings original, temporary;
        static Vector3 frozen, other;
        static ArmMotionValidation() { EditorApplication.update += Tick; start = EditorApplication.timeSinceStartup; }
        public static void Run()
        {
            Directory.CreateDirectory("ArmMotionValidation");
            try { Core(); File.WriteAllText("ArmMotionValidation/core-checks.txt", string.Join("\n", checks)); }
            catch (Exception e) { File.WriteAllText("ArmMotionValidation/core-checks.txt", string.Join("\n", checks) + "\nFAIL: " + e); throw; }
            ArmMotionSceneBuilder.Build();
            SessionState.SetBool(Active, true); SessionState.SetBool(Finished, false); EditorApplication.isPlaying = true;
        }
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("PASS: " + message); }
        static void Core()
        {
            foreach (int hz in new[] { 30, 60, 90, 120 }) foreach (bool l in new[] { true, false })
            {
                Test(hz, l, u => new Vector3(0, 0, .4f * u), ObservedPunch.Jab, "straight jab");
                Test(hz, l, u => new Vector3((l ? 1 : -1) * (u < .35f ? -.23f * u / .35f : -.23f + .46f * (1 - Mathf.Cos((u - .35f) / .65f * Mathf.PI)) / 2), 0, .4f * Mathf.Sin(u * Mathf.PI / 2)), ObservedPunch.Hook, "curved hook");
                Test(hz, l, u => new Vector3(.4f * u, 0, .3f * u), ObservedPunch.Unknown, "diagonal rejection");
            }
            var r = new PunchTrajectoryRecognizer(true, new TrajectoryTuning()); r.Calibrate(Vector3.zero);
            r.Sample(Vector3.zero, Quaternion.identity, 0); r.Sample(Vector3.zero, Quaternion.identity, .3f);
            Check(r.Phase == MotionPhase.TrackingLost && r.CompletedCount == 0, "tracking gap cancels without attack");
            r.Calibrate(Vector3.zero); r.Sample(new Vector3(float.NaN, 0, 0), Quaternion.identity, 1);
            Check(r.Phase == MotionPhase.TrackingLost, "NaN pose rejected");
        }
        static void Test(int hz, bool leftSide, Func<float, Vector3> path, ObservedPunch expected, string label)
        {
            var r = new PunchTrajectoryRecognizer(leftSide, new TrajectoryTuning()); r.Calibrate(Vector3.zero);
            float t = 0, dt = 1f / hz;
            void Sample(Vector3 p) { r.Sample(p, Quaternion.identity, t); t += dt; }
            for (int i = 0; i < hz / 2; i++) Sample(Vector3.zero);
            int n = Mathf.RoundToInt(.4f * hz);
            for (int i = 1; i <= n; i++) Sample(path((float)i / n));
            for (int i = 0; i < hz / 5; i++) Sample(path(1));
            Check(r.CompletedCount == 1 && r.LastCompleted.kind == expected, $"{label} {hz}Hz {(leftSide ? "L" : "R")}: {r.LastCompleted.kind}, {r.LastCompleted.reason}, arc {r.LastCompleted.arcRatio}");
            for (int i = 1; i <= n; i++) Sample(Vector3.Lerp(path(1), Vector3.zero, (float)i / n));
            for (int i = 0; i < hz / 3; i++) Sample(Vector3.zero);
            Check(r.CompletedCount == 1 && r.Phase == MotionPhase.Ready, "retraction creates no duplicate and rearms at neutral");
        }
        static void Next() { stage++; at = Time.unscaledTime; }
        static void Pose(Vector3 delta, int state = 3, float yaw = 0)
        {
            InputSystem.QueueDeltaStateEvent(hmd.centerEyePosition, new Vector3(0, 1.65f, 0));
            InputSystem.QueueDeltaStateEvent(hmd.centerEyeRotation, Quaternion.Euler(0, yaw, 0));
            InputSystem.QueueDeltaStateEvent(hmd.trackingState, 3);
            InputSystem.QueueDeltaStateEvent(left.devicePosition, new Vector3(-.25f, 1.3f, .3f) + delta);
            InputSystem.QueueDeltaStateEvent(left.deviceRotation, Quaternion.identity);
            InputSystem.QueueDeltaStateEvent(left.trackingState, state);
            InputSystem.QueueDeltaStateEvent(right.devicePosition, new Vector3(.25f, 1.3f, .3f));
            InputSystem.QueueDeltaStateEvent(right.deviceRotation, Quaternion.identity);
            InputSystem.QueueDeltaStateEvent(right.trackingState, 3);
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            try
            {
                if (SessionState.GetBool(Finished, false))
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    string report = SessionState.GetString(Report, ""); File.WriteAllText("ArmMotionValidation/play-checks.txt", report);
                    SessionState.SetBool(Active, false); Debug.Log(report); EditorApplication.Exit(report.Contains("FAIL:") ? 1 : 0); return;
                }
                if (EditorApplication.timeSinceStartup - start > 180) throw new Exception("Validation timeout");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount < 15) return;
                EditorApplication.QueuePlayerLoopUpdate(); float elapsed = Time.unscaledTime - at;
                switch (stage)
                {
                    case 0:
                        input = UnityEngine.Object.FindFirstObjectByType<ArmMotionInput>(); views = UnityEngine.Object.FindFirstObjectByType<CockpitViewPosition>();
                        // Editor shader compilation can stall a synthetic stream. Gap rejection
                        // is tested separately in Core at the shipping threshold of 150 ms.
                        input.recognition.maximumSampleGap = 1f;
                        Check(input && views && input.leftArm.hand && views.chest, "scene references and view anchors loaded");
                        Check(views.head.position.y > views.neck.position.y && views.neck.position.y > views.chest.position.y, "head > neck > chest camera heights");
                        Check(views.head.localPosition.y < 3 && views.chest.localPosition.y > .5f, "view anchors match six metre robot rather than cached skin bounds");
                        var skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(s => s.enabled).ToArray();
                        Check(skins.Length == 2 && skins[0].sharedMesh == skins[1].sharedMesh && skins[0].transform.lossyScale == skins[1].transform.lossyScale, "boss and player share exact mesh and scale");
                        Check(skins.Any(s => s.sharedMaterials.Any(m => m.name.Contains("Player_Blue"))) && skins.Any(s => s.sharedMaterials.Any(m => m.name.Contains("Boss_Red"))), "separate blue player and red boss materials");
                        original = InputSystem.settings; temporary = UnityEngine.Object.Instantiate(original);
                        temporary.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        temporary.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = temporary; Application.runInBackground = true;
                        hmd = InputSystem.AddDevice<XRHMD>(); left = InputSystem.AddDevice<XRController>(); right = InputSystem.AddDevice<XRController>();
                        InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand); InputSystem.SetDeviceUsage(right, CommonUsages.RightHand);
                        Pose(Vector3.zero); Next(); break;
                    case 1:
                        Pose(Vector3.zero); if (elapsed > .3f) { input.RequestCalibration(); Next(); } break;
                    case 2:
                        Pose(Vector3.zero); if (elapsed > .9f) { Check(input.Calibrated, "synthetic XR InputActions calibrate through runtime path"); other = input.rightArm.hand.position; Next(); } break;
                    case 3:
                        Pose(new Vector3(0, 0, .25f * Mathf.Clamp01(elapsed / .4f)));
                        if (elapsed > .7f) { Check(input.Calibrated && input.LastControllerDelta.z > .24f && input.leftArm.TargetError < .002f, $"real input binding drives bone IK: delta={input.LastControllerDelta.z:F3}, error={input.leftArm.TargetError:F5}, {input.Status}"); Check(Vector3.Distance(other, input.rightArm.hand.position) < .001f, "unassigned hand remains still"); Next(); } break;
                    case 4:
                        Pose(new Vector3(0, 0, .25f), 0, 35);
                        if (elapsed > .3f) { Check(!input.Calibrated && !input.TrackingValid, "tracking loss requires recalibration"); frozen = input.leftArm.hand.position; Next(); } break;
                    case 5:
                        Pose(new Vector3(.1f, 0, .25f), 0, 55);
                        if (elapsed > .3f)
                        {
                            Check(Vector3.Distance(frozen, input.leftArm.hand.position) < .001f, "lost hand freezes pose");
                            Check(Quaternion.Angle(input.cockpitCamera.transform.localRotation, Quaternion.Euler(0, 55, 0)) < 1, "HMD continues while hand tracking is lost");
                            Check(Quaternion.Angle(views.feedCamera.transform.rotation, views.Current.rotation) < .01f && Vector3.Distance(views.feedCamera.transform.position, views.Current.position) < .001f,
                                "pilot turns head 55 degrees while robot optical mount stays fixed");
                            // Evaluate the actual default guard, not the extended synthetic punch.
                            input.leftArm.Follow(Vector3.zero, Quaternion.identity); input.rightArm.Follow(Vector3.zero, Quaternion.identity);
                            views.Select(CockpitViewPosition.Mount.Head); Next();
                        } break;
                    case 6:
                        Pose(Vector3.zero, 0, 0);
                        if (elapsed > .3f) { GuardVisible(); Capture("head-feed.png", views.feedCamera); Capture("cockpit.png", input.cockpitCamera); views.Select(CockpitViewPosition.Mount.Neck); Next(); } break;
                    case 7:
                        if (elapsed > .3f) { GuardVisible(); Capture("neck-feed.png", views.feedCamera); Capture("neck-cockpit.png", input.cockpitCamera); views.Select(CockpitViewPosition.Mount.Chest); Next(); } break;
                    case 8:
                        if (elapsed > .3f)
                        {
                            GuardVisible(); Capture("chest-feed.png", views.feedCamera); Capture("chest-cockpit.png", input.cockpitCamera); Check(Vector3.Distance(views.feedCamera.transform.position, views.chest.position) < .001f, "camera switches to chest anchor");
                            var overview = new GameObject("Validation overview").AddComponent<Camera>(); overview.transform.position = new Vector3(10, 4, 103.5f);
                            overview.transform.LookAt(new Vector3(0, 0, 103.5f)); overview.fieldOfView = 65; overview.farClipPlane = 40;
                            Capture("matched-models.png", overview); UnityEngine.Object.Destroy(overview.gameObject); Complete();
                        } break;
                }
            }
            catch (Exception e) { checks.Add("FAIL: " + e); Complete(); }
        }
        static void GuardVisible()
        {
            foreach (var arm in new[] { input.leftArm, input.rightArm })
            {
                var p = views.feedCamera.WorldToViewportPoint(arm.hand.position);
                Check(p.z > .1f && p.x > .1f && p.x < .9f && p.y > .12f && p.y < .9f,
                    $"{views.selected} default guard wrist inside view: {p}");
            }
            var view = views.GetComponent<CockpitLiveView>();
            Check(view.centerFeed.width == 2048 && view.leftFeed.width == 2048 && view.rightFeed.width == 2048, "three high-density panorama sectors, 2048 pixels each");
            Check(input.cockpitCamera.GetUniversalAdditionalCameraData().cameraStack.Count == 0, "independent cockpit camera; no coupled world overlay");
            Check(!view.playerBody.forceRenderingOff && !view.firstPersonArms.enabled, "full model restored outside feed rendering");
        }
        static void Capture(string name, Camera camera)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) throw new Exception("Graphics required for feed validation");
            bool cockpit = camera == input.cockpitCamera;
            var active = RenderTexture.active; var rt = new RenderTexture(1280, 640, 24);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt;
            var texture = new Texture2D(1280, 640, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1280, 640), 0, 0); texture.Apply();
            File.WriteAllBytes("ArmMotionValidation/" + name, texture.EncodeToPNG());
            var colors = texture.GetPixels32(); Check(colors.Select(c => (int)c.r + c.g * 256 + c.b * 65536).Distinct().Take(100).Count() >= 100, name + " contains rendered geometry");
            if (cockpit)
            {
                int leftBlue = 0, rightBlue = 0;
                for (int i = 0; i < colors.Length; i++)
                { var c = colors[i]; if (c.b > 45 && c.b > c.r * 1.35f && c.b > c.g * 1.25f) { if (i % 1280 < 640) leftBlue++; else rightBlue++; } }
                Check(leftBlue > 500 && rightBlue > 500, $"{name} visible blue arms on BOTH sides: {leftBlue}/{rightBlue} pixels");
            }
            RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt);
        }
        static void Complete()
        {
            foreach (var device in new InputDevice[] { hmd, left, right }) if (device != null) InputSystem.RemoveDevice(device);
            if (original) { InputSystem.settings = original; UnityEngine.Object.DestroyImmediate(temporary); }
            SessionState.SetString(Report, string.Join("\n", checks)); SessionState.SetBool(Finished, true); EditorApplication.isPlaying = false;
        }
    }
}
