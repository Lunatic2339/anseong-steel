using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRFloatingShieldSetup
{
    const string Folder="Assets/Weapons/CapstoneShield";
    static MechaVRFloatingShieldSetup(){EditorApplication.update+=Tick;}
    static void Tick(){if(!File.Exists("Library/FloatingShield.request")||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Library/FloatingShield.request");Configure();}
    static void Check(bool ok,string msg){if(!ok)throw new InvalidOperationException(msg);}
    static void Life(MechaVRShieldDeploy d,string method){typeof(MechaVRShieldDeploy).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(d,null);}
    public static void Configure()
    {
        try
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Stop Play mode first.");
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/MainVRScene_BodyTracking.unity");
            Check(scene.IsValid()&&scene.isLoaded,"Open body tracking scene first.");
            var body=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MechaVRBodyFollower>(true)).Single();
            var ik=body.GetComponentsInChildren<TwoBoneIKConstraint>(true).Single(c=>c.data.tip&&c.data.tip.name=="Hand.L");
            string backup="ModelBackups/FloatingShieldScene_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");Directory.CreateDirectory(backup);
            File.Copy(scene.path,backup+"/SceneOnDisk.unity");
            Check(EditorSceneManager.SaveScene(scene,backup+"/SceneWithCurrentEdits.unity",true),"Backup failed.");
            var prefab=Build();
            var old=ik.data.mid.Find("LeftArmShield");if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);root.name="LeftArmShield";
            Undo.RegisterCreatedObjectUndo(root,"Restore floating shield model");
            root.transform.SetParent(ik.data.mid,false);
            root.transform.rotation=body.transform.rotation;
            var scale=ik.data.mid.lossyScale;root.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            var d=root.GetComponent<MechaVRShieldDeploy>();d.forearmAnchor=ik.data.mid;d.handAnchor=ik.data.tip;d.facingReference=body.head;d.forwardOffset=.28f;
            Life(d,"Awake");
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(d);
            foreach(var t in root.GetComponentsInChildren<Transform>())PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            typeof(MechaVRShieldSetup).GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{d});
            ValidatePose(d);
            Render(body,d);
            Life(d,"Awake");
            EditorSceneManager.MarkSceneDirty(scene);Check(EditorSceneManager.SaveScene(scene),"Save failed.");AssetDatabase.SaveAssets();
            MechaVRSwordSetup.ValidateInstalled();Check(File.ReadAllText("Library/MechaSwordSetup.txt").StartsWith("PASS"),"Sword regression failed.");
            var shader=Shader.Find("MechaVR/Shield Deployment");Check(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Shader error.");
            File.WriteAllText("Library/FloatingShield.txt","PASS\nBackup="+backup+"\nOriginal FBX restored; no armor strips; no arm-to-shield energy connection; X open/close; intermediate and reverse animation; input lifecycle; 6 arm poses upright; sword regression; shader compile PASS\n");
        }
        catch(Exception e){File.WriteAllText("Library/FloatingShield.txt","FAIL\n"+e);Debug.LogException(e);}
    }
    static GameObject Build()
    {
        var preview=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=new GameObject("CapstoneShield");SceneManager.MoveGameObjectToScene(root,preview);
            var pivot=new GameObject("FloatingShieldModel").transform;pivot.SetParent(root.transform,false);
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/CapstoneShield.fbx");Check(model,"Original FBX missing.");
            var core=UnityEngine.Object.Instantiate(model,pivot,false);core.name="Emitter";
            var film=UnityEngine.Object.Instantiate(model,pivot,false);film.name="EnergyField";
            var emitter=core.GetComponentInChildren<Renderer>();var energy=film.GetComponentInChildren<Renderer>();
            emitter.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Shield_Emitter.mat");
            energy.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Shield_Energy.mat");
            emitter.enabled=false;energy.enabled=false;
            emitter.shadowCastingMode=energy.shadowCastingMode=ShadowCastingMode.Off;emitter.receiveShadows=energy.receiveShadows=false;
            var d=root.AddComponent<MechaVRShieldDeploy>();d.energyRenderer=energy;d.emitterRenderer=emitter;d.shieldVisualRoot=pivot;d.forwardOffset=.28f;
            Check(energy.GetComponent<MeshFilter>().sharedMesh.uv2.Length>0,"Original deployment UV missing.");
            return PrefabUtility.SaveAsPrefabAsset(root,Folder+"/CapstoneShield.prefab");
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
    }
    static void ValidatePose(MechaVRShieldDeploy installed)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=UnityEngine.Object.Instantiate(installed.gameObject);SceneManager.MoveGameObjectToScene(root,scene);
            var d=root.GetComponent<MechaVRShieldDeploy>();
            var arm=new GameObject("TestArm").transform;SceneManager.MoveGameObjectToScene(arm.gameObject,scene);
            var hand=new GameObject("TestHand").transform;hand.SetParent(arm,false);
            var head=new GameObject("TestHeading").transform;SceneManager.MoveGameObjectToScene(head.gameObject,scene);
            d.forearmAnchor=arm;d.handAnchor=hand;d.facingReference=head;
            for(int i=0;i<6;i++)
            {
                arm.SetPositionAndRotation(new Vector3(i*.2f,1,0),Quaternion.Euler(i*35,i*40,i*65));hand.localPosition=new Vector3(0,.27f,0);head.rotation=Quaternion.Euler(i*12,i*30,0);
                d.RefreshPose();var forward=Vector3.ProjectOnPlane(head.forward,Vector3.up).normalized;var center=Vector3.Lerp(arm.position,hand.position,.55f);
                Check(Vector3.Distance(d.shieldVisualRoot.position,center+forward*.28f)<.0001f,"Shield placement failed.");
                Check(Vector3.Dot(d.shieldVisualRoot.up,Vector3.up)>.999f,"Shield tilted with arm.");
            }
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Render(MechaVRBodyFollower body,MechaVRShieldDeploy installed)
    {
        var scene=EditorSceneManager.NewPreviewScene();var rt=new RenderTexture(1000,850,24);var previous=RenderTexture.active;
        try
        {
            var clone=UnityEngine.Object.Instantiate(body.gameObject);SceneManager.MoveGameObjectToScene(clone,scene);
            foreach(var b in clone.GetComponentsInChildren<Behaviour>())b.enabled=false;
            var d=clone.GetComponentInChildren<MechaVRShieldDeploy>();Life(d,"Awake");d.Toggle();d.Advance(1);d.RefreshPose();
            var cameraObject=new GameObject("ShieldPreview");SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.028f,.039f,.06f);camera.orthographic=true;camera.orthographicSize=.67f;camera.targetTexture=rt;
            var lightObject=new GameObject("PreviewLight");SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,-25,0);
            Directory.CreateDirectory("MechaVR_Validation");
            for(int i=0;i<2;i++)
            {
                var t=d.shieldVisualRoot;camera.transform.position=t.position+t.forward*(i==0?1.6f:.7f)+t.right*(i==0?.4f:1.4f)+Vector3.up*.15f;
                camera.transform.LookAt(t.position-t.forward*.12f,Vector3.up);camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(1000,850,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,850),0,0);image.Apply();File.WriteAllBytes("MechaVR_Validation/FloatingShield_"+i+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
        }
        finally{RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
