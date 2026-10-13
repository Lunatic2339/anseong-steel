using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRSwordSetup
{
    const string ScenePath = "Assets/Scenes/MainVRScene_BodyTracking.unity";
    const string PrefabPath = "Assets/Weapons/CapstoneSword/CapstoneSword.prefab";
    const string RequestPath = "Library/MechaSwordValidationV3.request";
    const string ReportPath = "Library/MechaSwordSetup.txt";
    static double nextAttempt;
    static MechaVRSwordSetup() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextAttempt) return;
        nextAttempt = EditorApplication.timeSinceStartup + 3;
        if (File.ReadAllText(RequestPath).Trim() == "validate")
        {
            File.Delete(RequestPath);
            ValidateInstalled();
            return;
        }
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isDirty)
        {
            File.WriteAllText(ReportPath, "WAIT: MainVRScene has unsaved edits. Save it before automatic setup.\n");
            return;
        }
        File.Delete(RequestPath);
        Configure();
    }

    [MenuItem("Tools/Mecha VR/Set Up Right Hand Sword")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (scene.isDirty) throw new InvalidOperationException("Save MainVRScene first to preserve its unsaved edits.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException("Sword prefab not found.");
            var ik = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TwoBoneIKConstraint>(true))
                .Single(c => c.data.tip && c.data.tip.name == "Hand.R");
            var hand = ik.data.tip;
            var index = hand.Cast<Transform>().Single(t => t.name.StartsWith("Index.01."));
            var little = hand.Cast<Transform>().Single(t => t.name.StartsWith("Little.01."));
            var body = hand.GetComponentInParent<MechaVRBodyFollower>();
            if (!body) throw new InvalidOperationException("MechaRoot body follower not found.");
            string backupFolder = "ModelBackups/SwordEquip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backupFolder);
            File.Copy(ScenePath, backupFolder + "/MainVRScene.unity");
            File.Copy(ScenePath + ".meta", backupFolder + "/MainVRScene.unity.meta");
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure right hand sword");
            Transform grip = hand.Find("RightSwordGrip");
            if (!grip)
            {
                var go = new GameObject("RightSwordGrip");
                Undo.RegisterCreatedObjectUndo(go, "Create right sword grip");
                Undo.SetTransformParent(go.transform, hand, "Attach grip to right hand bone");
                grip = go.transform;
                Vector3 a = hand.InverseTransformPoint(index.position);
                Vector3 b = hand.InverseTransformPoint(little.position);
                Vector3 midpoint = (a + b) * 0.5f;
                Vector3 bladeUp = (a - b).normalized;
                Vector3 bladeRight = Vector3.ProjectOnPlane(midpoint, bladeUp).normalized;
                if (bladeUp.sqrMagnitude < 0.5f || bladeRight.sqrMagnitude < 0.5f)
                    throw new InvalidOperationException("Invalid hand anatomy for grip orientation.");
                Vector3 bladeForward = Vector3.Cross(bladeRight, bladeUp).normalized;
                grip.localPosition = midpoint * 0.7f + bladeForward * 0.025f;
                grip.localRotation = Quaternion.LookRotation(bladeForward, bladeUp);
                grip.localScale = Vector3.one;
            }
            var equip = body.GetComponent<MechaVRSwordEquip>();
            if (!equip) equip = Undo.AddComponent<MechaVRSwordEquip>(body.gameObject);
            Undo.RecordObject(equip, "Assign sword prefab and hand grip");
            equip.rightHandGrip = grip;
            equip.swordPrefab = prefab;
            equip.swordGripPoint = new Vector3(0f, 0.875f, 0f);
            equip.localEulerOffset = new Vector3(0f, 0f, 180f);
            foreach (var jump in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump.JumpProvider>(true)))
            {
                Undo.RecordObject(jump, "Disable sword button jump");
                jump.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(jump);
            }
            equip.enabled = true;
            EditorUtility.SetDirty(equip);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            Undo.CollapseUndoOperations(group);
            string validation = Validate(prefab, grip.localPosition, grip.localRotation, equip);
            File.WriteAllText(ReportPath, "PASS\nScene=" + ScenePath + "\nBackup=" + backupFolder
                + "\nHand=" + hand.name + "\nGripPosition=" + grip.localPosition.ToString("F6")
                + "\nGripRotation=" + grip.localEulerAngles.ToString("F3") + "\n" + validation);
            Debug.Log("[MechaVR] Right hand sword configured; A toggles the sword. " + validation);
        }
        catch (Exception e) { File.WriteAllText(ReportPath, "FAIL\n" + e); Debug.LogException(e); }
        finally { if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
    }

    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static string Validate(GameObject prefab, Vector3 position, Quaternion rotation, MechaVRSwordEquip settings)
    {
        // Synthetic XR controllers exercise the real binding and performed callback.
        const string layout = "CapstoneSwordTestController";
        // Preview scenes avoid changing or requiring saved user scenes.
        Scene preview = EditorSceneManager.NewPreviewScene();
        var manager = typeof(InputSystem).GetField("s_Manager", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        var playerUpdates = manager.GetType().GetProperty("runPlayerUpdatesInEditMode");
        bool previousPlayerUpdates = (bool)playerUpdates.GetValue(manager);
        InputDevice left = null, right = null;
        GameObject root = null;
        MechaVRSwordEquip equip = null;
        try
        {
            playerUpdates.SetValue(manager, true);
            InputSystem.RegisterLayout("{\"name\":\"" + layout + "\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"primaryButton\",\"layout\":\"Button\",\"format\":\"BIT\"},{\"name\":\"secondaryButton\",\"layout\":\"Button\",\"format\":\"BIT\"}]}");
            left = InputSystem.AddDevice(layout);
            right = InputSystem.AddDevice(layout);
            InputSystem.SetDeviceUsage(left, UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, UnityEngine.InputSystem.CommonUsages.RightHand);
            root = new GameObject("SwordValidation");
            SceneManager.MoveGameObjectToScene(root, preview);
            var hand = new GameObject("Hand.R").transform;
            hand.SetParent(root.transform, false);
            var grip = new GameObject("RightSwordGrip").transform;
            grip.SetParent(hand, false);
            grip.SetLocalPositionAndRotation(position, rotation);
            equip = root.AddComponent<MechaVRSwordEquip>();
            equip.rightHandGrip = grip;
            equip.swordPrefab = prefab;
            equip.swordGripPoint = settings.swordGripPoint;
            equip.localPositionOffset = settings.localPositionOffset;
            equip.localEulerOffset = settings.localEulerOffset;
            equip.swordScale = settings.swordScale;
            var zone = new GameObject("BackDrawZone").AddComponent<BoxCollider>();
            zone.transform.SetParent(root.transform, false);
            zone.isTrigger = true;
            zone.size = new Vector3(.4f, .4f, .4f);
            zone.transform.position = grip.position;
            equip.backDrawZone = zone;
            equip.requireBackDrawZone = true;
            equip.entryHaptics = false;
            Lifecycle(equip, "OnEnable");
            Check(!equip.EquippedSword, "Sword must start unequipped.");
            Press(left, "primaryButton", 1f);
            Check(!equip.EquippedSword, "Left X must not equip.");
            Press(right, "secondaryButton", 1f);
            Check(!equip.EquippedSword, "Right B must not equip.");
            zone.transform.position += Vector3.back * 2f;
            Press(right, "primaryButton", 1f);
            Check(!equip.EquippedSword, "A outside the draw zone must not equip.");
            zone.transform.position = grip.position;
            InputSystem.Update();
            Check(!equip.EquippedSword, "Entering with A held must not equip.");
            Press(right, "primaryButton", 0f);
            Press(right, "primaryButton", 1f);
            Check(equip.EquippedSword && grip.childCount == 1, "Right A must equip exactly one sword.");
            var first = equip.EquippedSword;
            Press(right, "primaryButton", 0f);
            Check(equip.EquippedSword == first, "Releasing A must retain sword.");
            InputSystem.Update();
            Check(equip.EquippedSword == first, "Sword changed without a new press.");
            hand.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(30, 70, 15));
            Check(Vector3.Distance(first.transform.TransformPoint(equip.swordGripPoint), grip.position) < 0.00001f, "Handle lost hand alignment.");
            Check(Vector3.Dot(-first.transform.up, grip.up) > 0.9999f, "Blade failed to follow hand rotation.");
            Press(right, "primaryButton", 1f);
            Check(equip.EquippedSword == first, "A outside the draw zone must not stow.");
            Press(right, "primaryButton", 0f);
            // A rotated torso-mounted box must use its own local axes.
            zone.transform.SetPositionAndRotation(grip.position, Quaternion.Euler(0, 125, 0));
            Check(equip.IsHandInDrawZone, "Rotated zone rejected its center.");
            Press(right, "primaryButton", 1f);
            Check(!equip.EquippedSword && grip.childCount == 0, "Second A must remove the sword.");
            InputSystem.Update();
            Check(!equip.EquippedSword, "Holding A must not re-equip.");
            Press(right, "primaryButton", 0f);
            Press(right, "primaryButton", 1f);
            Check(equip.EquippedSword && grip.childCount == 1, "Third A must re-equip one sword.");
            equip.Unequip();
            Lifecycle(equip, "OnDisable");
            Press(right, "primaryButton", 0f);
            Press(right, "primaryButton", 1f);
            Check(grip.childCount == 0, "Disabled equipment still responds.");
            Lifecycle(equip, "OnEnable");
            Press(right, "primaryButton", 0f);
            Press(right, "primaryButton", 1f);
            Check(grip.childCount == 1, "Input did not recover after re-enable.");
            return "Tests: outside A ignored for equip/stow; entering with held A ignored; rotated zone; start hidden; left X/right B ignored; right A equips; release retains; second A removes; held A stable; third A re-equips; hand position/rotation follow; disable/re-enable PASS\n";
        }
        finally
        {
            if (equip)
            {
                Lifecycle(equip, "OnDisable");
                if (equip.EquippedSword) UnityEngine.Object.DestroyImmediate(equip.EquippedSword);
            }
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (left != null) InputSystem.RemoveDevice(left);
            if (right != null) InputSystem.RemoveDevice(right);
            InputSystem.RemoveLayout(layout);
            playerUpdates.SetValue(manager, previousPlayerUpdates);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    static void Lifecycle(MechaVRSwordEquip equip, string method)
    {
        typeof(MechaVRSwordEquip).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(equip, null);
    }
    [MenuItem("Tools/Mecha VR/Validate Right Hand Sword")]
    public static void ValidateInstalled()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var jumps = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump.JumpProvider>(true)).ToArray();
            Check(jumps.Length > 0 && jumps.All(j => !j.enabled), "Scene jump provider must be disabled.");
            var equip = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MechaVRSwordEquip>(true)).Single();
            Check(equip.rightHandGrip && equip.rightHandGrip.parent.name == "Hand.R", "Grip is not attached to right hand.");
            Check(equip.requireBackDrawZone && equip.backDrawZone && equip.backDrawZone.isTrigger, "Back draw zone is missing.");
            Check(equip.backDrawZone.transform.parent == equip.transform, "Draw zone must follow torso, not arm.");
            string result = Validate(equip.swordPrefab, equip.rightHandGrip.localPosition, equip.rightHandGrip.localRotation, equip);
            File.WriteAllText(ReportPath, "PASS\nScene=" + ScenePath + "\nGrip=" + equip.rightHandGrip.localPosition.ToString("F6") + "\n" + result);
            Debug.Log("[MechaVR Sword] " + result);
        }
        catch (Exception e) { File.WriteAllText(ReportPath, "FAIL\n" + e); Debug.LogException(e); }
        finally { if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
    }
    static void Press(InputDevice device, string control, float value)
    {
        using (StateEvent.From(device, out var inputEvent))
        {
            device.GetChildControl<ButtonControl>(control).WriteValueIntoEvent(value, inputEvent);
            InputSystem.QueueEvent(inputEvent);
        }
        InputSystem.Update();
    }
}



