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
        {Begin(false);}
        [MenuItem("Tools/Boss Prototype/Record Sword Equip Verification")]
        public static void RunAndRecord()
        {Begin(true);}
        static void Begin(bool record)
        {
            SessionState.SetBool("BossRecordEquipment",record);
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            UnityEngine.Object.FindFirstObjectByType<BossModelDemo>().autoDemo=false;
            SessionState.SetBool("BossCombatVerification",true);EditorApplication.isPlaying=true;
        }
        static void OnPlay(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("BossCombatVerification",false))
            {
                var test=new GameObject("CombatVerification").AddComponent<BossCombatVerification>();
                test.recordEquipment=SessionState.GetBool("BossRecordEquipment",false);
                SessionState.SetBool("BossRecordEquipment",false);
            }
        }
    }
}
