using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AnseongSteel.PlayerMotion;

namespace AnseongSteel.RobotPlay.Editor
{
    public static class IntegratedArmRepair
    {
        public const string ScenePath = "Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity";
        public const string ModelPath = "Assets/_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026.fbx";
        public const string Output = "ArmIntegrationReview";
        public static void Inspect()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(ScenePath);
            var session = UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();
            var report = new StringBuilder();
            report.AppendLine("Baseline saved scene; no builder/UpdateOwnedScene executed.");
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                report.AppendLine("ROOT " + root.name + " active=" + root.activeSelf);
            report.AppendLine("COMBAT ROOT " + session.robotRoot.position);
            foreach (var renderer in session.robotRoot.GetComponentsInChildren<Renderer>(true))
                report.AppendLine("PLAYER RENDERER " + renderer.name + " active=" + renderer.gameObject.activeInHierarchy + " enabled=" + renderer.enabled + " layer=" + renderer.gameObject.layer);
            for (int side = 0; side < 2; side++)
            {
                var a = session.contact.arms[side];
                report.AppendLine("CONTACT " + side + " upper=" + a.upperArm.position + " elbow=" + a.forearm.position + " hand=" + a.hand.position);
            }
            var candidate = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                foreach (var t in candidate.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mixamorig:") && (t.name.EndsWith("Arm") || t.name.Contains("ForeArm") || t.name.EndsWith("Hand"))))
                    report.AppendLine("V026 BONE " + t.name + " position=" + t.position + " local=" + t.localPosition);
                var skins = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                report.AppendLine("V026 SKINS " + skins.Length);
                foreach (var r in skins.Take(8)) report.AppendLine("SKIN " + r.name + " verts=" + r.sharedMesh.vertexCount + " bones=" + r.bones.Length + " weights=" + r.sharedMesh.boneWeights.Length + " bounds=" + r.bounds);
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
            foreach (var g in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (PrefabUtility.IsPrefabAssetMissing(g.gameObject)) report.AppendLine("MISSING PREFAB " + g.name + " active=" + g.gameObject.activeInHierarchy);
            File.WriteAllText(Output + "/before-inspect.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
