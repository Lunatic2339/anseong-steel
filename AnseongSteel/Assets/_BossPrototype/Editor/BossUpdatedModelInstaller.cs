using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
    public static class BossUpdatedModelInstaller
    {
        const string Source = "Assets/_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026dsadsfadf.fbx";
        const string Compatible = "Assets/_BossPrototype/Models/BossUpdatedCompatible.fbx";
        public static void InspectSkin()
        {
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var d=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>().driver;
            var idle=d.animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle");
            idle.SampleAnimation(d.animator.gameObject,0);
            float actualMin=float.PositiveInfinity, actualMax=float.NegativeInfinity;
            foreach(var r in d.animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var vertices=r.sharedMesh.vertices;
                var weights=r.sharedMesh.boneWeights;
                var binds=r.sharedMesh.bindposes;
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];
                    Vector3 point=(r.bones[w.boneIndex0].localToWorldMatrix*binds[w.boneIndex0]).MultiplyPoint3x4(vertices[i]);
                    actualMin=Mathf.Min(actualMin,point.y);actualMax=Mathf.Max(actualMax,point.y);
                }
                var m=new Mesh(); r.BakeMesh(m);
                float min=m.vertices.Min(v=>r.transform.TransformPoint(v).y);
                if(r.bones.Any(b=>b!=null&&(b.name=="mixamorig:RightHand"||b.name=="mixamorig:RightFoot")))
                    Debug.Log($"SKIN {r.name} parent={r.transform.parent.name} meshMinY={min:F3} center={r.transform.TransformPoint(m.bounds.center):F3} bone={r.bones[0].name}/{r.bones[0].position:F3}");
                UnityEngine.Object.DestroyImmediate(m);
            }
            Debug.Log($"ACTUAL SKIN FLOOR={actualMin:F4} TOP={actualMax:F4}");
        }
        [MenuItem("Tools/Boss Prototype/Apply Updated Boss Model")]
        public static void Apply()
        {
            AssetDatabase.ImportAsset(Compatible, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Compatible);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            string prefab = "Assets/_BossPrototype/Generated/BossMecha.prefab";
            var contents = PrefabUtility.LoadPrefabContents(prefab);
            try { Replace(contents.GetComponent<BossAnimationDriver>()); PrefabUtility.SaveAsPrefabAsset(contents,prefab); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            Replace(demo.driver);
            EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);
            EditorSceneManager.SaveScene(demo.gameObject.scene);
            AssetDatabase.SaveAssets();
            BossModelSmokeTest.Run();
        }

        static void Replace(BossAnimationDriver driver)
        {
            var old=driver.animator;
            var controller=old.runtimeAnimatorController;
            var idle=controller.animationClips.Single(c=>c.name=="Idle");
            idle.SampleAnimation(old.gameObject,0);
            var fresh=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Compatible),old.gameObject.scene);
            fresh.name="Mecha";
            fresh.transform.SetParent(old.transform.parent,false);
            fresh.transform.localPosition=old.transform.localPosition;
            fresh.transform.localRotation=old.transform.localRotation;
            fresh.transform.localScale=old.transform.localScale;
            var animator=fresh.GetComponent<Animator>() ?? fresh.AddComponent<Animator>();
            animator.runtimeAnimatorController=controller;
            animator.applyRootMotion=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var canonical = old.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
            var inverseRest = new System.Collections.Generic.Dictionary<string,Matrix4x4>();
            foreach(var skin in old.GetComponentsInChildren<SkinnedMeshRenderer>())
                for(int i=0;i<skin.bones.Length;i++)
                    if(skin.bones[i]!=null) inverseRest[skin.bones[i].name]=skin.sharedMesh.bindposes[i]*skin.transform.worldToLocalMatrix;
            // Some constant finger channels are omitted by Mixamo. Preserve their
            // canonical local defaults as well as the skin's bind coordinate system.
            foreach(var bone in fresh.GetComponentsInChildren<Transform>())
                if(canonical.TryGetValue(bone.name,out var reference))
                {
                    bone.localPosition=reference.localPosition;
                    bone.localRotation=reference.localRotation;
                    bone.localScale=reference.localScale;
                }
            const string skinFolder="Assets/_BossPrototype/Models/CompatibleSkins";
            if(!AssetDatabase.IsValidFolder(skinFolder)) AssetDatabase.CreateFolder("Assets/_BossPrototype/Models","CompatibleSkins");
            int meshIndex=0;
            foreach(var r in fresh.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen=true;
                var mesh=UnityEngine.Object.Instantiate(r.sharedMesh);
                mesh.name=r.sharedMesh.name;
                var bind=mesh.bindposes;
                for(int i=0;i<r.bones.Length;i++)
                    if(inverseRest.TryGetValue(r.bones[i].name,out var inverse)) bind[i]=inverse*r.transform.localToWorldMatrix;
                mesh.bindposes=bind;
                string meshPath=skinFolder+"/Skin"+(meshIndex++).ToString("D3")+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(existing==null) AssetDatabase.CreateAsset(mesh,meshPath);
                else { EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh); }
                r.sharedMesh=mesh;
            }
            BossSurfaceInstaller.ApplyTo(fresh);
            idle.SampleAnimation(fresh,0);
            var oldBones=old.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
            var newBones=fresh.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
            float error=oldBones.Max(p=>Vector3.Distance(p.Value.position,newBones[p.Key].position));
            foreach(var p in oldBones.Where(p=>Vector3.Distance(p.Value.position,newBones[p.Key].position)>.005f))
                Debug.Log($"POSE {p.Key} oldLocal={p.Value.localPosition:F4} newLocal={newBones[p.Key].localPosition:F4} localAngle={Quaternion.Angle(p.Value.localRotation,newBones[p.Key].localRotation):F3} oldWorld={p.Value.position:F3} newWorld={newBones[p.Key].position:F3}");
            Debug.Log($"ROOT old={old.transform.localPosition:F4}/{old.transform.localRotation.eulerAngles} new={fresh.transform.localPosition:F4}/{fresh.transform.localRotation.eulerAngles}; arm old={old.transform.Find("Armature").localPosition}/{old.transform.Find("Armature").localRotation.eulerAngles} new={fresh.transform.Find("Armature").localPosition}/{fresh.transform.Find("Armature").localRotation.eulerAngles}");
            var bounds=new Bounds(fresh.transform.position,Vector3.zero);
            foreach(var r in fresh.GetComponentsInChildren<SkinnedMeshRenderer>()) bounds.Encapsulate(r.bounds);
            Debug.Log($"UPDATED MODEL sample bone position error={error:F6}, rendered size={bounds.size}, oldArmature={old.transform.Find("Armature").localScale}, newArmature={fresh.transform.Find("Armature").localScale}");
            if(error>.01f) { UnityEngine.Object.DestroyImmediate(fresh); throw new InvalidOperationException("Updated animation skeleton position mismatch: "+error); }
            driver.hitPoint.SetParent(newBones["mixamorig:RightHand"],false);
            driver.hitPoint.localRotation=Quaternion.identity;
            driver.hitPoint.position=Vector3.Lerp(newBones["mixamorig:RightHand"].position,newBones["mixamorig:RightHandMiddle1"].position,.75f);
            var storedSword=old.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="BackSwordSocket");
            if(storedSword!=null) storedSword.SetParent(newBones["mixamorig:Spine"],false);
            var attachment=driver.GetComponent<BossSwordAttachment>();
            if(attachment){attachment.rightHand=newBones["mixamorig:RightHand"];attachment.backSocket=storedSword;attachment.sword=storedSword?storedSword.Find("BackGreatsword"):null;EditorUtility.SetDirty(attachment);}
            driver.animator=animator;
            fresh.AddComponent<BossAnimationEvents>().driver=driver;
            float floor=float.PositiveInfinity;
            foreach(var skin in fresh.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var vertices=skin.sharedMesh.vertices;
                var weights=skin.sharedMesh.boneWeights;
                var binds=skin.sharedMesh.bindposes;
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];
                    if(w.weight0<.999f) throw new InvalidOperationException("Expected rigid mecha skin weights.");
                    var point=(skin.bones[w.boneIndex0].localToWorldMatrix*binds[w.boneIndex0]).MultiplyPoint3x4(vertices[i]);
                    floor=Mathf.Min(floor,point.y);
                }
            }
            float lift=driver.transform.position.y-floor;
            if(Mathf.Abs(lift)>.5f) throw new InvalidOperationException("Unexpected grounding offset: "+lift);
            fresh.transform.parent.position+=Vector3.up*lift;
            Debug.Log($"Updated model ground adjustment={lift:F4} metres");
            EditorUtility.SetDirty(driver);
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            BossSwordPolishInstaller.ConfigureGrip(driver);
        }
        public static void Audit()
        {
            AssetDatabase.ImportAsset(Source, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Source);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
            var old = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_BossPrototype/Animation/Idle.fbx");
            var fresh = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var a = old.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
            foreach(var t in fresh.GetComponentsInChildren<Transform>().Where(t=>a.ContainsKey(t.name)))
            {
                var o=a[t.name];
                float angle=Quaternion.Angle(o.localRotation,t.localRotation);
                if(angle>.1f || Vector3.Distance(o.localPosition,t.localPosition)>.0001f)
                    Debug.Log($"RIG DIFF {t.name}: rotation={angle:F3} pos={Vector3.Distance(o.localPosition,t.localPosition):F6}; path={AnimationUtility.CalculateTransformPath(t,fresh.transform)}");
            }
            Debug.Log($"MODEL newRoot={fresh.transform.localScale} meshes={fresh.GetComponentsInChildren<SkinnedMeshRenderer>().Length} oldRoot={old.transform.localScale}");
        }
    }
}




