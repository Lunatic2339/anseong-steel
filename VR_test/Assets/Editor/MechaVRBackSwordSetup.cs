using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRBackSwordSetup
{
    const string Request = "Library/BackSwordSetup.request";
    static MechaVRBackSwordSetup() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Configure();
    }
    [MenuItem("Tools/Mecha VR/Set Up Back Sword Draw Zone")]
    public static void Configure()
    {
        try
        {
            const string path = "Assets/Scenes/MainVRScene_BodyTracking.unity";
            var scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var equip = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MechaVRSwordEquip>(true)).Single();
            var body = equip.GetComponent<MechaVRBodyFollower>();
            var arm = body.GetComponentsInChildren<TwoBoneIKConstraint>(true).Single(a => a.data.tip && a.data.tip.name == "Hand.R");
            string backup = "ModelBackups/BackSwordScene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(path, backup + "/SceneOnDisk.unity");
            File.Copy(path + ".meta", backup + "/SceneOnDisk.unity.meta");
            if (!EditorSceneManager.SaveScene(scene, backup + "/SceneWithCurrentEdits.unity", true)) throw new IOException("Backup failed.");
            var zone = equip.transform.Find("RightShoulderSwordDrawZone");
            if (!zone)
            {
                var go = new GameObject("RightShoulderSwordDrawZone");
                Undo.RegisterCreatedObjectUndo(go, "Create back sword draw zone");
                zone = go.transform;
                Undo.SetTransformParent(zone, equip.transform, "Attach draw zone to torso");
            }
            Undo.RecordObject(zone, "Position back sword draw zone");
            zone.gameObject.layer = 2; // Ignore Raycast: no interaction rays hit the helper volume.
            zone.SetPositionAndRotation(arm.data.root.position + body.transform.up * .10f - body.transform.forward * .22f, body.transform.rotation);
            Vector3 scale = body.transform.lossyScale;
            zone.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            var box = zone.GetComponent<BoxCollider>();
            if (!box) box = Undo.AddComponent<BoxCollider>(zone.gameObject);
            Undo.RecordObject(box, "Set invisible draw volume");
            box.isTrigger = true;
            // Extend behind and below the shoulder while preserving the front/top edges.
            box.center = new Vector3(0f, -.10f, -.20f);
            box.size = new Vector3(.60f, .60f, .80f);
            box.enabled = true;
            Undo.RecordObject(equip, "Require shoulder reach for sword");
            equip.backDrawZone = box;
            equip.requireBackDrawZone = true;
            equip.entryHaptics = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(equip);
            EditorUtility.SetDirty(equip);
            MechaVRSwordSetup.ValidateInstalled();
            string validation = File.ReadAllText("Library/MechaSwordSetup.txt");
            if (!validation.StartsWith("PASS")) throw new InvalidOperationException(validation);
            if (zone.GetComponentsInChildren<Renderer>().Length != 0) throw new InvalidOperationException("Zone should be invisible.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            File.WriteAllText("Library/BackSwordSetup.txt", "PASS\nBackup=" + backup + "\nZone=" + zone.name + "\nCenter=" + zone.position.ToString("F4") + "\nSize=" + box.size + "\n" + validation);
            Debug.Log("[MechaVR] Back sword draw zone saved and validated.");
        }
        catch (Exception e)
        {
            File.WriteAllText("Library/BackSwordSetup.txt", "FAIL\n" + e);
            Debug.LogException(e);
        }
    }
}
