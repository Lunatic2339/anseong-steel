using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnseongSteel.Bosses.Editor
{
    // Can also run with -executeMethod ...BossModelSmokeTest.Run (without -quit).
    [InitializeOnLoad]
    public static class BossModelSmokeTest
    {
        private const string RunningKey = "BossPrototype.SmokeTest.Running";
        private static double started;
        private static bool subscribed;
        private static bool testing;
        private static bool walked;
        private static int impacts;
        private static int targetHits;
        private static string failure;
        private static BossModelDemo demo;
        private static Vector3 initialPosition;
        private static Vector3 initialHandPosition;
        private static bool animated;
        private static bool seenPunch;
        private static bool recovered;
        private static float punchStartedAt;
        private static float impactAt;
        private static float recoveredAt;
        private static int turnPhase;
        private static float turnStarted, turnYaw;
        private static Vector3 turnPosition;
        private static bool sawTurn;
        private static string demoReport;
        private static Transform hips;
        private static Quaternion hipsStart;
        private static float maxVisualYaw;
        private static float nextYawSample;
        private static readonly float[] TestAngles = { -90f, 90f, 180f, -45f };

        static BossModelSmokeTest()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            testing = true;
            started = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += CaptureError;
            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Boss Prototype/Run Model Smoke Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before running the test.");
            // Verify the existing configured scene without rebuilding user content.
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var sceneDemo = UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            if (sceneDemo == null || sceneDemo.driver.animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Scene is missing demo/controller references.");
            sceneDemo.autoDemo = true;
            var clips = sceneDemo.driver.animator.runtimeAnimatorController.animationClips;
            if (new[]{"Idle","Walk","Punch","TurnLeft","TurnRight"}.Any(name=>!clips.Any(c=>c.name==name && c.length>0f)))
                throw new InvalidOperationException("Missing required motion clips: " + string.Join(", ", clips.Select(c => c.name + "=" + c.length)));
            var punch = clips.Single(c => c.name == "Punch");
            if (sceneDemo.driver.hitPoint.parent.name != "mixamorig:RightHand" || punch.length < 5f)
                throw new InvalidOperationException("Expected the right-hand hit point and the complete punch take.");
            var events = AnimationUtility.GetAnimationEvents(punch);
            if (events.Length != 2 || events.Any(e => e.time < 0f || e.time > punch.length))
                throw new InvalidOperationException("Punch events are outside the clip.");
            started = EditorApplication.timeSinceStartup;
            subscribed = walked = animated = false;
            seenPunch = recovered = false;
            punchStartedAt = impactAt = recoveredAt = 0f;
            impacts = targetHits = 0;
            failure = null;
            turnPhase = 0;
            testing = true;
            SessionState.SetBool(RunningKey, true);
            Application.logMessageReceived += CaptureError;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        private static void CaptureError(string message, string trace, LogType type)
        {
            // Unity 6.3 can throw during first-run search indexing in batch mode.
            // Keep that engine error in the log; it is unrelated to the scene under test.
            if (trace.Contains("UnityEditor.Search.SearchDatabase") && trace.Contains("IndexationOnStartup")) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
        }

        private static void Tick()
        {
            if (!testing) return;
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (!EditorApplication.isPlaying)
            {
                if (subscribed || elapsed > 60) Finish(false, "Play mode stopped or failed to start.");
                return;
            }
            if (!subscribed)
            {
                demo = UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
                if (demo == null) return;
                demo.driver.attack.ImpactChecked += OnImpact;
                initialPosition = demo.driver.transform.position;
                initialHandPosition = demo.driver.transform.InverseTransformPoint(demo.driver.hitPoint.position);
                subscribed = true;
                started = EditorApplication.timeSinceStartup;
                return;
            }
            walked |= Vector3.Distance(initialPosition, demo.driver.transform.position) > 0.5f;
            animated |= Vector3.Distance(initialHandPosition, demo.driver.transform.InverseTransformPoint(demo.driver.hitPoint.position)) > 0.08f;
            if (demo.driver.IsAttacking && !seenPunch)
            {
                seenPunch = true;
                punchStartedAt = Time.time;
            }
            if (seenPunch && !demo.driver.IsAttacking && !recovered)
            {
                recovered = true;
                recoveredAt = Time.time;
            }
            if (failure != null) { Finish(false, failure); return; }
            if (turnPhase > 0) { CheckTurn(); return; }
            if (elapsed > 16 && !demo.driver.IsAttacking && !demo.driver.movement.IsMoving)
            {
                bool fullRecovery = recovered && recoveredAt - punchStartedAt > demo.driver.punchDuration - 0.2f
                    && recoveredAt - impactAt > 2.5f;
                var towardTarget = demo.target.position - demo.driver.transform.position;
                towardTarget.y = 0f;
                bool facingTarget = Vector3.Angle(demo.driver.transform.forward, towardTarget) < 0.5f;
                bool modelFacingTarget = Vector3.Angle(demo.driver.animator.transform.forward, towardTarget) < .5f;
                float targetDistance = towardTarget.magnitude;
                // Run demo retains the fist distance; the manual walk button now uses sword distance.
                bool reachedTarget = Mathf.Abs(targetDistance - 1.05f) < .01f;
                bool ok = walked && animated && impacts == 1 && targetHits == 1 && fullRecovery && facingTarget && modelFacingTarget && reachedTarget;
                demoReport = $"walked={walked}, animated={animated}, impacts={impacts}, targetHits={targetHits}, fullRecovery={fullRecovery}, facingTarget={facingTarget}, modelFacingTarget={modelFacingTarget}, targetDistance={targetDistance:F3}";
                if (!ok) Finish(false, demoReport);
                else { turnPhase = 1; StartTurn(); }
            }
            else if (elapsed > 30) Finish(false, "Demo did not complete within 30 seconds.");
        }

        private static void StartTurn()
        {
            turnStarted = Time.time;
            turnYaw = demo.driver.transform.eulerAngles.y;
            turnPosition = demo.driver.transform.position;
            sawTurn = false;
            hips = demo.driver.animator.GetComponentsInChildren<Transform>().Single(t => t.name == "mixamorig:Hips");
            hipsStart = Quaternion.Inverse(demo.driver.transform.rotation) * hips.rotation;
            maxVisualYaw = 0f;
            nextYawSample = Time.time;
            float angle = TestAngles[turnPhase - 1];
            if (angle < 0) demo.driver.rotation.TurnLeft(-angle);
            else demo.driver.rotation.TurnRight(angle);
        }

        private static void CheckTurn()
        {
            var driver = demo.driver;
            float angle = TestAngles[turnPhase - 1];
            var state = driver.animator.GetCurrentAnimatorStateInfo(0);
            sawTurn |= state.IsName(angle < 0 ? "TurnLeft" : "TurnRight");
            var relative = Quaternion.Inverse(driver.transform.rotation) * hips.rotation * Quaternion.Inverse(hipsStart);
            maxVisualYaw = Mathf.Max(maxVisualYaw, Mathf.Abs(Mathf.DeltaAngle(0, relative.eulerAngles.y)));
            if (Time.time >= nextYawSample)
            {
                Debug.Log($"Visual yaw: time={Time.time-turnStarted:F2}, root={driver.transform.eulerAngles.y:F2}, relative={Mathf.DeltaAngle(0, relative.eulerAngles.y):F2}");
                nextYawSample = Time.time + .3f;
            }
            if (Time.time - turnStarted > 8f) { Finish(false, "Turn timed out."); return; }
            if (driver.IsTurning || Time.time - turnStarted < Mathf.Ceil(Mathf.Abs(angle) / 90f) * 2.067f + .4f) return;
            float error = Mathf.Abs(Mathf.DeltaAngle(turnYaw + angle, driver.transform.eulerAngles.y));
            bool ok = error < .1f && maxVisualYaw < 10f && sawTurn && !driver.rotation.IsRotating && driver.movement.enabled
                && Vector3.Distance(turnPosition, driver.transform.position) < .001f;
            Debug.Log($"Turn test: angle={angle}, error={error}, maxVisualYaw={maxVisualYaw}, animated={sawTurn}, restoredMovement={driver.movement.enabled}");
            if (!ok) { Finish(false, "Turn pose/angle/movement check failed."); return; }
            if (++turnPhase > TestAngles.Length) { Finish(true, demoReport + ", turnsLeft90Right90Right180Left45=True"); return; }
            StartTurn();
        }

        private static void OnImpact(Collider[] candidates)
        {
            impacts++;
            impactAt = Time.time;
            var fist = demo.driver.hitPoint.position;
            var closest = demo.target.GetComponent<Collider>().ClosestPoint(fist);
            Debug.Log($"Right-punch contact: fist={fist:F3}, targetClosest={closest:F3}, gap={Vector3.Distance(fist, closest):F3}, radius={demo.driver.hitRadius:F3}, root={demo.driver.transform.position:F3}");
            if (candidates.Any(c => c.transform == demo.target || c.transform.IsChildOf(demo.target))) targetHits++;
            // Duplicate animation-event delivery must never cause a second hit.
            demo.driver.OnPunchImpact();
        }

        private static void Finish(bool success, string details)
        {
            testing = false;
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureError;
            if (demo != null) demo.driver.attack.ImpactChecked -= OnImpact;
            string report = (success ? "PASS: " : "FAIL: ") + details;
            string reportPath = Path.Combine("Library", "BossModelSmokeTest.txt");
            File.WriteAllText(reportPath, report);
            Debug.Log(report);
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                try { CaptureCamera(Camera.main); }
                catch (Exception exception) { Debug.LogWarning("Preview capture failed: " + exception.Message); }
            }
            EditorApplication.isPlaying = false;
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        // Run with a graphics device (omit -nographics). Captures the saved scene.
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var camera = Camera.main;
            var demo = UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            var idle = demo.driver.animator.runtimeAnimatorController.animationClips.Single(c => c.name == "Idle");
            idle.SampleAnimation(demo.driver.animator.gameObject, 0f);
            CaptureCamera(camera);
        }

        private static void CaptureCamera(Camera camera)
        {
            // A manual batch-mode render can run before SRP material buffers have
            // been uploaded by a normal frame. Render once to warm up, then read.
            var target = new RenderTexture(1280, 800, 24);
            target.Create();
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("Current pipeline does not support preview capture.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            var oldActive = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            File.WriteAllBytes("Library/BossModelPreview.png", image.EncodeToPNG());
            RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}


