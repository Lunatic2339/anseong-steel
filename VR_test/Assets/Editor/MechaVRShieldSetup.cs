using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class MechaVRShieldSetup
{
    const string Folder = "Assets/Weapons/CapstoneShield";
    const string ScenePath = "Assets/Scenes/MainVRScene_BodyTracking.unity";
    const string Report = "Library/MechaShieldSetup.txt";
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Mecha VR/Set Up Left Arm Shield")]
    public static void Configure()
    { MechaVRFloatingShieldSetup.Configure(); }
    static Material Material(string name,Shader shader,bool emitter)
    {
        string path=Folder+"/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) { m=new Material(shader); AssetDatabase.CreateAsset(m,path); }
        m.shader=shader; m.SetFloat("_Emitter",emitter?1:0); m.SetFloat("_Open",0);
        m.renderQueue=emitter?3001:3000; EditorUtility.SetDirty(m); return m;
    }
    static GameObject BuildPrefab(Material seam,Material energy)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=new GameObject("CapstoneShield"); SceneManager.MoveGameObjectToScene(root,scene);
            string meshPath=Folder+"/ArmEnergyPlane.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(!mesh) {mesh=new Mesh {name="ArmEnergyPlane"}; AssetDatabase.CreateAsset(mesh,meshPath);}
            mesh.Clear();
            mesh.vertices=new [] {
                new Vector3(-.1645f,.5f,0),new Vector3(.1645f,.5f,0),
                new Vector3(.2645f,.4f,0),new Vector3(.2645f,-.4f,0),
                new Vector3(.1645f,-.5f,0),new Vector3(-.1645f,-.5f,0),
                new Vector3(-.2645f,-.4f,0),new Vector3(-.2645f,.4f,0)};
            mesh.triangles=new [] {0,2,1,0,3,2,0,4,3,0,5,4,0,6,5,0,7,6};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var film=new GameObject("EnergyField"); film.transform.SetParent(root.transform,false);
            film.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=film.AddComponent<MeshRenderer>(); renderer.sharedMaterial=energy;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false; renderer.enabled=false;
            var deploy=root.AddComponent<MechaVRShieldDeploy>(); deploy.energyRenderer=renderer;
            deploy.armorSeamLights=new Renderer[2];
            for(int side=0;side<2;side++)
            {
                var strip=new GameObject("ArmorSeamLight_"+side); strip.transform.SetParent(root.transform,false);
                var line=strip.AddComponent<LineRenderer>(); line.useWorldSpace=false; line.positionCount=9;
                line.widthMultiplier=.003f; line.numCapVertices=4; line.sharedMaterial=seam;
                line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false; line.enabled=false;
                for(int k=0;k<9;k++) line.SetPosition(k,new Vector3(Mathf.Lerp(-.10f,.10f,k/8f),side==0?-.052f:.052f,-.025f));
                deploy.armorSeamLights[side]=line;
            }
            return PrefabUtility.SaveAsPrefabAsset(root,Folder+"/CapstoneShield.prefab");
        }
        finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void FitSeamLights(GameObject instance,GameObject body)
    {
        // Bake temporary query geometry only during authoring, to lay light along the real armor surface.
        var proxies=new System.Collections.Generic.List<GameObject>();
        var meshes=new System.Collections.Generic.List<Mesh>();
        try
        {
            foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                var proxy=new GameObject("ShieldSurfaceQuery"); proxies.Add(proxy);
                SceneManager.MoveGameObjectToScene(proxy,instance.scene);
                proxy.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);
                proxy.transform.localScale=skin.transform.lossyScale;
                proxy.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            Physics.SyncTransforms();
            var t=instance.transform;
            foreach(var line in instance.GetComponentsInChildren<LineRenderer>())
            {
                for(int k=0;k<line.positionCount;k++)
                {
                    Vector3 point=t.TransformPoint(line.GetPosition(k));
                    var ray=new Ray(point+t.forward*.25f,-t.forward);
                    float nearest=float.PositiveInfinity; Vector3 fitted=point;
                    foreach(var proxy in proxies)
                    {
                        if(proxy.GetComponent<MeshCollider>().Raycast(ray,out var hit,.5f) && hit.distance<nearest)
                        { nearest=hit.distance; fitted=hit.point+hit.normal*.0015f; }
                    }
                    line.SetPosition(k,line.transform.InverseTransformPoint(fitted));
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(line);
            }
        }
        finally {foreach(var proxy in proxies) UnityEngine.Object.DestroyImmediate(proxy); foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);}
    }
    static void Lifecycle(MechaVRShieldDeploy d,string method)=>typeof(MechaVRShieldDeploy).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(d,null);
    static void Press(InputDevice device,string control,float value)
    {
        using(StateEvent.From(device,out var ev)) {device.GetChildControl<ButtonControl>(control).WriteValueIntoEvent(value,ev); InputSystem.QueueEvent(ev);}
        InputSystem.Update();
    }
    static void Validate(MechaVRShieldDeploy installed)
    {
        const string layout="MechaShieldTestController";
        var manager=typeof(InputSystem).GetField("s_Manager",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var updates=manager.GetType().GetProperty("runPlayerUpdatesInEditMode"); bool old=(bool)updates.GetValue(manager);
        InputDevice left=null,right=null;
        var scene=EditorSceneManager.NewPreviewScene(); GameObject clone=null;
        try
        {
            updates.SetValue(manager,true);
            InputSystem.RegisterLayout("{\"name\":\""+layout+"\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"primaryButton\",\"layout\":\"Button\",\"format\":\"BIT\"},{\"name\":\"secondaryButton\",\"layout\":\"Button\",\"format\":\"BIT\"}]}");
            left=InputSystem.AddDevice(layout); right=InputSystem.AddDevice(layout);
            InputSystem.SetDeviceUsage(left,UnityEngine.InputSystem.CommonUsages.LeftHand); InputSystem.SetDeviceUsage(right,UnityEngine.InputSystem.CommonUsages.RightHand);
            clone=UnityEngine.Object.Instantiate(installed.gameObject); SceneManager.MoveGameObjectToScene(clone,scene);
            var d=clone.GetComponent<MechaVRShieldDeploy>(); Lifecycle(d,"Awake"); Lifecycle(d,"OnEnable");
            Check(!d.IsOpen&&!d.energyRenderer.enabled,"Must start closed.");
            Check(d.armorSeamLights.Length==0 && !clone.GetComponentsInChildren<LineRenderer>().Any(),"Armor strips remain.");
            Check(d.emitterRenderer && !d.emitterRenderer.enabled,"Closed model visible.");
            Check(clone.GetComponentsInChildren<Renderer>().Length==2 && d.shieldVisualRoot,"Expected only the shield core and energy surface.");
            Check(d.energyRenderer.GetComponent<MeshFilter>().sharedMesh.vertexCount>8,"Original modeled shield missing.");
            Check(!clone.transform.Find("ArmToShieldEnergy"),"Arm-to-shield energy connection must be absent.");
            Press(right,"primaryButton",1); Press(left,"secondaryButton",1); Check(!d.IsOpen,"Wrong button opened shield.");
            Press(left,"primaryButton",1); Check(d.IsOpen,"X did not open.");
            d.Advance(0.2f); Check(d.Deployment>0 && d.Deployment<1 && d.energyRenderer.enabled,"No intermediate animation.");
            InputSystem.Update(); Check(d.IsOpen,"Holding X toggled again.");
            float before=d.Deployment; Press(left,"primaryButton",0); Press(left,"primaryButton",1);
            Check(!d.IsOpen&&Mathf.Approximately(before,d.Deployment),"Reversal jumped.");
            d.Advance(0.1f); Check(d.Deployment<before,"Reversal did not close."); d.Advance(1);
            Check(d.Deployment==0&&!d.energyRenderer.enabled && !d.emitterRenderer.enabled,"Closed energy still visible.");
            Press(left,"primaryButton",0); Press(left,"primaryButton",1); d.Advance(1); Check(d.Deployment==1 && d.emitterRenderer.enabled,"Reopen or armor lights failed.");
            Lifecycle(d,"OnDisable"); Press(left,"primaryButton",0); Press(left,"primaryButton",1); Check(d.IsOpen,"Disabled input still responds.");
            Lifecycle(d,"OnEnable"); Press(left,"primaryButton",0); Press(left,"primaryButton",1); Check(!d.IsOpen,"Re-enable failed.");
            Lifecycle(d,"OnDisable");
        }
        finally
        {
            if(clone) UnityEngine.Object.DestroyImmediate(clone);
            if(left!=null) InputSystem.RemoveDevice(left); if(right!=null) InputSystem.RemoveDevice(right);
            InputSystem.RemoveLayout(layout); updates.SetValue(manager,old); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static void RenderMount(GameObject instance)
    {
        var go=new GameObject("ShieldMountPreviewCamera");
        SceneManager.MoveGameObjectToScene(go,instance.scene);
        var camera=go.AddComponent<Camera>(); camera.scene=instance.scene;
        var t=instance.transform; camera.transform.position=t.position+t.forward*1.5f+t.right*.4f+t.up*.25f;
        camera.transform.LookAt(t.position,-t.up); camera.orthographic=true; camera.orthographicSize=.67f;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.025f,.035f,.05f);
        camera.cullingMask=1<<31;
        var layers=instance.GetComponentInParent<MechaVRBodyFollower>().GetComponentsInChildren<Renderer>(true)
            .Select(r=>r.gameObject).Distinct().ToDictionary(g=>g,g=>g.layer);
        foreach(var g in layers.Keys) g.layer=31;
        var rt=new RenderTexture(800,800,24); var previous=RenderTexture.active;
        var d=instance.GetComponent<MechaVRShieldDeploy>();
        try
        {
            for(int stage=0;stage<3;stage++)
            {
                Lifecycle(d,"Awake"); if(stage>0){d.Toggle(); d.Advance(stage==1?.225f:1f);}
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                var image=new Texture2D(800,800,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,800,800),0,0); image.Apply();
                File.WriteAllBytes("MechaVR_Validation/ArmShield_Mounted_"+stage+".png",image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            }
        }
        finally {foreach(var entry in layers) entry.Key.layer=entry.Value; Lifecycle(d,"Awake"); RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);}
    }
    static void RenderPreviews(GameObject prefab)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var rt=new RenderTexture(640,800,24); Texture2D image=null;
        try
        {
            var shield=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var d=shield.GetComponent<MechaVRShieldDeploy>();
            var go=new GameObject("PreviewCamera"); SceneManager.MoveGameObjectToScene(go,scene);
            var camera=go.AddComponent<Camera>(); camera.scene=scene; camera.orthographic=true; camera.orthographicSize=.58f;
            camera.transform.position=new Vector3(0,0,2); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.045f,.06f,.08f); camera.targetTexture=rt;
            Directory.CreateDirectory("MechaVR_Validation");
            for(int n=0;n<3;n++)
            {
                Lifecycle(d,"Awake"); if(n>0) {d.Toggle(); d.Advance(n==1?.225f:1f);}
                camera.Render(); var previous=RenderTexture.active; RenderTexture.active=rt;
                image=new Texture2D(640,800,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,640,800),0,0); image.Apply();
                File.WriteAllBytes("MechaVR_Validation/Shield_"+n+".png",image.EncodeToPNG()); RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(image); image=null;
            }
        }
        finally { if(image) UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); EditorSceneManager.ClosePreviewScene(scene); }
    }
}