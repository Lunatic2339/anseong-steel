using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using AnseongSteel.PlayerMotion;

namespace AnseongSteel.RobotPlay.Editor
{
    // Repairs only the combat body's visual child. Never invokes a scene builder.
    public static class IntegratedArmRepairBuilder
    {
        public const string Root = "Assets/_Project/05_Prefabs/IntegratedPlayerArmsV026";
        public const string VisualName = "Player v026 - blue combat arms";
        [MenuItem("Anseong Steel/Robot Play/Repair integrated v026 visual arms")]
        public static void Repair()
        {
            EditorSceneManager.OpenScene(IntegratedArmRepair.ScenePath);
            var session = UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();
            if (!session || !session.contact || session.contact.arms.Length != 2) throw new InvalidOperationException("Expected existing combat authority and two contact arms.");
            if (session.robotRoot.Find(VisualName)) throw new InvalidOperationException("Visual already exists; use saved-scene validation instead of rebuilding.");
            var before = ProtectedReferences(session);
            var oldVisuals = session.robotRoot.Cast<Transform>().Where(t => PrefabUtility.IsPrefabAssetMissing(t.gameObject) || t.GetComponent<RobotRigVisual>()).ToArray();
            if (oldVisuals.Length != 1) throw new InvalidOperationException("Expected exactly one broken combat visual child, found " + oldVisuals.Length);
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(IntegratedArmRepair.ModelPath);
            if (!asset) throw new InvalidOperationException("Normal v026 model not imported.");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            visual.name = VisualName;
            visual.transform.SetParent(session.robotRoot, false);
            visual.transform.localScale = Vector3.one * 6;
            try
            {
                var transforms = visual.GetComponentsInChildren<Transform>(true);
                Transform Bone(string name) => transforms.Single(t => t.name == "mixamorig:" + name);
                if (Bone("LeftArm").position.x > Bone("RightArm").position.x) visual.transform.localRotation = Quaternion.Euler(0, 180, 0);
                foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                var rig = visual.AddComponent<RobotRigVisual>();
                rig.session = session;
                var materials = Palette();
                var report = new List<string>();
                for (int side = 0; side < 2; side++)
                {
                    string prefix = side == 0 ? "Left" : "Right";
                    var upper = Bone(prefix + "Arm"); var lower = Bone(prefix + "ForeArm"); var hand = Bone(prefix + "Hand");
                    var fist = hand.gameObject.AddComponent<DefaultFistPose>();
                    // DefaultFistPose needs the imported, relaxed finger rest before rebinding.
                    fist.Configure(hand, prefix);
                    for (int joint = 0; joint < fist.joints.Length; joint++) fist.joints[joint].localRotation = fist.restRotations[joint];
                    rig.arms[side] = new RobotRigVisual.Binding
                    {
                        upper = upper, lower = lower, hand = hand,
                        upperAxis = upper.InverseTransformDirection(lower.position - upper.position),
                        lowerAxis = lower.InverseTransformDirection(hand.position - lower.position),
                        // Contact follower's longitudinal axis is +Y, palm-back axis +Z.
                        handOffset = Quaternion.LookRotation(Vector3.up, Vector3.forward) * Quaternion.Inverse(Quaternion.LookRotation(fist.palmForwardLocal, -fist.palmInwardLocal))
                    };
                    var skin = BuildArmSkin(visual, upper, side, materials);
                    report.Add(prefix + " visual skin vertices=" + skin.sharedMesh.vertexCount + " triangles=" + skin.sharedMesh.triangles.Length / 3 + " bones=" + skin.bones.Length);
                }
                // Imported renderers are retained as source references but never rendered.
                foreach (var r in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (!r.name.StartsWith("Combat ")) r.enabled = false;
                foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 27;
                foreach (var fist in visual.GetComponentsInChildren<DefaultFistPose>()) fist.Apply();
                rig.Synchronize();
                foreach (var r in visual.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled))
                {
                    var baked = new Mesh(); r.BakeMesh(baked);
                    r.localBounds = baked.bounds;
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                // Only the unusable visual instance is replaced; source assets and fixtures remain.
                foreach (var old in oldVisuals) UnityEngine.Object.DestroyImmediate(old.gameObject);
                if (ProtectedReferences(session) != before) throw new InvalidOperationException("Protected gameplay/scene references changed.");
                foreach (var t in visual.GetComponentsInChildren<Transform>(true)) { PrefabUtility.RecordPrefabInstancePropertyModifications(t); PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject); }
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), IntegratedArmRepair.ScenePath);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(IntegratedArmRepair.Output);
                File.WriteAllText(IntegratedArmRepair.Output + "/repair.txt", string.Join("\n", report) + "\nProtected references unchanged. Normal v026 source GUID=" + AssetDatabase.AssetPathToGUID(IntegratedArmRepair.ModelPath));
                Debug.Log("ARM_REPAIR_SAVED " + string.Join("; ", report));
            }
            catch { UnityEngine.Object.DestroyImmediate(visual); throw; }
        }
        static string ProtectedReferences(RobotPlaySession s)
        {
            // Instance IDs survive this narrow edit. No input, gameplay or cockpit component is added/edited.
            return EditorJsonUtility.ToJson(s) + EditorJsonUtility.ToJson(s.contact) + EditorJsonUtility.ToJson(s.robotRoot.GetComponent<CapsuleCollider>())
                + string.Join("|", s.contact.arms.Select(a => a.upperArm.GetInstanceID() + "/" + a.forearm.GetInstanceID() + "/" + a.hand.GetInstanceID()))
                + string.Join("|", UnityEngine.Object.FindObjectsByType<CockpitPilotAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(a => EditorJsonUtility.ToJson(a)))
                + EditorJsonUtility.ToJson(s.GetComponent<IntegratedCombatHud>()) + EditorJsonUtility.ToJson(s.GetComponent<IntegratedBossEncounter>()) + EditorJsonUtility.ToJson(s.GetComponent<IntegratedCockpitSelector>());
        }
        static Material[] Palette()
        {
            string[] names = { "BlueArmor", "BlueTrim", "DarkMetal", "RedInsert" };
            Color[] colors = { new Color(.035f,.18f,.52f), new Color(.02f,.4f,1), new Color(.075f,.095f,.12f), new Color(.8f,.045f,.025f) };
            return names.Select((name, i) =>
            {
                string path = Root + "/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", colors[i]); m.SetFloat("_Metallic", .65f); m.SetFloat("_Smoothness", .45f);
                return m;
            }).ToArray();
        }
        static int MaterialGroup(Material m)
        {
            var c = m && m.HasProperty("_Color") ? m.GetColor("_Color") : Color.gray;
            if (c.r > c.g * 1.8f && c.r > c.b * 1.8f) return 3;
            float brightness = Mathf.Max(c.r, Mathf.Max(c.g,c.b));
            return brightness < .25f ? 2 : brightness > .85f ? 1 : 0;
        }
        static SkinnedMeshRenderer BuildArmSkin(GameObject model, Transform upper, int side, Material[] palette)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var weights = new List<BoneWeight>();
            var bones = new List<Transform>(); var groups = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => !r.name.StartsWith("Combat ")).ToArray())
            {
                var mesh = r.sharedMesh; var originalBones = r.bones; var originalWeights = mesh.boneWeights;
                bool InArm(int index) => index >= 0 && index < originalBones.Length && originalBones[index] && originalBones[index].IsChildOf(upper);
                bool Included(BoneWeight w) => (w.weight0 > .2f && InArm(w.boneIndex0)) || (w.weight1 > .2f && InArm(w.boneIndex1)) || (w.weight2 > .2f && InArm(w.boneIndex2)) || (w.weight3 > .2f && InArm(w.boneIndex3));
                var selected = new List<(int,int)>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    for (int t = 0; t < tris.Length; t += 3)
                        if (Included(originalWeights[tris[t]]) && Included(originalWeights[tris[t+1]]) && Included(originalWeights[tris[t+2]]))
                        { selected.Add((tris[t], sub)); selected.Add((tris[t+1], sub)); selected.Add((tris[t+2], sub)); }
                }
                if (selected.Count == 0) continue;
                int RemapBone(int index)
                {
                    if (index < 0 || index >= originalBones.Length || !originalBones[index]) throw new InvalidOperationException("Invalid source skin bone.");
                    var bone = originalBones[index]; int mapped = bones.IndexOf(bone);
                    if (mapped < 0) { mapped = bones.Count; bones.Add(bone); } return mapped;
                }
                var geometry = model.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var normalMatrix = geometry.inverse.transpose;
                var oldVertices = mesh.vertices; var oldNormals = mesh.normals; var oldUv = mesh.uv;
                var vertexMap = new Dictionary<int,int>();
                foreach (var entry in selected)
                {
                    int index = entry.Item1;
                    if (!vertexMap.TryGetValue(index, out int mapped))
                    {
                        mapped = vertices.Count; vertexMap[index] = mapped;
                        vertices.Add(geometry.MultiplyPoint3x4(oldVertices[index])); normals.Add(normalMatrix.MultiplyVector(oldNormals[index]).normalized);
                        uv.Add(index < oldUv.Length ? oldUv[index] : Vector2.zero);
                        var w = originalWeights[index];
                        weights.Add(new BoneWeight { boneIndex0=RemapBone(w.boneIndex0), boneIndex1=w.weight1>0?RemapBone(w.boneIndex1):0, boneIndex2=w.weight2>0?RemapBone(w.boneIndex2):0, boneIndex3=w.weight3>0?RemapBone(w.boneIndex3):0, weight0=w.weight0, weight1=w.weight1, weight2=w.weight2, weight3=w.weight3 });
                    }
                    groups[MaterialGroup(r.sharedMaterials[entry.Item2])].Add(mapped);
                }
            }
            if (vertices.Count == 0 || bones.Count == 0) throw new InvalidOperationException("No v026 arm geometry.");
            var result = new Mesh { name = side == 0 ? "v026 left arm" : "v026 right arm", indexFormat=IndexFormat.UInt32 };
            result.SetVertices(vertices); result.SetNormals(normals); result.SetUVs(0,uv); result.boneWeights=weights.ToArray();
            // The v026 review builder also rebinds rigid pieces to the imported rest skeleton.
            result.bindposes=bones.Select(b => b.worldToLocalMatrix * model.transform.localToWorldMatrix).ToArray();
            var used = Enumerable.Range(0,4).Where(i => groups[i].Count > 0).ToArray(); result.subMeshCount=used.Length;
            for (int i=0;i<used.Length;i++) result.SetTriangles(groups[used[i]],i);
            result.RecalculateBounds(); AssetDatabase.CreateAsset(result,Root+(side==0?"/LeftArm.asset":"/RightArm.asset"));
            var skin=new GameObject(side==0?"Combat Left Arm":"Combat Right Arm").AddComponent<SkinnedMeshRenderer>();
            skin.transform.SetParent(model.transform,false); skin.sharedMesh=result; skin.bones=bones.ToArray(); skin.rootBone=model.transform;
            skin.sharedMaterials=used.Select(i=>palette[i]).ToArray(); skin.localBounds=result.bounds; skin.updateWhenOffscreen=true;
            return skin;
        }
    }
}
