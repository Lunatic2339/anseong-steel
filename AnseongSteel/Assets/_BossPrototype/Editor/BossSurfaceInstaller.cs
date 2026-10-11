using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
    public static class BossSurfaceInstaller
    {
        const string Root = "Assets/_BossPrototype/Models/Surface";
        [Serializable] public class SurfaceData { public SurfaceMaterial[] materials; public SurfaceObject[] objects; }
        [Serializable] public class SurfaceMaterial { public string name, texture; public float[] color, emission; public float metallic, roughness, strength; }
        [Serializable] public class SurfaceObject { public string name; public string[] materials; }
        static SurfaceData Read() => JsonUtility.FromJson<SurfaceData>(File.ReadAllText(Root+"/BossSurface.json"));
        static Color ColorOf(float[] a) => new Color(a[0],a[1],a[2],a[3]);
        static string MaterialPath(string name) => Root+"/Materials/"+name+".mat";
        [MenuItem("Tools/Boss Prototype/Apply Blender Textures")]
        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if(!AssetDatabase.IsValidFolder(Root+"/Materials")) AssetDatabase.CreateFolder(Root,"Materials");
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null) throw new InvalidOperationException("URP Lit shader missing");
            var data=Read();
            foreach(var s in data.materials)
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(s.name));
                if(m==null) {m=new Material(shader);AssetDatabase.CreateAsset(m,MaterialPath(s.name));}
                m.shader=shader;m.name=s.name;
                Texture2D texture=null;
                if(!string.IsNullOrEmpty(s.texture))
                {
                    string path=Root+"/Textures/"+s.texture;
                    var ti=(TextureImporter)AssetImporter.GetAtPath(path);
                    if(ti==null) throw new InvalidOperationException("Missing texture: "+path);
                    ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.None;ti.mipmapEnabled=true;ti.maxTextureSize=2048;ti.SaveAndReimport();
                    texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                // A linked Blender Base Color replaces the socket value; do not tint the image.
                m.SetTexture("_BaseMap",texture);
                m.SetColor("_BaseColor",texture!=null?Color.white:ColorOf(s.color).gamma);
                m.SetFloat("_Metallic",s.metallic);m.SetFloat("_Smoothness",1-s.roughness);
                m.SetColor("_EmissionColor",ColorOf(s.emission)*s.strength);
                if(s.strength>0)m.EnableKeyword("_EMISSION");else m.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(m);
            }
            foreach(string path in new[]{"Assets/_BossPrototype/Models/BossUpdatedCompatible.fbx","Assets/_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026dsadsfadf.fbx"})
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                foreach(var s in data.materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),s.name),AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(s.name)));
                importer.SaveAndReimport();
            }
            const string prefab="Assets/_BossPrototype/Generated/BossMecha.prefab";
            var contents=PrefabUtility.LoadPrefabContents(prefab);
            try { ApplyTo(contents.GetComponent<BossAnimationDriver>().animator.gameObject);PrefabUtility.SaveAsPrefabAsset(contents,prefab); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
            var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            ApplyTo(demo.driver.animator.gameObject);
            EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);EditorSceneManager.SaveScene(demo.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"BOSS SURFACE PASS: {data.materials.Length} materials; {data.materials.Count(m=>!string.IsNullOrEmpty(m.texture))} textured materials; prefab and scene assigned.");
            BossModelSmokeTest.CapturePreview();
        }
        public static void ApplyTo(GameObject model)
        {
            var data=Read();var objects=data.objects.ToDictionary(o=>o.name);
            int count=0;
            foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!objects.TryGetValue(r.name,out var entry)) throw new InvalidOperationException("Unmapped boss mesh: "+r.name);
                var mats=entry.materials.Select(n=>AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(n))).ToArray();
                if(mats.Any(m=>m==null)||mats.Length!=r.sharedMesh.subMeshCount) throw new InvalidOperationException("Invalid material slots: "+r.name);
                if(r.sharedMesh.uv.Length!=r.sharedMesh.vertexCount) throw new InvalidOperationException("Missing UV: "+r.name);
                r.sharedMaterials=mats;EditorUtility.SetDirty(r);count++;
            }
            if(count!=data.objects.Length) throw new InvalidOperationException("Incomplete boss material assignment: "+count);
        }
    }
}
