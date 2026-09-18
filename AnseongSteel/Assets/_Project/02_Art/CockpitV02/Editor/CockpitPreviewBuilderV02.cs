// Editor-only, self-contained cockpit import and preview builder. No runtime gameplay code.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnseongSteel.Cockpit.Editor
{
    public static class CockpitPreviewBuilderV02
    {
        public const string Root = "Assets/_Project/02_Art/CockpitV02";
        [Serializable] class MaterialSpec { public string name; public float[] color; public float metallic, roughness, emission; }
        [Serializable] class Manifest { public MaterialSpec[] materials; public int modules, triangles; public float stationSpacing, padRadius, padTop, eyeHeight; }
        static string Output => Environment.GetEnvironmentVariable("COCKPIT_REVIEW_OUTPUT") ?? Path.GetFullPath(Root + "/Review");

        [MenuItem("Anseong Steel/Cockpit/Build or Refresh V02 Preview")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var folder in new[] {"Materials", "Prefabs", "Scenes"}) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            var data = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/cockpit_manifest.json"));
            var texturePath = Root + "/Textures/TemporaryCameraFeed.png";
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            textureImporter.sRGBTexture = true; textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.maxTextureSize = 2048; textureImporter.mipmapEnabled = true; textureImporter.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var modelPath = Root + "/Models/Anseong_Cockpit_v02.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.globalScale = 1; importer.useFileScale = true; importer.importCameras = false; importer.importLights = false;
            importer.importAnimation = false; importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var spec in data.materials)
            {
                var path = Root + "/Materials/" + spec.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = Shader.Find(spec.name == "Display_Feed" ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                if (shader == null) throw new Exception("Required URP shader unavailable.");
                if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat,path); } else mat.shader = shader;
                var color = new Color(spec.color[0],spec.color[1],spec.color[2],1);
                mat.SetColor("_BaseColor",spec.name == "Display_Feed" ? Color.white : color);
                if (spec.name == "Display_Feed") { mat.SetTexture("_BaseMap",texture); mat.SetFloat("_Cull",0); }
                else
                {
                    mat.SetFloat("_Metallic",spec.metallic); mat.SetFloat("_Smoothness",1-spec.roughness);
                    mat.SetColor("_EmissionColor", color * spec.emission);
                    if (spec.emission > 0) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),spec.name),mat);
            }
            importer.SaveAndReimport();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root = new GameObject("Cockpit_V02");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            model.transform.SetParent(root.transform,false); model.name="Cockpit_Modules";
            if(model.GetComponentsInChildren<Transform>().Any(t=>t.name.Contains("06_Arm") || t.name.Contains("07_Arm"))) throw new Exception("Removed rig unexpectedly present.");
            var anchors = new GameObject("Pilot_Anchors"); anchors.transform.SetParent(root.transform,false);
            // Validate orientation against station geometry, rather than silently assuming an FBX convention.
            var leftModule=model.GetComponentsInChildren<Transform>().First(t=>t.name=="02_Station_L");
            var rightModule=model.GetComponentsInChildren<Transform>().First(t=>t.name=="03_Station_R");
            var lb=leftModule.GetComponent<Renderer>().bounds; var rb=rightModule.GetComponent<Renderer>().bounds;
            if (lb.center.x > rb.center.x) { model.transform.localRotation=Quaternion.Euler(0,180,0); }
            lb=leftModule.GetComponent<Renderer>().bounds; rb=rightModule.GetComponent<Renderer>().bounds;
            if (Mathf.Abs(lb.center.x + data.stationSpacing/2) > .08f || Mathf.Abs(rb.center.x-data.stationSpacing/2) > .08f)
                throw new Exception("FBX station positions or metre scale do not match manifest.");
            var display=model.GetComponentsInChildren<Transform>().First(t=>t.name=="10_Display").GetComponent<Renderer>();
            if (display.bounds.center.z < 0) throw new Exception("Display faces the wrong end of the cockpit; check FBX axes.");
            foreach (var side in new[] {-1,1})
            {
                var pilot = new GameObject(side<0 ? "Pilot_Left_Floor" : "Pilot_Right_Floor"); pilot.transform.SetParent(anchors.transform,false);
                pilot.transform.localPosition = new Vector3(side*data.stationSpacing/2,data.padTop,0);
                var eye=new GameObject("Eye_Reference_165cm"); eye.transform.SetParent(pilot.transform,false); eye.transform.localPosition=new Vector3(0,data.eyeHeight,0);
            }
            // Simple deck collider only; props do not constrain real-world VR controller motion.
            var collider=new GameObject("Deck_Collision"); collider.transform.SetParent(root.transform,false);
            var box=collider.AddComponent<BoxCollider>(); box.center=new Vector3(0,-.10f,.10f); box.size=new Vector3(6.15f,.20f,6.1f);
            foreach (var side in new[] {-1,1})
            {
                var pad=new GameObject(side<0 ? "Pad_Collision_L" : "Pad_Collision_R"); pad.transform.SetParent(root.transform,false);
                pad.transform.localPosition=new Vector3(side*data.stationSpacing/2,data.padTop/2,0);
                // Low-poly convex mesh matches the round pad, avoids square invisible corners.
                var mesh=new Mesh {name="RoundPadCollider"}; var vertices=new Vector3[32*2+2];
                var triangles=new int[32*12]; vertices[64]=new Vector3(0,-data.padTop/2,0); vertices[65]=new Vector3(0,data.padTop/2,0);
                for(int i=0;i<32;i++) { float a=i*Mathf.PI*2/32; vertices[i]=new Vector3(Mathf.Cos(a)*data.padRadius,-data.padTop/2,Mathf.Sin(a)*data.padRadius); vertices[i+32]=vertices[i]+Vector3.up*data.padTop;
                    int n=(i+1)%32; int k=i*12; int[] f={i,n,i+32,n,n+32,i+32,64,n,i,65,i+32,n+32}; Array.Copy(f,0,triangles,k,12); }
                mesh.vertices=vertices; mesh.triangles=triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string mp=Root+"/Models/PadCollider_"+side+".asset"; var existing=AssetDatabase.LoadAssetAtPath<Mesh>(mp);
                if(existing) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; } else AssetDatabase.CreateAsset(mesh,mp);
                var mc=pad.AddComponent<MeshCollider>(); mc.sharedMesh=mesh; mc.convex=true;
            }
            PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/Cockpit_V02.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Cockpit_V02.prefab"));
            RenderSettings.skybox=null; RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.25f,.32f,.38f); RenderSettings.ambientIntensity=1;
            var lighting=new GameObject("Preview_Lighting_Only");
            AddLight("Key",new Vector3(0,3,2),new Vector3(0,1,0),new Color(.65f,.81f,1),2.2f,LightType.Directional,lighting.transform);
            AddLight("Warm rear fill",new Vector3(0,2,-2),new Vector3(0,1,0),new Color(1,.67f,.36f),5,LightType.Point,lighting.transform);
            AddLight("Display fill",new Vector3(0,2,2.5f),Vector3.zero,new Color(.3f,.7f,1),6,LightType.Point,lighting.transform);
            var cameras=new GameObject("Preview_Cameras_Not_XR");
            var overview=AddCamera("Overview",new Vector3(0,2.64f,-5.7f),new Vector3(0,1.64f,.8f),55,cameras.transform);
            float eyeY=data.padTop+data.eyeHeight;
            var left=AddCamera("LeftEye",new Vector3(-data.stationSpacing/2,eyeY,0),new Vector3(-.46f,eyeY-.12f,3),70,cameras.transform);
            var right=AddCamera("RightEye",new Vector3(data.stationSpacing/2,eyeY,0),new Vector3(.46f,eyeY-.12f,3),70,cameras.transform);
            overview.enabled=true; overview.tag="MainCamera"; overview.gameObject.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/Cockpit_Preview_V02.unity"); AssetDatabase.SaveAssets();
            var renderers=instance.GetComponentsInChildren<MeshRenderer>();
            int tris=instance.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);
            if(renderers.Length != data.modules) throw new Exception("Unexpected module count.");
            if(renderers.Any(r=>r.sharedMaterials.Any(m=>m==null || m.shader.name.Contains("Error")))) throw new Exception("Invalid material.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output,"Unity_Validation.txt"),"Unity "+Application.unityVersion+"\nScene: "+Root+"/Scenes/Cockpit_Preview_V02.unity\nPrefab: "+Root+"/Prefabs/Cockpit_V02.prefab\nMesh renderers: "+renderers.Length+"\nTriangles: "+tris+"\nLeft pad center: "+lb.center+"\nRight pad center: "+rb.center+"\nMetre scale, side labels and forward direction: PASS\nMaterial remaps: PASS\nNo XR rig or gameplay changes. Headset/performance testing pending.\n");
            Debug.Log("COCKPIT_BUILD_OK renderers="+renderers.Length+" triangles="+tris);
            AssetDatabase.ExportPackage(Root,Path.Combine(Output,"CockpitV02.unitypackage"),ExportPackageOptions.Recurse);
            SessionState.SetBool("CockpitV02.CapturePending",true);
            ScheduleCapture();
        }
        [InitializeOnLoadMethod]
        static void ResumeCaptureAfterDomainReload()
        { if(SessionState.GetBool("CockpitV02.CapturePending",false)) ScheduleCapture(); }
        static void ScheduleCapture()
        {
            // Importing assets can reload the domain; SessionState resumes this step afterwards.
            int ticks=0;
            EditorApplication.CallbackFunction capture=null;
            capture=()=> { if(EditorApplication.isCompiling || EditorApplication.isUpdating || ++ticks<30) return; EditorApplication.update-=capture;
                try {
                    SessionState.SetBool("CockpitV02.CapturePending",false);
                    foreach(var name in new[]{"Overview","LeftEye","RightEye"}) Capture(GameObject.Find(name).GetComponent<Camera>());
                    Debug.Log("COCKPIT_RENDER_OK"); if(Application.isBatchMode) EditorApplication.Exit(0);
                }
                catch(Exception ex) { Debug.LogException(ex); if(Application.isBatchMode) EditorApplication.Exit(2); }
            }; EditorApplication.update+=capture;
        }
        static Camera AddCamera(string name, Vector3 p, Vector3 target,float fov,Transform parent)
        { var go=new GameObject(name); go.transform.SetParent(parent); go.transform.position=p; go.transform.LookAt(target); var c=go.AddComponent<Camera>(); c.fieldOfView=fov; c.nearClipPlane=.03f; c.farClipPlane=40; c.clearFlags=CameraClearFlags.SolidColor; c.backgroundColor=new Color(.025f,.04f,.06f); c.enabled=false; c.GetUniversalAdditionalCameraData().renderPostProcessing=false; return c; }
        static void AddLight(string name,Vector3 p,Vector3 target,Color color,float intensity,LightType type,Transform parent)
        { var go=new GameObject(name); go.transform.SetParent(parent); go.transform.position=p; go.transform.LookAt(target); var l=go.AddComponent<Light>(); l.type=type; l.color=color; l.intensity=intensity; l.range=9; l.shadows=type==LightType.Directional?LightShadows.Soft:LightShadows.None; }
        static void Capture(Camera cam)
        {
            var rt=new RenderTexture(1440,1000,24,RenderTextureFormat.ARGB32); rt.Create();
            var previous=RenderTexture.active;
            try {
                var request=new UniversalRenderPipeline.SingleCameraRequest {destination=rt};
                RenderPipeline.SubmitRenderRequest(cam,request); RenderTexture.active=rt;
                var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); tex.Apply();
                File.WriteAllBytes(Path.Combine(Output,"Unity_"+cam.name+".png"),tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            } finally {RenderTexture.active=previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);}
        }
    }
}
