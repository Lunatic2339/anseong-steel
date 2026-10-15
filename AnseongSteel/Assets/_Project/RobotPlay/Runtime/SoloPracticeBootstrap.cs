using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnseongSteel.RobotPlay
{
    static class SoloPracticeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscription()=>SceneManager.sceneLoaded-=Loaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;Attach(SceneManager.GetActiveScene());}
        static void Loaded(Scene scene,LoadSceneMode mode)=>Attach(scene);
        static void Attach(Scene scene)
        {
            if(Application.isBatchMode)return;
#if UNITY_EDITOR
            // Existing opt-in verification owns its saved scene and fixtures.
            if(UnityEditor.SessionState.GetBool("PersonalReach.Active",false)||UnityEditor.SessionState.GetBool("SavedSceneReach.Active",false)||UnityEditor.SessionState.GetBool("CalibrationUsability.Active",false))return;
#endif
            var settings=Resources.Load<SoloPracticeSettings>("SoloPracticeSettings");
            if(!settings||scene.path!=settings.scenePath)return;
            foreach(var session in Object.FindObjectsByType<RobotPlaySession>(FindObjectsSortMode.None))
                if(session.gameObject.scene==scene&&session.name==settings.sessionObjectName&&!session.GetComponent<SoloPracticeMode>())
                {var mode=session.gameObject.AddComponent<SoloPracticeMode>();mode.settings=settings;}
        }
    }
}
