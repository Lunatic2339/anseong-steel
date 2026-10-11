using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnseongSteel.PlayerMotion;
namespace AnseongSteel.RobotPlay.Editor
{
    public static class IntegratedPlayBuilder
    {
        public const string ScenePath="Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity";
        [MenuItem("Anseong Steel/Robot Play/Open sunwoo Integrated Play")]
        public static void Open()
        {
            if(System.IO.File.Exists(ScenePath)){EditorSceneManager.OpenScene(ScenePath);return;}
            Build();
        }
        static void Layer(Transform t,int layer){t.gameObject.layer=layer;PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);foreach(Transform child in t)Layer(child,layer);}
        public static void UpdateOwnedScene()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();
            ConfigureGame(session);
            Layer(session.robotRoot,27);Layer(session.target.transform,27);
            var floor=GameObject.Find("Arena floor");if(floor)Layer(floor.transform,27);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        static void ConfigureGame(RobotPlaySession session)
        {
            session.tuning.requiresAttackButton=session.tuning.strictComboRules=session.tuning.postContactBoost=session.tuning.cooperativeMovement=true;
            session.tuning.standaloneHookDamage=true;session.externalBossTimeline=true;
            var body=session.robotRoot.GetComponent<CapsuleCollider>();if(!body)body=session.robotRoot.gameObject.AddComponent<CapsuleCollider>();
            body.center=new Vector3(0,1.1f,0);body.height=2.2f;body.radius=.5f;
            var encounter=session.GetComponent<IntegratedBossEncounter>();
            if(!encounter)
            {
                var old=session.target.transform.Find("Uploaded rig target visual (replace here)");if(old){old.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject);}
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_BossPrototype/Generated/BossMecha.prefab");
                if(!asset)throw new Exception("Existing BossMecha prefab missing; no replacement model will be generated.");
                var boss=(GameObject)PrefabUtility.InstantiatePrefab(asset);boss.name="Existing animated BossMecha";boss.transform.SetParent(session.target.transform,false);
                boss.transform.localPosition=Vector3.zero;boss.transform.localRotation=Quaternion.Euler(0,180,0);
                foreach(var c in boss.GetComponentsInChildren<Collider>(true)){c.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
                encounter=session.gameObject.AddComponent<IntegratedBossEncounter>();encounter.session=session;
                encounter.driver=boss.GetComponent<AnseongSteel.Bosses.BossAnimationDriver>();encounter.driver.hitRadius=.75f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(encounter.driver);
            }
            var bridge=session.GetComponent<IntegratedCombatHud>();
            var flow=session.GetComponent<IntegratedPlayFlow>();if(!flow)flow=session.gameObject.AddComponent<IntegratedPlayFlow>();
            var online=session.GetComponent<IntegratedOnlineState>();if(!online)online=session.gameObject.AddComponent<IntegratedOnlineState>();
            flow.session=session;flow.pilot=session.GetComponent<RobotPilotInput>();flow.bridge=bridge;flow.boss=encounter;
            var feedback=session.GetComponent<IntegratedFeedback>();if(!feedback)feedback=session.gameObject.AddComponent<IntegratedFeedback>();feedback.session=session;
            const string sounds="Assets/VRMPAssets/Audio/SFXClips/";
            feedback.hit=AssetDatabase.LoadAssetAtPath<AudioClip>(sounds+"Button_22_Click.wav");
            feedback.guard=AssetDatabase.LoadAssetAtPath<AudioClip>(sounds+"HoverSound.wav");
            feedback.damage=AssetDatabase.LoadAssetAtPath<AudioClip>(sounds+"NegativeSound.wav");
            feedback.boost=AssetDatabase.LoadAssetAtPath<AudioClip>(sounds+"CoinSplash.wav");
        }
        public static void Build()
        {
            if(System.IO.File.Exists(ScenePath))throw new Exception("Existing authored integrated scene is preserved. Open it instead.");
            var scene=EditorSceneManager.OpenScene("Assets/_Project/04_Scenes/sunwoo_HUDTest.unity");
            var context=UnityEngine.Object.FindFirstObjectByType<ArmMotionInput>();
            var feed=UnityEngine.Object.FindFirstObjectByType<CockpitLiveView>();
            foreach(var root in scene.GetRootGameObjects())Debug.Log("INTEGRATION_SOURCE_ROOT "+root.name+" "+root.transform.position);
            context.enabled=false;context.showDebugPanels=false;context.showHandDirections=false;
            var mounts=UnityEngine.Object.FindFirstObjectByType<CockpitViewPosition>();if(mounts)mounts.enabled=false;
            // The old robot is a presentation/test fixture. It never owns combat input here.
            var oldRobot=context.leftArm.robotFrame;
            foreach(var renderer in oldRobot.GetComponentsInChildren<Renderer>())renderer.enabled=false;
            var combat=EditorSceneManager.OpenScene(RobotLocalPunchScene.ScenePath,OpenSceneMode.Additive);
            var session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();
            var pilot=session.GetComponent<RobotPilotInput>();
            var oldCamera=pilot.cockpitCamera.gameObject;UnityEngine.Object.DestroyImmediate(oldCamera);
            pilot.cockpitCamera=context.cockpitCamera;pilot.seat=null;pilot.source=PrototypeInput.XRLocalBoth;
            foreach(Transform t in session.robotRoot.GetComponentsInChildren<Transform>())
                if(t.name.StartsWith("Cockpit ")||t.name=="Cockpit state / event display")t.gameObject.SetActive(false);
            session.statusText=null;
            foreach(var root in combat.GetRootGameObjects())SceneManager.MoveGameObjectToScene(root,scene);
            Layer(session.robotRoot,27);Layer(session.target.transform,27);
            var floor=GameObject.Find("Arena floor");if(floor)Layer(floor.transform,27);
            EditorSceneManager.CloseScene(combat,true);SceneManager.SetActiveScene(scene);
            var bridge=session.gameObject.AddComponent<IntegratedCombatHud>();bridge.session=session;bridge.pilot=pilot;bridge.cockpitContext=context;bridge.feed=feed;
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new Exception("Could not save integrated scene");
            AssetDatabase.SaveAssets();Debug.Log("INTEGRATED_SCENE_READY "+ScenePath);
        }
    }
}