using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
    [InitializeOnLoad]
    public static class BossCombatVerificationRunner
    {
        static BossCombatVerificationRunner(){EditorApplication.playModeStateChanged+=OnPlay;}
        [MenuItem("Tools/Boss Prototype/Verify Independent Combat Patterns")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            UnityEngine.Object.FindFirstObjectByType<BossModelDemo>().autoDemo=false;
            SessionState.SetBool("BossCombatVerification",true);EditorApplication.isPlaying=true;
        }
        static void OnPlay(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("BossCombatVerification",false))
                new GameObject("CombatVerification").AddComponent<BossCombatVerification>();
        }
    }
}
