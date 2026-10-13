using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnseongSteel.PlayerMotion.Editor
{
    [InitializeOnLoad]
    public static class ArmMotionValidation
    {
        const string Active = "ArmMotion.Validate", Finished = "ArmMotion.Finished", Report = "ArmMotion.Report";
        static readonly List<string> checks = new List<string>();
        static int stage, sampledFrame = -1; static float at; static double start;
        static ArmMotionInput input; static CockpitViewPosition views;
        static XRHMD hmd; static XRController left, right, untrackedDuplicate;
        static readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        static InputSettings original, temporary;
        static Vector3 frozen, other, frozenPilot;
        static int contactCount;
        static Vector3 guardElbow, guardWrist, pilotGuardElbow, pilotGuardWrist;
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
            r.DiscardInterruptedStroke();
            Check(r.Phase == MotionPhase.WaitingForNeutral && r.CompletedCount == 0, "discarding interrupted stroke requires neutral without an attack");
            var shoulder = Vector3.zero; var ready = new Vector3(0, 0, .6f);
            foreach (float gain in new[] { .2f, 1f, 3f })
            {
                var lastPoint = ready; bool continuous = true;
                for (int i = 1; i <= 100; i++)
                {
                    var mapped = ControllerArmFollower.MapWorkspace(shoulder, ready, new Vector3(0, 0, i * .01f), 1, gain, 1);
                    continuous &= mapped.z > lastPoint.z && mapped.z - lastPoint.z <= Mathf.Max(1, gain) * .01001f && mapped.z < .995f;
                    lastPoint = mapped;
                }
                Check(continuous, $"soft reach remains monotonic across guard and reach boundary at gain {gain}");
            }
            Check(Vector3.Distance(ControllerArmFollower.MapWorkspace(shoulder, ready, new Vector3(.1f, 0, -.2f), 1, .4f, 1), new Vector3(.1f, 0, .4f)) < .00001f,
                "rearward travel is not reduced by forward reach calibration");
            var hint = new ControllerElbowHint();
            for (int i = 0; i < 60; i++) hint.Update(new Vector3(0, 0, i / 60f * .35f), 1f / 90f);
            Check(hint.Lift < .01f, "straight jab keeps elbow tucked without wrist-rotation cues");
            for (int i = 0; i < 60; i++) hint.Update(new Vector3(i / 60f * .23f, 0, .12f), 1f / 90f);
            Check(hint.Lift > .8f, "lateral hook trajectory raises elbow without wrist input");
            for (int i = 0; i < 180; i++) hint.Update(Vector3.zero, 1f / 90f);
            Check(hint.Lift < .01f, "elbow hint relaxes back to guard");
            Check(!ControllerArmFollower.TryReachScale(Vector3.zero, Vector3.forward, 2, Vector3.zero, out _), "reach calibration rejects unextended hand");
            Check(RobotPunchContactTest.SweptContact(new Vector3(0, 0, -2), new Vector3(0, 0, 2), Vector3.one * .4f, .1f), "fast swept punch cannot tunnel through contact pad");
            Check(!RobotPunchContactTest.SweptContact(new Vector3(2, 0, -2), new Vector3(2, 0, 2), Vector3.one * .4f, .1f), "swept punch missing pad produces no hit");
            Check(!RobotPunchContactTest.SweptContact(Vector3.zero, new Vector3(0, 0, .1f), Vector3.one * .4f, .1f), "holding inside pad does not repeatedly hit");
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
        static void Pose(Vector3 delta, int state = 3, float yaw = 0, bool rightSide = false)
        {
            InputSystem.QueueDeltaStateEvent(hmd.centerEyePosition, new Vector3(0, 1.65f, 0));
            InputSystem.QueueDeltaStateEvent(hmd.centerEyeRotation, Quaternion.Euler(0, yaw, 0));
            InputSystem.QueueDeltaStateEvent(hmd.trackingState, 3);
            InputSystem.QueueDeltaStateEvent(left.devicePosition, new Vector3(-.25f, 1.3f, .3f) + (rightSide ? Vector3.zero : delta));
            InputSystem.QueueDeltaStateEvent(left.deviceRotation, Quaternion.identity);
            InputSystem.QueueDeltaStateEvent(left.trackingState, state);
            InputSystem.QueueDeltaStateEvent(right.devicePosition, new Vector3(.25f, 1.3f, .3f) + (rightSide ? delta : Vector3.zero));
            InputSystem.QueueDeltaStateEvent(right.deviceRotation, Quaternion.identity);
            InputSystem.QueueDeltaStateEvent(right.trackingState, 3);
            if (untrackedDuplicate != null)
            {
                InputSystem.QueueDeltaStateEvent(untrackedDuplicate.devicePosition, Vector3.one * 5);
                InputSystem.QueueDeltaStateEvent(untrackedDuplicate.deviceRotation, Quaternion.Euler(80, 120, 45));
                InputSystem.QueueDeltaStateEvent(untrackedDuplicate.trackingState, 0);
            }
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
                EditorApplication.QueuePlayerLoopUpdate();
                // Feed one synthetic pose per player frame, even if the editor ticks
                // repeatedly during rendering/import work. Otherwise a queued jump can
                // be delivered on a tiny subsequent frame and resemble tracking loss.
                if (sampledFrame == Time.frameCount) return;
                sampledFrame = Time.frameCount;
                float elapsed = Time.unscaledTime - at;
                // EditorApplication callbacks read the editor state buffer by default.
                // Select/process the player buffer before inspecting raw XR controls.
                if (stage > 0) InputSystem.Update();
                switch (stage)
                {
                    case 0:
                        input = UnityEngine.Object.FindFirstObjectByType<ArmMotionInput>(); views = UnityEngine.Object.FindFirstObjectByType<CockpitViewPosition>();
                        // Use the shipping gap threshold: render stalls must not disable arm tracking.
                        Check(input && views && input.leftArm.hand && views.chest, "scene references and view anchors loaded");
                        Check(views.head.position.y > views.neck.position.y && views.neck.position.y > views.chest.position.y, "head > neck > chest camera heights");
                        Check(views.head.localPosition.y < 3 && views.chest.localPosition.y > .5f, "view anchors match six metre robot rather than cached skin bounds");
                        var skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(s => s.enabled && !s.GetComponentInParent<CockpitPilotAvatar>()).ToArray();
                        Check(skins.Length == 2 && skins[0].sharedMesh == skins[1].sharedMesh && skins[0].transform.lossyScale == skins[1].transform.lossyScale, "boss and player share exact mesh and scale");
                        Check(skins.Any(s => s.sharedMaterials.Any(m => m.name.Contains("Player_Blue"))) && skins.Any(s => s.sharedMaterials.Any(m => m.name.Contains("Boss_Red"))), "separate blue player and red boss materials");
                        ValidateFingers(skins.First(s => input.leftArm.upper.IsChildOf(s.transform.parent)));
                        ValidateFists("before calibration");
                        ValidatePilotView();
                        ValidateGuardAlignment();
                        original = InputSystem.settings; temporary = UnityEngine.Object.Instantiate(original);
                        temporary.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        temporary.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = temporary; Application.runInBackground = true;
                        foreach (var device in InputSystem.devices.Where(d => d is XRHMD || d is XRController).ToArray())
                            if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
                        InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>();
                        hmd = InputSystem.AddDevice<XRHMD>();
                        left = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
                        right = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
                        untrackedDuplicate = InputSystem.AddDevice<XRController>();
                        InputSystem.SetDeviceUsage(untrackedDuplicate, CommonUsages.LeftHand);
                        InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand); InputSystem.SetDeviceUsage(right, CommonUsages.RightHand);
                        Pose(Vector3.zero); Next(); break;
                    case 1:
                        Pose(Vector3.zero); if (elapsed > .3f) { Next(); } break;
                    case 2:
                        Pose(Vector3.zero); if (elapsed > 1.2f) { Check(input.Calibrated, "Oculus Touch layout automatically calibrates without Y/C, despite untracked duplicate device"); other = input.rightArm.hand.position; Next(); } break;
                    case 3:
                        Pose(new Vector3(0, 0, .25f * Mathf.Clamp01(elapsed / .4f)));
                        if (elapsed > .7f) { Check(input.Calibrated && input.LastControllerDelta.z > .24f && input.leftArm.TargetError < .002f, $"real input binding drives bone IK: delta={input.LastControllerDelta.z:F3}, error={input.leftArm.TargetError:F5}, {input.Status}"); Check(Vector3.Distance(other, input.rightArm.hand.position) < .001f, "unassigned hand remains still"); ValidatePilotMotion(true); Next(); } break;
                    case 4:
                        Pose(new Vector3(0, 0, .25f), 0, 35);
                        if (elapsed > .3f) { Check(!input.Calibrated && !input.TrackingValid, "tracking loss requires recalibration"); frozen = input.leftArm.hand.position; frozenPilot = input.Avatar.rig.left.hand.position; Next(); } break;
                    case 5:
                        Pose(new Vector3(.1f, 0, .25f), 0, 55);
                        if (elapsed > .3f)
                        {
                            Check(Vector3.Distance(frozen, input.leftArm.hand.position) < .001f, "lost hand freezes pose");
                            Check(Quaternion.Angle(input.cockpitCamera.transform.localRotation, Quaternion.Euler(0, 55, 0)) < 1, "HMD continues while hand tracking is lost");
                            Check(Quaternion.Angle(views.feedCamera.transform.rotation, views.Current.rotation) < .01f && Vector3.Distance(views.feedCamera.transform.position, views.Current.position) < .001f,
                                "pilot turns head 55 degrees while robot optical mount stays fixed");
                            ValidateFists("after tracking loss");
                            Check(Vector3.Distance(frozenPilot, input.Avatar.rig.left.hand.position) < .001f, "pilot hand also freezes on tracking loss");
                            input.Directions.Refresh(); Check(!input.Directions.controllers[0].enabled && input.Directions.controllers[1].enabled,
                                $"lost controller arrow hidden while tracked other-hand arrow stays visible: arrows={input.Directions.controllers[0].enabled}/{input.Directions.controllers[1].enabled}, show={input.showHandDirections}, {input.InputStatus}, raw={input.ControllerInCockpit(false, out var rawP, out _)}, pos={rawP}");
                            ValidatePilotView();
                            // Evaluate the actual default guard, not the extended synthetic punch.
                            input.leftArm.Follow(Vector3.zero, Quaternion.identity); input.rightArm.Follow(Vector3.zero, Quaternion.identity);
                            views.Select(CockpitViewPosition.Mount.Head); Next();
                        } break;
                    case 6:
                        Pose(Vector3.zero, 0, 0);
                        if (elapsed > .3f) { GuardVisible(); Capture("head-feed.png", views.feedCamera); Capture("cockpit.png", input.cockpitCamera);
                            input.cockpitCamera.transform.localRotation = Quaternion.Euler(38, 0, 0); input.Avatar.Evaluate();
                            Capture("pilot-look-down.png", input.cockpitCamera);
                            input.cockpitCamera.transform.localRotation = Quaternion.identity; input.Avatar.Evaluate();
                            views.Select(CockpitViewPosition.Mount.Neck); Next(); } break;
                    case 7:
                        if (elapsed > .3f) { GuardVisible(); Capture("neck-feed.png", views.feedCamera); Capture("neck-cockpit.png", input.cockpitCamera); views.Select(CockpitViewPosition.Mount.Chest); Next(); } break;
                    case 8:
                        if (elapsed > .3f)
                        {
                            GuardVisible(); Capture("chest-feed.png", views.feedCamera); Capture("chest-cockpit.png", input.cockpitCamera); Check(Vector3.Distance(views.feedCamera.transform.position, views.chest.position) < .001f, "camera switches to chest anchor");
                            var overview = new GameObject("Validation overview").AddComponent<Camera>(); overview.transform.position = new Vector3(10, 4, 103.5f);
                            overview.transform.LookAt(new Vector3(0, 0, 103.5f)); overview.fieldOfView = 65; overview.farClipPlane = 40;
                            Capture("matched-models.png", overview); UnityEngine.Object.Destroy(overview.gameObject);
                            var fist = input.leftArm.hand.GetComponent<DefaultFistPose>();
                            var forward = fist.transform.TransformDirection(fist.palmForwardLocal); var inward = fist.transform.TransformDirection(fist.palmInwardLocal);
                            var closeup = new GameObject("Fist validation closeup").AddComponent<Camera>();
                            closeup.orthographic = true; closeup.orthographicSize = .55f; closeup.farClipPlane = 1.95f;
                            closeup.clearFlags = CameraClearFlags.SolidColor; closeup.backgroundColor = new Color(.05f,.05f,.05f);
                            var center = fist.transform.position + forward * .35f;
                            closeup.transform.position = center + inward * 1.5f; closeup.transform.LookAt(center, forward);
                            Capture("default-fist-palm.png", closeup); UnityEngine.Object.Destroy(closeup.gameObject);
                            var pilotOverview = new GameObject("Pilot validation overview").AddComponent<Camera>();
                            pilotOverview.transform.position = new Vector3(0, 2.2f, 1.65f); pilotOverview.transform.LookAt(new Vector3(0, 1.1f, 0));
                            pilotOverview.fieldOfView = 85; pilotOverview.nearClipPlane = .03f; pilotOverview.farClipPlane = 20;
                            Capture("pilot-stations.png", pilotOverview); UnityEngine.Object.Destroy(pilotOverview.gameObject);
                            input.SwitchPilot(); Next();
                        } break;
                    case 9:
                        Pose(Vector3.zero, rightSide: true);
                        if (elapsed > 1.2f) { Check(input.Calibrated && !input.leftPilot, "right station recalibrates after switch"); ValidatePilotView(); other = input.leftArm.hand.position; Next(); } break;
                    case 10:
                        Pose(new Vector3(-.06f, .02f, .20f) * Mathf.Clamp01(elapsed / .4f), rightSide: true);
                        if (elapsed > .7f) { ValidatePilotMotion(false); Check(Vector3.Distance(other, input.leftArm.hand.position) < .001f, "right station leaves left robot arm still"); Capture("pilot-right-cockpit.png", input.cockpitCamera);
                            int count = input.Recognizer.CompletedCount;
                            typeof(ArmMotionInput).GetField("previousTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(input, Time.unscaledTime - .3f);
                            typeof(ArmMotionInput).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(input, null);
                            Check(input.Calibrated && input.TrackingValid && input.Recognizer.CompletedCount == count && input.Recognizer.Phase == MotionPhase.WaitingForNeutral,
                                "300 ms render gap discards stroke without freezing arms or clearing completed count"); Next(); } break;
                    case 11:
                        Pose(new Vector3(-.06f, .02f, .20f), rightSide: true);
                        InputSystem.QueueDeltaStateEvent(right.deviceRotation, Quaternion.Euler(20, 35, 15));
                        if (elapsed > .4f)
                        {
                            Check(input.Calibrated, "arm remains live after sample gap"); input.Directions.Refresh();
                            var arrow = input.Directions.controllers[1];
                            Check(arrow.enabled && Vector3.Angle(arrow.GetPosition(1) - arrow.GetPosition(0), input.pilotSeat.rotation * Quaternion.Euler(20, 35, 15) * Vector3.forward) < .1f,
                                "cyan arrow follows actual Oculus controller orientation");
                            Check(input.InputStatus.Contains("LEFT: TRACKED") && input.InputStatus.Contains("RIGHT: TRACKED"), "per-hand tracking diagnostics available");
                            ValidateWristLock();
                            input.cockpitCamera.transform.localRotation = Quaternion.Euler(32, 0, 0); input.Avatar.Evaluate(); input.Directions.Refresh();
                            Capture("hand-directions.png", input.cockpitCamera);
                            input.showHandDirections = false; input.Directions.Refresh();
                            Check(input.Directions.controllers.All(a => !a.enabled) && input.Directions.pilotHands.All(a => !a.enabled) && input.Directions.robotHands.All(a => !a.enabled), "direction overlay can be hidden");
                            input.showHandDirections = true; Next();
                        } break;
                    case 12:
                        Pose(Vector3.Lerp(new Vector3(-.06f, .02f, .20f), new Vector3(0, 0, .35f), Mathf.Clamp01(elapsed / .4f)), rightSide: true);
                        if (elapsed > .7f) { var beforeReach = input.Status; bool reachSaved = input.CalibrateFullReach(); Check(reachSaved, $"full extension calibration accepts tracked straight reach: {beforeReach}; {input.Status}; delta={input.LastControllerDelta}"); Next(); } break;
                    case 13:
                        Pose(new Vector3(0, 0, .35f), rightSide: true);
                        if (elapsed > .4f)
                        {
                            Check(Mathf.Abs(input.Arm.Extension - .96f) < .003f, $"user full reach maps to robot 96% extension ({input.Arm.Extension:P1})");
                            var a = input.Avatar.rig.right;
                            Check(Mathf.Abs(Vector3.Distance(a.upper.position, a.hand.position) / (a.upperLength + a.lowerLength) - .96f) < .003f, "pilot full reach also matches 96% arm length");
                            Capture("full-reach-feed.png", views.feedCamera); Next();
                        } break;
                    case 14:
                        Pose(Vector3.Lerp(new Vector3(0, 0, .35f), Vector3.zero, Mathf.Clamp01(elapsed / .4f)), rightSide: true);
                        if (elapsed > .8f) Next(); break;
                    case 15:
                        // Quaternion.identity for the entire hook: hand POSITION must lift the elbow.
                        Pose(new Vector3(-.23f, .03f, .12f) * Mathf.Clamp01(elapsed / .4f), rightSide: true);
                        if (elapsed > .7f)
                        {
                            var arm = input.Arm; float lift = arm.elbowLift; var raisedElbow = arm.forearm.position;
                            arm.elbowLift = 0; arm.Follow(input.LastControllerDelta, Quaternion.identity); var tuckedElbow = arm.forearm.position;
                            arm.elbowLift = lift; arm.Follow(input.LastControllerDelta, Quaternion.identity);
                            Check(lift > .8f && raisedElbow.y > tuckedElbow.y + .04f, $"fixed-wrist hook raises robot elbow {raisedElbow.y - tuckedElbow.y:F3}m (hint {lift:F2})");
                            Check(arm.TargetError < .002f && Quaternion.Angle(arm.hand.localRotation, arm.LockedWristRotation) < .1f, "hook preserves hand target and wrist fixed to forearm");
                            ValidateWristLock();
                            var a = input.Avatar.rig.right; float raisedY = a.lower.position.y;
                            input.Avatar.Follow(false, input.LastControllerDelta, Quaternion.identity, 0);
                            float tuckedY = a.lower.position.y; input.Avatar.Follow(false, input.LastControllerDelta, Quaternion.identity, lift);
                            Check(raisedY > tuckedY + .02f, $"pilot elbow rises with robot during fixed-wrist hook ({raisedY - tuckedY:F3}m)");
                            input.Directions.Refresh(); Capture("hook-fixed-wrist-feed.png", views.feedCamera);
                            input.cockpitCamera.transform.localRotation = Quaternion.Euler(30, 0, 0); input.Avatar.Evaluate();
                            Capture("hook-fixed-wrist-cockpit.png", input.cockpitCamera); Next();
                        } break;
                    case 16:
                        Pose(Vector3.Lerp(new Vector3(-.23f, .03f, .12f), Vector3.zero, Mathf.Clamp01(elapsed / .4f)), rightSide: true);
                        if (elapsed > .8f)
                        {
                            Check(input.Calibrated, $"contact test keeps live calibration: {input.Status}"); views.Select(CockpitViewPosition.Mount.Head);
                            input.ViewComparison.SetDirect(true); input.ViewComparison.Apply();
                            var direct = input.ViewComparison.directCamera; var feed = views.GetComponent<CockpitLiveView>();
                            Check(direct.enabled && !input.cockpitCamera.enabled && direct.stereoTargetEye == StereoTargetEyeMask.Both && direct.targetTexture == null,
                                "direct robot mode renders XR stereo camera to headset, not cockpit texture");
                            Check(!feed.exteriorCamera.enabled && !feed.leftCamera.enabled && !feed.rightCamera.enabled, "direct mode suspends unused panorama cameras");
                            Check(Vector3.Distance(direct.transform.position, views.head.position) < .001f, "direct robot camera starts at selected robot view anchor");
                            ValidateRigidGuardRotation(); ValidateHookWorkspace();
                            Capture("direct-robot-guard.png", direct); contactCount = input.ContactTest.HitCount; Next();
                        } break;
                    case 17:
                        Pose(new Vector3(0, 0, .35f) * Mathf.Clamp01(elapsed / .45f), rightSide: true);
                        if (elapsed > .9f)
                        {
                            Check(input.ContactTest.HitCount == contactCount + 1, $"physical punch hits reachable pad once while held extended ({input.ContactTest.HitCount - contactCount})");
                            Check(Time.unscaledTime - input.ContactTest.LastHitTime < 1, "contact feedback time belongs to current punch");
                            Capture("direct-robot-contact.png", input.ViewComparison.directCamera); Next();
                        } break;
                    case 18:
                        Pose(new Vector3(0, 0, .35f) * (1 - Mathf.Clamp01(elapsed / .45f)), yaw: 35, rightSide: true);
                        if (elapsed > .9f)
                        {
                            Check(input.ContactTest.HitCount == contactCount + 1, "retracting hand does not score another hit");
                            input.ViewComparison.Apply();
                            Check(Quaternion.Angle(input.ViewComparison.directCamera.transform.rotation, views.head.rotation * Quaternion.Euler(0, 35, 0)) < .1f &&
                                Quaternion.Angle(views.feedCamera.transform.rotation, views.head.rotation) < .1f, "direct inspection follows HMD without rotating robot optical mount");
                            input.ViewComparison.SetDirect(false);
                            var feed = views.GetComponent<CockpitLiveView>();
                            Check(input.cockpitCamera.enabled && !input.ViewComparison.directCamera.enabled && feed.exteriorCamera.enabled && feed.leftCamera.enabled && feed.rightCamera.enabled,
                                "returning to cockpit restores original camera and panorama rendering");
                            Next();
                        } break;
                    case 19:
                        Pose(new Vector3(0, 0, .35f) * Mathf.Clamp01(elapsed / .45f), rightSide: true);
                        if (elapsed > .9f)
                        {
                            Check(input.ContactTest.HitCount == contactCount + 2, "pad rearms after withdrawal for next punch in cockpit mode");
                            Capture("cockpit-contact.png", input.cockpitCamera); Next();
                        } break;
                    case 20:
                        Pose(new Vector3(0, 0, .35f) * (1 - Mathf.Clamp01(elapsed / .45f)), rightSide: true);
                        if (elapsed > .8f)
                        {
                            Check(input.Calibrated, "rotation test keeps tracking calibration after withdrawal");
                            guardElbow = input.Arm.forearm.position; guardWrist = input.Arm.hand.position;
                            pilotGuardElbow = input.Avatar.rig.right.lower.position; pilotGuardWrist = input.Avatar.rig.right.hand.position;
                            Next();
                        } break;
                    case 21:
                        Pose(Vector3.zero, rightSide: true);
                        InputSystem.QueueDeltaStateEvent(right.deviceRotation, Quaternion.Euler(0, 0, 55));
                        if (elapsed > .8f)
                        {
                            Check(input.Arm.forearm.position.y > guardElbow.y + .04f, $"XR controller inward rotation raises robot elbow at fixed hand position ({input.Arm.forearm.position.y - guardElbow.y:F3}m); {input.Status}");
                            Check(input.Avatar.rig.right.lower.position.y > pilotGuardElbow.y + .02f, "XR controller inward rotation raises pilot elbow");
                            Check(Vector3.Distance(input.Arm.hand.position, guardWrist) < .002f && Vector3.Distance(input.Avatar.rig.right.hand.position, pilotGuardWrist) < .002f, "XR rotation keeps robot and pilot wrist positions fixed");
                            ValidateWristLock(); Capture("inward-rotation-feed.png", views.feedCamera); Complete();
                        } break;
                }
                if (!SessionState.GetBool(Finished, false) && input.Calibrated)
                {
                    // Consume synthetic input in the same tick that produced it.
                    // A deferred editor event after a screenshot can otherwise pair
                    // the old pose with a later, tiny player-frame time interval.
                    InputSystem.Update();
                    typeof(ArmMotionInput).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(input, null);
                }
            }
            catch (Exception e) { checks.Add("FAIL: " + e); Complete(); }
        }
        static void ValidateGuardAlignment()
        {
            foreach (var arm in new[] { input.leftArm, input.rightArm })
            {
                var back = Vector3.ProjectOnPlane(arm.robotFrame.forward, arm.hand.position - arm.forearm.position).normalized;
                Check(Vector3.Dot(arm.HandFrame * Vector3.up, back) > .999f, "robot guard back-of-hand faces front rather than upward");
            }
            foreach (var avatar in new[] { input.leftAvatar, input.rightAvatar }) foreach (var a in new[] { avatar.rig.left, avatar.rig.right })
            {
                var back = Vector3.ProjectOnPlane(avatar.transform.forward, a.hand.position - a.lower.position).normalized;
                Check(Vector3.Dot(avatar.rig.HandFrame(a) * Vector3.up, back) > .999f, "pilot guard uses same palm-facing-body reference");
            }
        }
        static void ValidateWristLock()
        {
            foreach (var arm in new[] { input.leftArm, input.rightArm })
            {
                Check(Quaternion.Angle(arm.hand.localRotation, arm.LockedWristRotation) < .1f &&
                    Vector3.Angle(arm.HandFrame * Vector3.forward, arm.hand.position - arm.forearm.position) < .1f,
                    "robot fist stays straight and fixed to forearm");
            }
            foreach (var avatar in new[] { input.leftAvatar, input.rightAvatar }) foreach (var a in new[] { avatar.rig.left, avatar.rig.right })
                Check(Quaternion.Angle(a.hand.localRotation, a.handRest) < .1f && Mathf.Abs(a.appliedRoll) < .01f &&
                    Mathf.Abs(a.wristFlex) < .01f && Mathf.Abs(a.wristDeviation) < .01f,
                    "pilot wrist stays rigid while whole forearm may rotate");
        }
        static void ValidateRigidGuardRotation()
        {
            foreach (bool leftSide in new[] { true, false })
            {
                var robot = leftSide ? input.leftArm : input.rightArm;
                var avatar = leftSide ? input.leftAvatar : input.rightAvatar;
                var a = leftSide ? avatar.rig.left : avatar.rig.right;
                float lift = robot.elbowLift; robot.elbowLift = 0;
                robot.Follow(Vector3.zero, Quaternion.identity); avatar.Follow(leftSide, Vector3.zero, Quaternion.identity);
                var elbow = robot.forearm.position; var wrist = robot.hand.position;
                var pilotElbow = a.lower.position; var pilotWrist = a.hand.position;
                float upperLength = Vector3.Distance(robot.upper.position, elbow), lowerLength = Vector3.Distance(elbow, wrist);
                var rotation = Quaternion.Euler(0, 0, leftSide ? -55 : 55);
                robot.Follow(Vector3.zero, rotation); avatar.Follow(leftSide, Vector3.zero, rotation);
                Check(robot.forearm.position.y > elbow.y + .04f, $"{leftSide} inward rotation raises robot elbow ({robot.forearm.position.y - elbow.y:F3}m)");
                Check(a.lower.position.y > pilotElbow.y + .02f, $"{leftSide} inward rotation raises pilot elbow ({a.lower.position.y - pilotElbow.y:F3}m)");
                Check(Vector3.Distance(robot.hand.position, wrist) < .002f && Vector3.Distance(a.hand.position, pilotWrist) < .002f, "rotation-driven elbow preserves both wrist positions");
                Check(Mathf.Abs(Vector3.Distance(robot.upper.position, robot.forearm.position) - upperLength) < .001f && Mathf.Abs(Vector3.Distance(robot.forearm.position, robot.hand.position) - lowerLength) < .001f, "rotation-driven arm keeps bone lengths");
                ValidateWristLock();
                var repeat = a.lower.position; avatar.Evaluate();
                Check(Vector3.Distance(repeat, a.lower.position) < .001f, "pilot repeated evaluation does not accumulate rotation");
                robot.Follow(Vector3.zero, Quaternion.identity); avatar.Follow(leftSide, Vector3.zero, Quaternion.identity); robot.elbowLift = lift;
                Check(Vector3.Distance(robot.forearm.position, elbow) < .002f && Vector3.Distance(a.lower.position, pilotElbow) < .002f, "returning controller orientation restores guard elbow");
            }
        }
        static void ValidateHookWorkspace()
        {
            foreach (bool leftSide in new[] { true, false })
            {
                var arm = leftSide ? input.leftArm : input.rightArm;
                var avatar = leftSide ? input.leftAvatar : input.rightAvatar;
                var pilot = leftSide ? avatar.rig.left : avatar.rig.right;
                float oldGain = arm.motionScale, oldLift = arm.elbowLift;
                float pilotGain = leftSide ? avatar.leftReachScale : avatar.rightReachScale;
                foreach (bool calibrated in new[] { false, true })
                {
                    if (calibrated) { arm.CalibrateReach(Vector3.forward * .35f); avatar.CalibrateReach(leftSide, Vector3.forward * .35f); }
                    // Wind up outward, sweep inward, then return: no recognizer or animation involved.
                    float maxExtension = 0, maxPilotExtension = 0, largestStep = 0;
                    bool reachable = true, rigid = true;
                    Vector3 previous = Vector3.zero;
                    for (int i = 0; i <= 60; i++)
                    {
                        float t = i / 60f, sign = leftSide ? -1 : 1;
                        var delta = new Vector3(sign * Mathf.Lerp(.28f, -.18f, t), .03f, .14f * Mathf.Sin(t * Mathf.PI));
                        var rotation = Quaternion.Euler(0, 0, leftSide ? -55 : 55);
                        arm.elbowLift = 1; arm.Follow(delta, rotation); avatar.Follow(leftSide, delta, rotation, 1);
                        maxExtension = Mathf.Max(maxExtension, arm.Extension);
                        maxPilotExtension = Mathf.Max(maxPilotExtension, Vector3.Distance(pilot.upper.position, pilot.hand.position) / (pilot.upperLength + pilot.lowerLength));
                        if (i > 0) largestStep = Mathf.Max(largestStep, Vector3.Distance(previous, arm.hand.position));
                        previous = arm.hand.position;
                        reachable &= arm.TargetError < .002f && arm.ReachError < .002f;
                        rigid &= Quaternion.Angle(arm.hand.localRotation, arm.LockedWristRotation) < .1f;
                    }
                    Check(reachable && rigid, "entire compact hook stays reachable with rigid wrist");
                    Check(maxExtension < .92f && maxPilotExtension < .92f, $"{leftSide} hook retains elbow bend before/after reach calibration {calibrated}: robot {maxExtension:P1}, pilot {maxPilotExtension:P1}");
                    Check(largestStep < .04f, "hook sweep remains continuous without reach-boundary sticking");
                    arm.Follow(new Vector3((leftSide ? -1 : 1) * .15f, 0, 0), Quaternion.identity);
                    Check(arm.Extension < .85f, "small outward hand movement does not fully extend arm");
                    arm.Follow(new Vector3(0, 0, -.18f), Quaternion.identity);
                    Check(arm.Extension < .7f, "pulling hand back permits deeply bent arm");
                    if (calibrated)
                    {
                        arm.Follow(Vector3.forward * .35f, Quaternion.identity);
                        Check(Mathf.Abs(arm.Extension - .96f) < .003f, "directional mapping preserves calibrated full jab reach");
                    }
                }
                // Regression: a bent elbow is not enough if the hand barely moves.
                arm.Follow(Vector3.zero, Quaternion.identity); avatar.Follow(leftSide, Vector3.zero, Quaternion.identity);
                var robotReady = arm.hand.position; var pilotReady = pilot.hand.position;
                float robotLength = Vector3.Distance(arm.upper.position, arm.forearm.position) + Vector3.Distance(arm.forearm.position, arm.hand.position);
                float pilotLength = pilot.upperLength + pilot.lowerLength;
                var windup = new Vector3(leftSide ? -.12f : .12f, 0, -.2f);
                arm.Follow(windup, Quaternion.identity); avatar.Follow(leftSide, windup, Quaternion.identity);
                var robotBack = arm.hand.position; var pilotBack = pilot.hand.position;
                Check(Vector3.Distance(robotReady, robotBack) > robotLength * .28f && Vector3.Distance(pilotReady, pilotBack) > pilotLength * .28f,
                    $"windup retains substantial travel: robot {Vector3.Distance(robotReady, robotBack):F3}m, pilot {Vector3.Distance(pilotReady, pilotBack):F3}m");
                var across = new Vector3(leftSide ? .18f : -.18f, .03f, .1f);
                arm.Follow(across, Quaternion.Euler(0, 0, leftSide ? -55 : 55)); avatar.Follow(leftSide, across, Quaternion.Euler(0, 0, leftSide ? -55 : 55), 1);
                Check(Vector3.Distance(robotBack, arm.hand.position) > robotLength * .4f && Vector3.Distance(pilotBack, pilot.hand.position) > pilotLength * .4f,
                    "back-to-front hook produces substantial robot and pilot sweep");
                Check(arm.Extension < .92f, "restored hook travel still preserves elbow bend");
                // A mildly diagonal reach calibration must fit the SAME mapped target,
                // without accepting sideways-only calibration or increasing lateral gain.
                arm.Follow(Vector3.right * .15f, Quaternion.identity); var sideBefore = arm.hand.position;
                Check(!arm.CalibrateReach(new Vector3(.3f, 0, .2f)), "sideways pose rejected for forward reach calibration");
                var reach = new Vector3(.04f, .03f, .35f);
                Check(arm.CalibrateReach(reach) && avatar.CalibrateReach(leftSide, reach), "mild diagonal full reach accepted");
                arm.Follow(reach, Quaternion.identity); avatar.Follow(leftSide, reach, Quaternion.identity);
                Check(Mathf.Abs(arm.Extension - .96f) < .003f && Mathf.Abs(Vector3.Distance(pilot.upper.position, pilot.hand.position) / (pilot.upperLength + pilot.lowerLength) - .96f) < .003f, "diagonal reach calibration matches anisotropic robot and pilot targets");
                arm.Follow(Vector3.right * .15f, Quaternion.identity);
                Check(Vector3.Distance(sideBefore, arm.hand.position) < .002f, "forward reach calibration leaves sideways travel unchanged");
                arm.motionScale = oldGain; arm.elbowLift = oldLift;
                if (leftSide) avatar.leftReachScale = pilotGain; else avatar.rightReachScale = pilotGain;
                arm.Follow(Vector3.zero, Quaternion.identity); avatar.Follow(leftSide, Vector3.zero, Quaternion.identity);
            }
        }
        static void ValidatePilotView()
        {
            var avatar = input.Avatar;
            var remote = input.leftPilot ? input.rightAvatar : input.leftAvatar;
            Check(avatar && remote && avatar.rig.IsReady && remote.rig.IsReady, "both existing V04 pilot rigs are ready");
            Check(Vector3.Distance(avatar.eyeAnchor.position, input.cockpitCamera.transform.position) < .001f && Quaternion.Angle(avatar.eyeAnchor.rotation, input.cockpitCamera.transform.rotation) < .1f, "camera matches pilot eye position and orientation");
            Check((input.cockpitCamera.cullingMask & (1 << avatar.localView.Head.gameObject.layer)) == 0 &&
                (input.cockpitCamera.cullingMask & (1 << remote.localView.Head.gameObject.layer)) != 0, "only selected pilot head hidden from cockpit camera");
            Check(avatar.rig.leftGrip == 1 && avatar.rig.rightGrip == 1 && remote.rig.leftGrip == 1 && remote.rig.rightGrip == 1, "both pilots keep default closed fists");
        }
        static void ValidatePilotMotion(bool leftSide)
        {
            var avatar = input.Avatar; var arm = leftSide ? avatar.rig.left : avatar.rig.right;
            var delta = avatar.transform.InverseTransformPoint(arm.target.position) - (leftSide ? avatar.leftReady : avatar.rightReady);
            Check(input.Calibrated && delta.magnitude > .02f && Vector3.Distance(delta, avatar.MappedDelta(leftSide, input.LastControllerDelta)) < .001f,
                (leftSide ? "left" : "right") + " pilot applies directional workspace mapping at human scale");
            Check(Mathf.Abs(arm.targetError - arm.clampedDistance) < .002f, "pilot arm follows target within physical reach");
            ValidatePilotView();
        }
        static void ValidateFists(string phase)
        {
            var fists = UnityEngine.Object.FindObjectsByType<DefaultFistPose>(FindObjectsSortMode.None);
            Check(fists.Length == 4, "player and boss both have two default fists " + phase);
            foreach (var fist in fists)
            {
                Check(fist.joints.Length == 15 && fist.joints.Select((j, i) => Quaternion.Angle(j.localRotation, fist.closedRotations[i])).All(a => a < .1f),
                    fist.name + " keeps closed pose " + phase);
                float closed = Vector3.Distance(fist.transform.position, fist.joints[5].position);
                for (int i = 0; i < fist.joints.Length; i++) fist.joints[i].localRotation = fist.restRotations[i];
                float open = Vector3.Distance(fist.transform.position, fist.joints[5].position); fist.Apply();
                Check(closed < open * .9f, $"{fist.name} fingers fold toward palm ({closed:F3}m vs {open:F3}m)");
                var rotations = fist.joints.Select(j => j.localRotation).ToArray(); fist.Apply(); fist.Apply();
                Check(fist.joints.Select((j, i) => Quaternion.Angle(j.localRotation, rotations[i])).All(a => a < .1f), "reapplying fist does not accumulate curl");
            }
        }
        static void ValidateFingers(SkinnedMeshRenderer skin)
        {
            var weights = skin.sharedMesh.boneWeights;
            var original = skin.transform.parent.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Where(r => AssetDatabase.GetAssetPath(r.sharedMesh) == ArmMotionSceneBuilder.ModelPath)
                .SelectMany(r => r.sharedMesh.vertices.Select(v => skin.transform.InverseTransformPoint(r.transform.TransformPoint(v)))).ToArray();
            var derived = skin.sharedMesh.vertices;
            Check(original.Length == derived.Length, "derived v26 skin preserves every source vertex");
            float maximumError = original.Select((p, i) => Vector3.Distance(p, derived[i])).Max();
            Check(maximumError < .0001f, $"v26 rebind preserves original rest geometry (max error {maximumError:F6}m)");
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                    for (int joint = 1; joint <= 3; joint++)
                    {
                        string name = "mixamorig:" + side + "Hand" + finger + joint;
                        int index = Array.FindIndex(skin.bones, b => b.name == name);
                        Check(index >= 0 && weights.Any(w => (w.boneIndex0 == index && w.weight0 > 0) || (w.boneIndex1 == index && w.weight1 > 0)
                            || (w.boneIndex2 == index && w.weight2 > 0) || (w.boneIndex3 == index && w.weight3 > 0)), name + " exists and influences vertices");
                    }
                var bone = skin.bones.First(b => b.name == "mixamorig:" + side + "HandIndex2");
                var rotation = bone.localRotation; var before = new Mesh(); var after = new Mesh();
                try
                {
                    skin.BakeMesh(before); bone.localRotation = rotation * Quaternion.Euler(20, 0, 0); skin.BakeMesh(after);
                    var a = before.vertices; var b = after.vertices;
                    Check(a.Where((v, i) => Vector3.Distance(v, b[i]) > .0001f).Any(), side + " finger joint rotation deforms visible skin");
                }
                finally { bone.localRotation = rotation; UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
            }
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
            foreach (var device in new InputDevice[] { hmd, left, right, untrackedDuplicate }) if (device != null) InputSystem.RemoveDevice(device);
            foreach (var device in disabledDevices) if (device.added) InputSystem.EnableDevice(device); disabledDevices.Clear();
            if (original) { InputSystem.settings = original; UnityEngine.Object.DestroyImmediate(temporary); }
            SessionState.SetString(Report, string.Join("\n", checks)); SessionState.SetBool(Finished, true); EditorApplication.isPlaying = false;
        }
    }
}
