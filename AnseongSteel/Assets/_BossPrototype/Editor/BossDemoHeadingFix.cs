using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnseongSteel.Bosses.Editor
{
    public static class BossDemoHeadingFix
    {
        [MenuItem("Tools/Boss Prototype/Align Visual Heading And Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var driver = UnityEngine.Object.FindFirstObjectByType<BossModelDemo>().driver;
            var actor = driver.transform;
            var model = driver.animator.transform;
            Vector3 position = model.position;
            Quaternion orientation = model.rotation;
            Vector3 forward = Vector3.ProjectOnPlane(model.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) throw new InvalidOperationException("Model forward is vertical.");
            // Transfer the user's visual composition to the actor. Keep the model's
            // exact world pose while making its forward match the movement root.
            actor.position = new Vector3(position.x, actor.position.y, position.z);
            actor.rotation = Quaternion.LookRotation(forward, Vector3.up);
            model.SetPositionAndRotation(position, orientation);
            if (Vector3.Distance(position, model.position) > .0001f || Quaternion.Angle(orientation, model.rotation) > .01f)
                throw new InvalidOperationException("Visual composition changed during alignment.");
            Debug.Log($"Aligned actor heading={actor.eulerAngles.y:F2}; preserved model world pose; visual/root error={Vector3.Angle(actor.forward, model.forward):F4}");
            EditorSceneManager.MarkSceneDirty(actor.gameObject.scene);
            EditorSceneManager.SaveScene(actor.gameObject.scene);
            BossModelSmokeTest.Run();
        }
    }
}
