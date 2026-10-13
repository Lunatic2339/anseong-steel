using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.RobotPlay.Editor
{
    public static class RobotLocalPunchScene
    {
        public const string ScenePath=RobotPlayBuilder.Root+"/Scenes/LocalPunchTest.unity";
        [MenuItem("Anseong Steel/Robot Play/Create local punch test scene")]
        public static void Build()
        {
            var scene=EditorSceneManager.OpenScene(RobotPlayBuilder.ScenePath);
            var session=Object.FindFirstObjectByType<RobotPlaySession>();
            var input=Object.FindFirstObjectByType<RobotPilotInput>();
            input.source=PrototypeInput.XRLocalBoth;input.startup=RobotPilotInput.Startup.Offline;
            session.trainingWindow=true;session.tuning.standaloneHookDamage=true;
            session.Pose(0,Vector3.zero);session.Pose(1,Vector3.zero);
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("LOCAL_PUNCH_SCENE_READY "+ScenePath);
        }
    }
}