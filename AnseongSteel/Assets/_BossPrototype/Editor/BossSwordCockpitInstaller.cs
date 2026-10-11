using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
    public static class BossSwordCockpitInstaller
    {
        const string Root="Assets/_BossPrototype";
        const string Sword=Root+"/Models/Sword/BossGreatsword.fbx";
        [MenuItem("Tools/Boss Prototype/Apply Back Sword and First Person View")]
        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Models/Sword/Greatsword.mat");
            if(!mat) {mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,Root+"/Models/Sword/Greatsword.mat");}
            mat.SetColor("_BaseColor",Color.white);
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Models/Sword/Greatsword_BaseColor.jpg"));
            mat.SetFloat("_Metallic",.55f);mat.SetFloat("_Smoothness",.4f);EditorUtility.SetDirty(mat);
            var importer=(ModelImporter)AssetImporter.GetAtPath(Sword);
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;
            importer.SaveAndReimport();
            foreach(var source in AssetDatabase.LoadAssetAtPath<GameObject>(Sword).GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Distinct())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),source.name),mat);
            importer.SaveAndReimport();
            string prefab=Root+"/Generated/BossMecha.prefab";
            var contents=PrefabUtility.LoadPrefabContents(prefab);
            try { Attach(contents.GetComponent<BossAnimationDriver>(),mat);PrefabUtility.SaveAsPrefabAsset(contents,prefab); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            Attach(demo.driver,mat);
            var camera=Camera.main;
            var view=demo.GetComponent<BossCockpitView>()??demo.gameObject.AddComponent<BossCockpitView>();
            if(view.windowFrame) UnityEngine.Object.DestroyImmediate(view.windowFrame);
            view.viewCamera=camera;view.targetRenderer=demo.targetRenderer;
            view.overviewPosition=new Vector3(6.5f,4.2f,5.5f);
            view.overviewEuler=Quaternion.LookRotation(new Vector3(0,1.15f,-.6f)-view.overviewPosition).eulerAngles;
            var away=demo.target.position-demo.driver.transform.position;away.y=0;away.Normalize();
            view.cockpitPosition=new Vector3(demo.target.position.x,2.25f,demo.target.position.z)+away*.55f;
            view.cockpitEuler=Quaternion.LookRotation(-away+Vector3.down*.20f).eulerAngles;
            camera.nearClipPlane=.03f;
            view.windowFrame=null;
            demo.showControls=false;
            view.SetCockpitView(true);
            EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);EditorSceneManager.SaveScene(demo.gameObject.scene);AssetDatabase.SaveAssets();
            Validate();
            BossModelSmokeTest.CapturePreview();
            Debug.Log("SWORD FIRST PERSON PASS: spine attachment, textured sword, hidden target renderer with collider retained, first-person and overview views.");
        }
        static Material MakeMaterial(string name,Color color,float metallic)
        {
            string path=Root+"/Generated/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) {m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(m);return m;
        }
        static void Bar(Transform parent,string name,Vector3 p,Vector3 size,Material m,float roll)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;
            go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=size;go.transform.localRotation=Quaternion.Euler(0,0,roll);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial=m;
            go.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        static void Attach(BossAnimationDriver driver,Material material)
        {
            var animator=driver.animator;
            animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle").SampleAnimation(animator.gameObject,0);
            var bone=animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:Spine");
            var old=bone.Find("BackSwordSocket");if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var socket=new GameObject("BackSwordSocket").transform;
            socket.position=driver.transform.TransformPoint(new Vector3(.46f,2.55f,-.65f));
            socket.rotation=driver.transform.rotation*Quaternion.Euler(0,0,-25f);
            socket.SetParent(bone,true);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Sword),driver.gameObject.scene);
            model.name="BackGreatsword";model.transform.SetParent(socket,false);
            var renderers=model.GetComponentsInChildren<Renderer>();
            // Measure in socket coordinates so rig/world orientation does not alter scale.
            var points=model.GetComponentsInChildren<MeshFilter>().SelectMany(f=>Enumerable.Range(0,8).Select(i=>socket.InverseTransformPoint(f.transform.TransformPoint(f.sharedMesh.bounds.center+Vector3.Scale(f.sharedMesh.bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)))))).ToArray();
            var min=new Vector3(points.Min(v=>v.x),points.Min(v=>v.y),points.Min(v=>v.z));
            var max=new Vector3(points.Max(v=>v.x),points.Max(v=>v.y),points.Max(v=>v.z));
            if(max.y-min.y<.1f)throw new InvalidOperationException("Sword axis/size unexpected: "+(max-min));
            float scale=2.25f/(max.y-min.y);
            model.transform.localScale*=scale;
            model.transform.localPosition=-new Vector3((min.x+max.x)/2,Mathf.Lerp(min.y,max.y,.92f),(min.z+max.z)/2)*scale;
            foreach(var r in renderers)r.sharedMaterial=material;
            var bracket=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Models/Surface/Materials/Ring_Metal_Frame.mat");
            Bar(socket,"Upper sword mount",new Vector3(0,-.3f,.13f),new Vector3(.18f,.12f,.3f),bracket,0);
            Bar(socket,"Lower sword mount",new Vector3(0,-.8f,.13f),new Vector3(.18f,.12f,.3f),bracket,0);
            BossSwordPolishInstaller.ConfigureGrip(driver);
            Debug.Log($"SWORD bounds={max-min} length=2.25 grip={socket.position} parent={bone.name}");
        }
        public static void CaptureReviewAndTest()
        {
            BossModelSmokeTest.CapturePreview();
            System.IO.File.Copy("Library/BossModelPreview.png","Library/BossCockpitPreview.png",true);
            var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            var view=demo.GetComponent<BossCockpitView>();
            view.SetCockpitView(false);
            if(!demo.targetRenderer.enabled||(view.windowFrame && view.windowFrame.activeSelf))throw new InvalidOperationException("Overview switch failed");
            var cam=view.viewCamera;
            cam.transform.position=demo.driver.transform.TransformPoint(new Vector3(-2.3f,2.2f,-4f));
            cam.transform.LookAt(demo.driver.transform.TransformPoint(new Vector3(0,1.6f,-.2f)));cam.fieldOfView=42;
            var target=new RenderTexture(1280,800,24);target.Create();
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest {destination=target};
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam,request);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam,request);
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
            System.IO.File.WriteAllBytes("Library/BossSwordRearPreview.png",image.EncodeToPNG());
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
            view.SetCockpitView(true);Validate();
            // Reload the saved scene before the existing motion/contact regression test.
            BossModelSmokeTest.Run();
        }
        public static void Validate()
        {
            var d=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            var socket=d.driver.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="BackSwordSocket");
            if(socket.parent.name!="mixamorig:Spine"||!socket.GetComponentInChildren<Renderer>().sharedMaterial.GetTexture("_BaseMap"))throw new InvalidOperationException("Sword binding missing");
            var v=d.GetComponent<BossCockpitView>();
            if(!v||!v.cockpitView||d.targetRenderer.enabled||!d.target.GetComponent<Collider>().enabled)throw new InvalidOperationException("Cockpit target setup invalid");
            var pos=socket.localPosition;var rot=socket.localRotation;
            foreach(string name in new[]{"Walk","Punch","TurnLeft","TurnRight"})
            {
                var clip=d.driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name==name);
                foreach(float normalized in new[]{0f,.25f,.5f,.75f,1f})
                {
                    clip.SampleAnimation(d.driver.animator.gameObject,clip.length*normalized);
                    if(Vector3.Distance(socket.localPosition,pos)>.0001f||Quaternion.Angle(socket.localRotation,rot)>.01f)throw new InvalidOperationException("Sword detached during "+name);
                }
            }
            d.driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle").SampleAnimation(d.driver.animator.gameObject,0);
        }
    }
}




