using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

public static class MechaVRBossSetup
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "MechaVR_Diagnostics_20260929");

    [MenuItem("Tools/Mecha VR/Set Up Boss Contact")]
    public static void ConfigureMenu() => Debug.Log("[MechaVR] " + Configure());

    public static string Configure()
    {
        if (EditorApplication.isPlaying) return "Stop Play before configuring boss contact";
        var scene = SceneManager.GetActiveScene();
        var drivers = UnityEngine.Object.FindObjectsByType<MechaVRHandTarget>(FindObjectsInactive.Include)
            .Where(d => d.gameObject.scene == scene).ToArray();
        var bossObjects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .Where(t => t.gameObject.scene == scene && t.name == "boss").ToArray();
        if (drivers.Length != 2 || bossObjects.Length != 1) throw new InvalidOperationException("Expected two arm drivers and exactly one object named boss in this scene.");
        Transform bossObject = bossObjects[0];
        if (!bossObject.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger))
            throw new InvalidOperationException("boss needs an enabled non-trigger Collider.");
        var builder = drivers[0].GetComponentInParent<RigBuilder>();
        if (!builder || !builder.transform.parent || drivers.Any(d => d.GetComponentInParent<RigBuilder>() != builder))
            throw new InvalidOperationException("Both arms must share a RigBuilder below MechaRoot.");
        var definitions = drivers.Select(d =>
        {
            var ik = d.GetComponent<TwoBoneIKConstraint>();
            if (!ik || !((IRigConstraint)ik).IsValid()) throw new InvalidOperationException("Invalid arm IK: " + d.name);
            return new MechaVRBossContact.Arm { label = ik.data.root.name.EndsWith(".L") ? "Left" : "Right", upperArm = ik.data.root, forearm = ik.data.mid, hand = ik.data.tip };
        }).OrderBy(a => a.label).ToArray();
        var bounds = FitVolumes(builder.transform, definitions);
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure Mecha Boss Contact");
        var marker = bossObject.GetComponent<MechaVRBossTarget>();
        if (!marker) marker = Undo.AddComponent<MechaVRBossTarget>(bossObject.gameObject);
        var solver = builder.transform.parent.GetComponent<MechaVRBossContact>();
        if (!solver) solver = Undo.AddComponent<MechaVRBossContact>(builder.transform.parent.gameObject);
        Undo.RecordObject(solver, "Assign Boss Contact Volumes");
        var report = new StringBuilder("BOSS CONTACT SETUP\n");
        report.AppendLine("boss=" + bossObject.name + " position=" + bossObject.position.ToString("F4"));
        foreach (var arm in definitions)
        {
            var volumes = new List<BoxCollider>();
            foreach (var bone in new[] { arm.upperArm.parent, arm.upperArm, arm.forearm, arm.hand })
            {
                if (!bounds.TryGetValue(bone, out Bounds fitted)) continue;
                Transform holder = bone.Find("Boss Contact Volume");
                if (!holder)
                {
                    var go = new GameObject("Boss Contact Volume");
                    Undo.RegisterCreatedObjectUndo(go, "Create Arm Contact Volume");
                    holder = go.transform;
                    Undo.SetTransformParent(holder, bone, "Parent Arm Contact Volume");
                }
                Undo.RecordObject(holder, "Position Arm Contact Volume");
                holder.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                holder.localScale = Vector3.one;
                var box = holder.GetComponent<BoxCollider>();
                if (!box) box = Undo.AddComponent<BoxCollider>(holder.gameObject);
                Undo.RecordObject(box, "Fit Arm Contact Volume");
                box.center = fitted.center;
                box.size = Vector3.Max(fitted.size, Vector3.one * 0.015f) + Vector3.one * 0.004f;
                box.isTrigger = true;
                box.enabled = true; // native geometry must be active for ComputePenetration
                holder.gameObject.layer = 2; // Ignore Raycast
                volumes.Add(box);
                report.AppendLine(arm.label + " " + bone.name + " center=" + box.center.ToString("F5") + " size=" + box.size.ToString("F5"));
            }
            if (volumes.Count < 3) throw new InvalidOperationException("Could not fit all arm parts for " + arm.label);
            arm.volumes = volumes.ToArray();
        }
        solver.boss = marker;
        solver.arms = definitions;
        solver.maxSubsteps = 192;
        solver.minimumHitSpeed = 0.2f;
        solver.hitCooldown = 0.12f;
        solver.enabled = true;
        EditorUtility.SetDirty(solver);
        PrefabUtility.RecordPrefabInstancePropertyModifications(solver);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "boss-setup.txt"), report.ToString());
        return "Saved boss contact: " + definitions.Sum(a => a.volumes.Length) + " fitted volumes, independent left/right arms";
    }

    private static Dictionary<Transform, Bounds> FitVolumes(Transform model, MechaVRBossContact.Arm[] arms)
    {
        var result = new Dictionary<Transform, Bounds>();
        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!renderer.sharedMesh) continue;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                var vertices = baked.vertices;
                var weights = renderer.sharedMesh.boneWeights;
                var bones = renderer.bones;
                if (vertices.Length != weights.Length) throw new InvalidOperationException("Skin weight/vertex count mismatch: " + renderer.name);
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight w = weights[i];
                    int index = w.boneIndex0;
                    float weight = w.weight0;
                    if (w.weight1 > weight) { index = w.boneIndex1; weight = w.weight1; }
                    if (w.weight2 > weight) { index = w.boneIndex2; weight = w.weight2; }
                    if (w.weight3 > weight) { index = w.boneIndex3; weight = w.weight3; }
                    if (weight <= 0 || index >= bones.Length || !bones[index]) continue;
                    Transform owner = null;
                    foreach (var arm in arms)
                    {
                        if (bones[index].IsChildOf(arm.hand)) owner = arm.hand;
                        else if (bones[index].IsChildOf(arm.forearm)) owner = arm.forearm;
                        else if (bones[index].IsChildOf(arm.upperArm)) owner = arm.upperArm;
                        else if (arm.upperArm.parent && bones[index].IsChildOf(arm.upperArm.parent)) owner = arm.upperArm.parent;
                        if (owner) break;
                    }
                    if (!owner) continue;
                    Vector3 p = owner.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                    if (result.TryGetValue(owner, out Bounds b)) { b.Encapsulate(p); result[owner] = b; }
                    else result.Add(owner, new Bounds(p, Vector3.zero));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        return result;
    }

    public static void AppendReport(StringBuilder sb)
    {
        foreach (var solver in UnityEngine.Object.FindObjectsByType<MechaVRBossContact>(FindObjectsInactive.Include))
        {
            sb.AppendLine("BOSS CONTACT enabled=" + solver.isActiveAndEnabled + " boss=" + (solver.boss ? solver.boss.name : "NONE")
                + " body-blocked=" + solver.BodyBlocked + " recovery-failed=" + solver.RecoveryFailed + " queries=" + solver.QueriesLastFrame);
            foreach (var arm in solver.arms)
                sb.AppendLine("  " + arm.label + " blocked=" + arm.IsBlocked + " part=" + arm.ContactPart + " volumes=" + arm.volumes.Length);
            if (solver.boss) sb.AppendLine("  hits=" + solver.boss.HitCount + " last-arm=" + solver.boss.LastArm + " speed=" + solver.boss.LastSpeed.ToString("F3"));
        }
    }
}

[CustomEditor(typeof(MechaVRBossContact))]
public sealed class MechaVRBossContactInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var solver = (MechaVRBossContact)target;
        foreach (var arm in solver.arms)
            EditorGUILayout.LabelField(arm.label, arm.IsBlocked ? "Blocked: " + arm.ContactPart : "Following controller");
        if (solver.RecoveryFailed) EditorGUILayout.HelpBox("The avatar is trapped between boss colliders. Move the spawn point or colliders apart.", MessageType.Warning);
        if (EditorApplication.isPlaying) Repaint();
    }
}

[CustomEditor(typeof(MechaVRBossTarget))]
public sealed class MechaVRBossTargetInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var boss = (MechaVRBossTarget)target;
        EditorGUILayout.LabelField("Hit count", boss.HitCount.ToString());
        EditorGUILayout.LabelField("Last arm", boss.LastArm ?? "");
        EditorGUILayout.LabelField("Last speed", boss.LastSpeed.ToString("F2") + " m/s");
        if (EditorApplication.isPlaying) Repaint();
    }
}
