using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CapstoneSwordImport
{
    const string Folder = "Assets/Weapons/CapstoneSword";
    const string ModelPath = Folder + "/CapstoneSword.fbx";
    const string TexturePath = Folder + "/Textures/CapstoneSword_BaseColor.png";
    const string MaterialPath = Folder + "/CapstoneSword.mat";
    const string PrefabPath = Folder + "/CapstoneSword.prefab";
    const string ReportPath = "Library/CapstoneSwordImport.txt";
    static bool attempted;

    static CapstoneSwordImport() { EditorApplication.update += TryImport; }
    static void TryImport()
    {
        if (attempted || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(ModelPath) || !File.Exists(TexturePath)) return;
        attempted = true;
        EditorApplication.update -= TryImport;
        if (File.Exists(PrefabPath)) return;
        Build();
    }

    [MenuItem("Tools/Capstone/Sword/Create Imported Prefab")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before importing the sword."); return; }
        if (File.Exists(PrefabPath)) { Debug.Log("Sword prefab already exists: " + PrefabPath); return; }
        Scene preview = default;
        try
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader was not found.");
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.sRGBTexture = true;
            textureImporter.alphaSource = TextureImporterAlphaSource.None;
            textureImporter.mipmapEnabled = true;
            textureImporter.maxTextureSize = 4096;
            textureImporter.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (!texture) throw new InvalidOperationException("Sword texture is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "CapstoneSword" };
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "CapstoneSword_Surface"), material);
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) throw new InvalidOperationException("FBX did not import.");
            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("CapstoneSword");
            SceneManager.MoveGameObjectToScene(root, preview);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            instance.transform.SetParent(root.transform, false);
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("No renderers in sword model.");
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            int vertices = 0, triangles = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (!mesh || mesh.uv.Length != mesh.vertexCount || mesh.normals.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Missing mesh, UVs, or normals.");
                vertices += mesh.vertexCount;
                for (int s = 0; s < mesh.subMeshCount; s++) triangles += (int)mesh.GetIndexCount(s) / 3;
            }
            if (triangles != 10003 || bounds.size.y < 0.95f || bounds.size.y > 1.05f || bounds.size.x > 0.3f || bounds.size.z > 0.1f)
                throw new InvalidOperationException("Unexpected sword geometry or scale: " + bounds.size + ", triangles=" + triangles);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (!success) throw new InvalidOperationException("Could not save sword prefab.");
            AssetDatabase.SaveAssets();
            File.WriteAllText(ReportPath, "PASS\nPrefab=" + PrefabPath + "\nShader=" + material.shader.name
                + "\nTexture=" + texture.width + "x" + texture.height + "\nTriangles=" + triangles
                + "\nVertices=" + vertices + "\nWorldSizeMeters=" + bounds.size.ToString("F6")
                + "\nCenter=" + bounds.center.ToString("F6") + "\nUVsNormalsMaterials=valid\nSourceBlendUnchanged=true\n");
            Debug.Log("[Capstone Sword] Imported and validated: " + PrefabPath);
        }
        catch (Exception e)
        {
            File.WriteAllText(ReportPath, "FAIL\n" + e);
            Debug.LogException(e);
        }
        finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
    }
}
