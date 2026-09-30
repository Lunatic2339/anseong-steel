using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

[InitializeOnLoad]
public static class MechaVRDiagnostics
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "MechaVR_Diagnostics_20260929");
    private static readonly string CommandPath = Path.Combine(Folder, "command.txt");
    private static DateTime bridgeUntil;
    private static double nextReport;
    private static string lastResult = "Diagnostic helper loaded";
    private static readonly Dictionary<MechaVRHandTarget, Vector3> previousControllers = new Dictionary<MechaVRHandTarget, Vector3>();
    private static readonly Dictionary<MechaVRHandTarget, Vector3> previousTargets = new Dictionary<MechaVRHandTarget, Vector3>();

    static MechaVRDiagnostics()
    {
        try
        {
            string sessionPath = Path.Combine(Folder, "session.txt");
            if (File.Exists(sessionPath))
            {
                string[] lines = File.ReadAllLines(sessionPath);
                if (lines.Length == 2 && string.Equals(Path.GetFullPath(lines[0]), Path.GetFullPath(Application.dataPath), StringComparison.OrdinalIgnoreCase))
                    bridgeUntil = new DateTime(long.Parse(lines[1], CultureInfo.InvariantCulture), DateTimeKind.Utc);
            }
        }
        catch (Exception e) { Debug.LogWarning("[MechaVR] Diagnostic session: " + e.Message); }
        EditorApplication.update += Tick;
        EditorApplication.delayCall += WriteReport;
    }

    private static MechaVRHandTarget[] Drivers() => UnityEngine.Object.FindObjectsByType<MechaVRHandTarget>(FindObjectsInactive.Include)
        .Where(d => d.gameObject.scene.IsValid()).ToArray();

    private static void Tick()
    {
        if (DateTime.UtcNow > bridgeUntil || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextReport)
            return;
        nextReport = EditorApplication.timeSinceStartup + 0.5;
        try
        {
            string command = File.Exists(CommandPath) ? File.ReadAllText(CommandPath).Trim() : "";
            if (command == "repair" && EditorApplication.isPlaying)
                lastResult = "Repair waiting for Play mode to stop";
            else if (command.Length > 0)
            {
                File.WriteAllText(CommandPath, "");
                switch (command)
                {
                    case "inspect": lastResult = "Inspection requested"; break;
                    case "setup-boss-contact": lastResult = MechaVRBossSetup.Configure(); break;
                    case "test-boss-contact": lastResult = MechaVRBossContactTests.Run(); break;
                    case "repair": RepairRigLinks(); break;
                    case "start": EditorApplication.isPlaying = true; lastResult = "Play requested"; break;
                    case "stop": EditorApplication.isPlaying = false; lastResult = "Stop requested"; break;
                    case "calibrate": CalibrateBoth(); break;
                    case "test-ik": TestIK(); break;
                    case "refresh": lastResult = "Asset refresh requested"; AssetDatabase.Refresh(); break;
                    case "always-animate": SetAlwaysAnimate(); break;
                    case "setup-body-follow": SetupBodyFollow(); break;
                    case "test-body-follow": TestBodyFollow(); break;
                    case "finish": bridgeUntil = DateTime.MinValue; lastResult = "Automatic diagnostic session finished"; break;
                    default: lastResult = "Unknown diagnostic command ignored"; break;
                }
            }
            WriteReport();
        }
        catch (Exception e) { lastResult = e.ToString(); WriteReport(); }
    }

    private static string Hierarchy(Transform t)
    {
        if (!t) return "NONE";
        var parts = new List<string>();
        for (; t; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string Pose(Transform t) => !t ? "NONE" : Hierarchy(t) + " position=" + t.position.ToString("F5") + " scale=" + t.lossyScale.ToString("F3");

    [MenuItem("Tools/Mecha VR/Write Diagnostic Report")]
    public static void WriteReport()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            sb.AppendLine("UTC=" + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("project=" + Application.dataPath);
            sb.AppendLine("scene=" + SceneManager.GetActiveScene().path + " dirty=" + SceneManager.GetActiveScene().isDirty);
            sb.AppendLine("playing=" + EditorApplication.isPlaying + " paused=" + EditorApplication.isPaused + " focused=" + Application.isFocused + " frame=" + Time.frameCount);
            sb.AppendLine("keyboard=" + (Keyboard.current != null) + " result=" + lastResult);
            sb.AppendLine("Input devices=" + string.Join("; ", InputSystem.devices.Select(d => d.layout + ":" + d.displayName + ":enabled=" + d.enabled)));
            MechaVRBossSetup.AppendReport(sb);
            foreach (var body in UnityEngine.Object.FindObjectsByType<MechaVRBodyFollower>(FindObjectsInactive.Include))
            {
                sb.AppendLine("BODY " + Pose(body.transform) + " enabled=" + body.isActiveAndEnabled);
                sb.AppendLine("  head=" + Pose(body.head));
                if (body.head && body.mechaHeadSpace) sb.AppendLine("  head-anchor-error=" + Vector3.Distance(body.head.position, body.mechaHeadSpace.position).ToString("F6"));
            }
            foreach (XRNode node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
                sb.AppendLine("XR " + node + " device=" + device.name + " valid=" + device.isValid + " tracked=" + tracked);
            }
            var builders = new HashSet<RigBuilder>();
            foreach (var d in Drivers())
            {
                sb.AppendLine("DRIVER " + Hierarchy(d.transform) + " enabled=" + d.isActiveAndEnabled + " status=" + d.Status + " calibrations=" + d.CalibrationCount + " body-position=" + d.alignPositionToController);
                sb.AppendLine("  controller=" + Pose(d.controller));
                sb.AppendLine("  target=" + Pose(d.target));
                if (d.controller && d.target)
                {
                    MechaVRHandTarget id = d;
                    if (previousControllers.TryGetValue(id, out Vector3 pc) && previousTargets.TryGetValue(id, out Vector3 pt))
                        sb.AppendLine("  sample movement controller=" + Vector3.Distance(pc, d.controller.position).ToString("F6") + " target=" + Vector3.Distance(pt, d.target.position).ToString("F6"));
                    previousControllers[id] = d.controller.position;
                    previousTargets[id] = d.target.position;
                }
                var ik = d.GetComponent<TwoBoneIKConstraint>();
                if (ik)
                {
                    sb.AppendLine("  IK valid=" + ((IRigConstraint)ik).IsValid() + " weight=" + ik.weight);
                    sb.AppendLine("  root=" + Hierarchy(ik.data.root) + " mid=" + Hierarchy(ik.data.mid) + " tip=" + Pose(ik.data.tip));
                    if (ik.data.tip && ik.data.target) sb.AppendLine("  tip-target-distance=" + Vector3.Distance(ik.data.tip.position, ik.data.target.position).ToString("F6"));
                }
                var rig = d.GetComponentInParent<Rig>();
                sb.AppendLine("  ancestor rig=" + (rig ? rig.name : "NONE"));
                var builder = d.GetComponentInParent<RigBuilder>();
                if (builder) builders.Add(builder);
            }
            foreach (var b in builders)
            {
                var a = b.GetComponent<Animator>();
                sb.AppendLine("BUILDER " + Hierarchy(b.transform) + " enabled=" + b.isActiveAndEnabled + " graph=" + b.graph.IsValid());
                if (a) sb.AppendLine("  Animator enabled=" + a.enabled + " culling=" + a.cullingMode + " update=" + a.updateMode + " avatar=" + (a.avatar ? a.avatar.name + ":valid=" + a.avatar.isValid : "NONE"));
                for (int i = 0; i < b.layers.Count; i++)
                {
                    var l = b.layers[i];
                    sb.AppendLine("  layer " + i + " rig=" + (l?.rig ? l.rig.name : "NONE") + " active=" + l?.active + " initialized=" + l?.isInitialized + " constraints=" + (l?.constraints?.Length ?? 0));
                }
            }
            File.WriteAllText(Path.Combine(Folder, "report.txt"), sb.ToString());
        }
        catch (Exception e) { Debug.LogWarning("[MechaVR] Could not write diagnostic report: " + e.Message); }
    }

    [MenuItem("Tools/Mecha VR/Repair Rig Links")]
    public static void RepairRigLinks()
    {
        if (EditorApplication.isPlaying) { lastResult = "Stop Play before repairing saved Rig links"; return; }
        int changes = 0;
        var scenes = new HashSet<Scene>();
        foreach (var d in Drivers())
        {
            var rig = d.GetComponentInParent<Rig>();
            var builder = d.GetComponentInParent<RigBuilder>();
            if (!rig || !builder) continue;
            var existing = builder.layers.FirstOrDefault(l => l != null && l.rig == rig);
            if (existing != null && existing.active) continue;
            Undo.RecordObject(builder, "Connect Mecha Arm Rig");
            if (existing != null) existing.active = true;
            else
            {
                int empty = builder.layers.FindIndex(l => l == null || l.rig == null);
                if (empty >= 0) builder.layers[empty] = new RigLayer(rig, true);
                else builder.layers.Add(new RigLayer(rig, true));
            }
            EditorUtility.SetDirty(builder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(builder);
            EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
            scenes.Add(builder.gameObject.scene);
            changes++;
        }
        foreach (var scene in scenes)
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        lastResult = "Saved repaired Rig links: " + changes;
        Debug.Log("[MechaVR] " + lastResult);
        WriteReport();
    }

    [MenuItem("Tools/Mecha VR/Calibrate Both Arms Now")]
    public static void CalibrateBoth()
    {
        if (!EditorApplication.isPlaying) { lastResult = "Calibration requires Play mode"; return; }
        foreach (var d in Drivers()) d.Calibrate();
        lastResult = "Calibrate invoked for both arms";
    }

    private static void SetAlwaysAnimate()
    {
        if (EditorApplication.isPlaying) { lastResult = "Stop Play before saving Animator settings"; return; }
        int changes = 0;
        var scenes = new HashSet<Scene>();
        foreach (var animator in Drivers().Select(d => d.GetComponentInParent<Animator>()).Where(a => a).Distinct())
        {
            if (animator.cullingMode == AnimatorCullingMode.AlwaysAnimate) continue;
            Undo.RecordObject(animator, "Keep Mecha VR IK Active");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);
            scenes.Add(animator.gameObject.scene);
            changes++;
        }
        foreach (var scene in scenes) if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        lastResult = "Saved AlwaysAnimate for " + changes + " Animator(s)";
        Debug.Log("[MechaVR] " + lastResult);
    }

    [MenuItem("Tools/Mecha VR/Set Up Player Body Follow")]
    public static void SetupBodyFollow()
    {
        if (EditorApplication.isPlaying) { lastResult = "Stop Play before configuring body follow"; return; }
        var drivers = Drivers();
        if (drivers.Length != 2) { lastResult = "Body follow setup requires exactly two hand drivers"; return; }
        var builder = drivers[0].GetComponentInParent<RigBuilder>();
        if (!builder || !builder.transform.parent) { lastResult = "MechaRoot parent not found"; return; }
        Transform root = builder.transform.parent;
        var follower = root.GetComponent<MechaVRBodyFollower>();
        Transform origin = follower && follower.trackingOrigin ? follower.trackingOrigin : drivers[0].trackingSpace;
        Camera camera = origin ? origin.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.CompareTag("MainCamera")) : null;
        var iks = drivers.Select(d => d.GetComponent<TwoBoneIKConstraint>()).ToArray();
        if (!camera || iks.Any(ik => !ik || !ik.data.root)) { lastResult = "Head camera or shoulder bones missing"; return; }

        Undo.RecordObject(builder.transform, "Center Mecha Model In Its Root");
        builder.transform.localPosition = Vector3.zero;
        PrefabUtility.RecordPrefabInstancePropertyModifications(builder.transform);

        Transform trackingFrame = origin.Find("BodyTrackingSpace");
        if (!trackingFrame)
        {
            var go = new GameObject("BodyTrackingSpace");
            Undo.RegisterCreatedObjectUndo(go, "Create Human Tracking Frame");
            trackingFrame = go.transform;
            Undo.SetTransformParent(trackingFrame, origin, "Parent Human Tracking Frame");
        }
        Transform headSpace = root.Find("MechaHeadSpace");
        if (!headSpace)
        {
            var go = new GameObject("MechaHeadSpace");
            Undo.RegisterCreatedObjectUndo(go, "Create Mecha Head Frame");
            headSpace = go.transform;
            Undo.SetTransformParent(headSpace, root, "Parent Mecha Head Frame");
        }
        Undo.RecordObject(trackingFrame, "Configure Human Tracking Frame");
        Undo.RecordObject(headSpace, "Configure Mecha Head Frame");
        trackingFrame.localScale = Vector3.one;
        headSpace.localScale = Vector3.one;
        headSpace.localRotation = Quaternion.identity;
        Vector3 shoulders = (iks[0].data.root.position + iks[1].data.root.position) * 0.5f;
        // The virtual head is 18 cm above and 6 cm ahead of the shoulder centre
        // at source scale. Uniform MechaRoot scaling scales this offset too.
        headSpace.localPosition = root.InverseTransformPoint(shoulders) + new Vector3(0f, 0.18f, 0.06f);

        if (!follower) follower = Undo.AddComponent<MechaVRBodyFollower>(root.gameObject);
        Undo.RecordObject(follower, "Configure Mecha Body Follow");
        Undo.RecordObject(root, "Place Mecha At Player");
        follower.head = camera.transform;
        follower.trackingOrigin = origin;
        follower.bodyTrackingSpace = trackingFrame;
        follower.mechaHeadSpace = headSpace;
        follower.followHeadYaw = true;
        foreach (var driver in drivers)
        {
            Undo.RecordObject(driver, "Map Hand To Player Body Frames");
            driver.trackingSpace = trackingFrame;
            driver.mechaSpace = headSpace;
            driver.alignPositionToController = true;
            EditorUtility.SetDirty(driver);
            PrefabUtility.RecordPrefabInstancePropertyModifications(driver);
        }
        follower.ApplyPose();
        EditorUtility.SetDirty(follower);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
        lastResult = "Saved player body follow and direct controller-to-wrist position mapping";
        Debug.Log("[MechaVR] " + lastResult);
        WriteReport();
    }

    private static void TestBodyFollow()
    {
        if (!EditorApplication.isPlaying) { lastResult = "Body follow test requires Play mode"; return; }
        var body = UnityEngine.Object.FindObjectsByType<MechaVRBodyFollower>(FindObjectsInactive.Exclude).FirstOrDefault();
        if (!body || !body.head || !body.bodyTrackingSpace || !body.mechaHeadSpace) { lastResult = "Body follower not configured"; return; }
        Transform realHead = body.head;
        bool bodyEnabled = body.enabled;
        var testObject = new GameObject("__MechaFollowValidation") { hideFlags = HideFlags.HideAndDontSave };
        testObject.SetActive(false);
        var headObject = new GameObject("__MechaTestHead") { hideFlags = HideFlags.HideAndDontSave };
        var controllerObject = new GameObject("__MechaTestController") { hideFlags = HideFlags.HideAndDontSave };
        var targetObject = new GameObject("__MechaTestTarget") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            headObject.transform.SetPositionAndRotation(realHead.position, realHead.rotation);
            controllerObject.transform.SetPositionAndRotation(realHead.position + new Vector3(-0.2f, -0.3f, 0.35f), realHead.rotation);
            body.enabled = false;
            body.head = headObject.transform;
            body.ApplyPose();
            var probe = testObject.AddComponent<MechaVRHandTarget>();
            probe.trackingSpace = body.bodyTrackingSpace;
            probe.mechaSpace = body.mechaHeadSpace;
            probe.controller = controllerObject.transform;
            probe.target = targetObject.transform;
            probe.alignPositionToController = true;
            probe.movementGain = 1f;
            probe.Calibrate();
            probe.ApplyTargetPose();
            Vector3 initialTarget = probe.target.position;
            Vector3 initialRoot = body.transform.position;
            Vector3 step = new Vector3(1f, 0f, 0.5f);
            headObject.transform.position += step;
            controllerObject.transform.position += step;
            body.ApplyPose();
            probe.ApplyTargetPose();
            float bodyError = Vector3.Distance(body.transform.position - initialRoot, step);
            float handTranslationError = Vector3.Distance(probe.target.position - initialTarget, step);
            Vector3 beforeHandMove = probe.target.position;
            Vector3 handStep = new Vector3(0f, 0.15f, 0.1f);
            controllerObject.transform.position += handStep;
            probe.ApplyTargetPose();
            float handError = Vector3.Distance(probe.target.position - beforeHandMove, handStep * body.transform.lossyScale.x);
            Vector3 beforeTurn = probe.target.position;
            headObject.transform.rotation = Quaternion.AngleAxis(45f, Vector3.up) * headObject.transform.rotation;
            body.ApplyPose();
            probe.ApplyTargetPose();
            float turnError = Vector3.Distance(probe.target.position, beforeTurn);
            float anchorError = Vector3.Distance(body.mechaHeadSpace.position, headObject.transform.position);
            bool passed = bodyError < 0.0001f && handTranslationError < 0.0001f && handError < 0.0001f && turnError < 0.0001f && anchorError < 0.0001f;
            lastResult = "BODY FOLLOW " + (passed ? "PASS" : "FAIL") + " body-translation-error=" + bodyError.ToString("F7")
                + " hand-translation-error=" + handTranslationError.ToString("F7") + " independent-hand-error=" + handError.ToString("F7")
                + " head-turn-hand-error=" + turnError.ToString("F7") + " head-anchor-error=" + anchorError.ToString("F7");
            File.WriteAllText(Path.Combine(Folder, "body-follow-test.txt"), DateTime.UtcNow.ToString("O") + Environment.NewLine + lastResult);
            Debug.Log("[MechaVR] " + lastResult);
        }
        finally
        {
            body.head = realHead;
            body.enabled = bodyEnabled;
            body.ApplyPose();
            UnityEngine.Object.DestroyImmediate(testObject);
            UnityEngine.Object.DestroyImmediate(headObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    private static void TestIK()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) { lastResult = "IK test requires unpaused Play mode"; return; }
        var drivers = Drivers();
        var enabledStates = drivers.Select(d => d.enabled).ToArray();
        var result = new StringBuilder("IK TEST: ");
        try
        {
            foreach (var d in drivers) d.enabled = false;
            foreach (var d in drivers)
            {
                var ik = d.GetComponent<TwoBoneIKConstraint>();
                var b = d.GetComponentInParent<RigBuilder>();
                if (!ik || !b || !b.graph.IsValid() || !((IRigConstraint)ik).IsValid())
                { result.Append(d.name + " FAIL: missing/invalid IK graph; "); continue; }
                Vector3 targetPosition = ik.data.target.position;
                Quaternion targetRotation = ik.data.target.rotation;
                var animator = b.GetComponent<Animator>();
                AnimatorCullingMode oldCulling = animator.cullingMode;
                Transform[] bones = { ik.data.root, ik.data.mid, ik.data.tip };
                Vector3[] positions = bones.Select(t => t.localPosition).ToArray();
                Quaternion[] rotations = bones.Select(t => t.localRotation).ToArray();
                try
                {
                    b.Evaluate(0f);
                    Vector3 before = ik.data.tip.position;
                    float step = 0.06f * (d.mechaSpace ? d.mechaSpace.lossyScale.y : 1f);
                    ik.data.target.position = targetPosition + Vector3.up * step;
                    b.Evaluate(1f / 60f);
                    float moved = Vector3.Distance(before, ik.data.tip.position);
                    result.Append(d.name + " original-culling=" + oldCulling + " moved=" + moved.ToString("F6"));
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    b.Evaluate(1f / 60f);
                    float alwaysMoved = Vector3.Distance(before, ik.data.tip.position);
                    result.Append(" AlwaysAnimate-moved=" + alwaysMoved.ToString("F6") + " expected=" + step.ToString("F6") + (alwaysMoved > step * 0.2f ? " PASS; " : " FAIL; "));
                }
                finally
                {
                    ik.data.target.SetPositionAndRotation(targetPosition, targetRotation);
                    b.Evaluate(0f);
                    for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; }
                    animator.cullingMode = oldCulling;
                }
            }
        }
        finally { for (int i = 0; i < drivers.Length; i++) if (drivers[i]) drivers[i].enabled = enabledStates[i]; }
        lastResult = result.ToString();
        Debug.Log("[MechaVR] " + lastResult);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "ik-test.txt"), DateTime.UtcNow.ToString("O") + Environment.NewLine + lastResult);
    }
}

[CustomEditor(typeof(MechaVRHandTarget))]
public sealed class MechaVRHandTargetInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var driver = (MechaVRHandTarget)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(driver.Status, MessageType.Info);
        EditorGUILayout.LabelField("Calibration count", driver.CalibrationCount.ToString());
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Calibrate after 3 seconds")) driver.BeginCalibration();
            if (GUILayout.Button("Calibrate both arms now")) MechaVRDiagnostics.CalibrateBoth();
        }
        if (EditorApplication.isPlaying) Repaint();
    }
}
