using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
    public static class BossCombatInstaller
    {
        const string Root="Assets/_BossPrototype";
        [MenuItem("Tools/Boss Prototype/Install Combat Patterns")]
        public static void Install()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if(!AssetDatabase.IsValidFolder(Root+"/Combat"))AssetDatabase.CreateFolder(Root,"Combat");
            var profiles=Enum.GetValues(typeof(BossAttackKind)).Cast<BossAttackKind>().Select(Definition).ToArray();
            var warning=Material("AttackPreview",new Color(1,.55f,.05f));
            var projectile=Material("MissilePreview",new Color(1,.15f,.025f));
            string path=Root+"/Generated/BossMecha.prefab";
            var prefab=PrefabUtility.LoadPrefabContents(path);
            try {Configure(prefab.GetComponent<BossAnimationDriver>(),profiles,warning,projectile);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            demo.autoDemo=false;
            var combat=Configure(demo.driver,profiles,warning,projectile);
            var receiver=demo.target.GetComponent<BossCombatTarget>()??demo.target.gameObject.AddComponent<BossCombatTarget>();combat.target=receiver;
            var panel=demo.GetComponent<BossCombatTester>()??demo.gameObject.AddComponent<BossCombatTester>();panel.demo=demo;panel.combat=combat;
            // Respect the user's removed cockpit geometry. Only maintain a first-person camera.
            var view=demo.GetComponent<BossCockpitView>();if(view){view.windowFrame=null;view.SetCockpitView(true);}
            BindClips();
            EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);EditorSceneManager.SaveScene(demo.gameObject.scene);AssetDatabase.SaveAssets();
            Debug.Log("COMBAT INSTALL PASS: "+profiles.Length+" patterns; no cockpit objects created.");
        }
        static BossCombatController Configure(BossAnimationDriver driver,BossAttackDefinition[] profiles,Material warning,Material projectile)
        {
            driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle").SampleAnimation(driver.animator.gameObject,0);
            var combat=driver.GetComponent<BossCombatController>()??driver.gameObject.AddComponent<BossCombatController>();
            foreach(var type in typeof(BossAttackPattern).Assembly.GetTypes().Where(t=>!t.IsAbstract&&typeof(BossAttackPattern).IsAssignableFrom(t)))
                if(!driver.GetComponent(type))driver.gameObject.AddComponent(type);
            combat.driver=driver;combat.attacks=profiles;combat.telegraphMaterial=warning;combat.missileMaterial=projectile;
            var attachment=driver.GetComponent<BossSwordAttachment>()??driver.gameObject.AddComponent<BossSwordAttachment>();
            attachment.backSocket=driver.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="BackSwordSocket");
            attachment.sword=attachment.backSocket.Find("BackGreatsword");
            attachment.rightHand=driver.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:RightHand");
            var finger=driver.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:RightHandMiddle1");
            attachment.handPosition=attachment.rightHand.InverseTransformPoint(Vector3.Lerp(attachment.rightHand.position,finger.position,.35f));
            attachment.handEuler=(Quaternion.Inverse(attachment.rightHand.rotation)*Quaternion.LookRotation(driver.transform.up,-driver.transform.forward)).eulerAngles;
            combat.sword=attachment;BossSwordPolishInstaller.ConfigureGrip(driver);EditorUtility.SetDirty(combat);EditorUtility.SetDirty(attachment);return combat;
        }
        static Material Material(string name,Color color)
        {
            var path=Root+"/Combat/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(mat,path);}return mat;
        }
        static BossAttackDefinition Definition(BossAttackKind kind)
        {
            var path=Root+"/Combat/"+kind+".asset";var d=AssetDatabase.LoadAssetAtPath<BossAttackDefinition>(path);if(d)return d;
            d=ScriptableObject.CreateInstance<BossAttackDefinition>();d.kind=kind;
            switch(kind)
            {
                case BossAttackKind.HeavyPunch:d.damage=35;d.range=1.8f;break;
                case BossAttackKind.SwordSlash:d.damage=22;d.requiredGuard=BossArm.Left;break;
                case BossAttackKind.OverheadSmash:d.damage=50;d.windup=1.3f;d.active=.7f;d.coreExposureSeconds=0;break;
                case BossAttackKind.AlternatingCombo:d.damage=12;d.active=1.2f;d.range=1.8f;break;
                case BossAttackKind.MissileBarrage:d.damage=8;d.range=15;d.active=1.5f;d.requiredGuard=BossArm.Both;break;
                case BossAttackKind.Grab:d.damage=0;d.range=1.8f;d.requiredGuard=BossArm.Left;break;
                case BossAttackKind.Feint:d.windup=.65f;d.active=.8f;d.requiredGuard=BossArm.Right;break;
                case BossAttackKind.Shockwave:d.range=4;d.active=1.2f;d.damage=25;break;
                case BossAttackKind.LaserSweep:d.range=10;d.active=1.8f;d.damage=20;break;
                case BossAttackKind.EMP:d.range=8;d.damage=0;d.requiredGuard=BossArm.None;d.status=BossStatus.HudDisabled;break;
                case BossAttackKind.SensorJam:d.range=8;d.damage=0;d.requiredGuard=BossArm.None;d.status=BossStatus.LeftSensorHidden;break;
                case BossAttackKind.VisionDisruption:d.range=8;d.damage=0;d.requiredGuard=BossArm.None;d.status=BossStatus.RightVisionDistorted;break;
                case BossAttackKind.ArmLock:d.damage=5;d.range=1.8f;d.requiredGuard=BossArm.Left;d.status=BossStatus.LeftArmDisabled;break;
                case BossAttackKind.WeaponBreak:d.damage=10;d.range=1.8f;d.status=BossStatus.WeaponsDisabled;break;
            }
            AssetDatabase.CreateAsset(d,path);return d;
        }
        [MenuItem("Tools/Boss Prototype/Bind Combat Animation Clips")]
        public static void BindClips()
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/Generated/BossAnimator.controller");
            foreach(var guid in AssetDatabase.FindAssets("t:BossAttackDefinition",new[]{Root+"/Combat"}))
            {
                var d=AssetDatabase.LoadAssetAtPath<BossAttackDefinition>(AssetDatabase.GUIDToAssetPath(guid));if(!d.clip)continue;
                var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(d.clip)) as ModelImporter;
                if(importer&&importer.animationType!=ModelImporterAnimationType.Generic)throw new InvalidOperationException(d.kind+": use a Generic animation exported on the same v026 skeleton.");
                var state=controller.layers[0].stateMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==d.StateName)??controller.layers[0].stateMachine.AddState(d.StateName);
                state.motion=d.clip;state.writeDefaultValues=false;EditorUtility.SetDirty(d);
            }
            EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        }
    }
}



