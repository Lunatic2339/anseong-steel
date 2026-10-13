using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossHandPoseInstaller
 {
  const string Root="Assets/_BossPrototype";
  public static void ApplyAndReview(){BossCombatFinishInstaller.Apply();BossHandRigWorkshop.Review();}
  [MenuItem("Tools/Boss Prototype/Calibrate Mechanical Hand Poses")]
  public static void Apply()
  {
   string path=Root+"/Generated/BossMecha.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
   try{Configure(prefab.GetComponent<BossAnimationDriver>());PrefabUtility.SaveAsPrefabAsset(prefab,path);}
   finally{PrefabUtility.UnloadPrefabContents(prefab);}
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var driver=Object.FindFirstObjectByType<BossModelDemo>().driver;Configure(driver);
   EditorSceneManager.MarkSceneDirty(driver.gameObject.scene);EditorSceneManager.SaveScene(driver.gameObject.scene);AssetDatabase.SaveAssets();
   BossHandRigWorkshop.Review();Debug.Log("MECHANICAL HAND POSES INSTALL PASS");
  }
  public static void Configure(BossAnimationDriver driver)
  {
   var attachment=driver.GetComponent<BossSwordAttachment>();
   ConfigureHandle(attachment);
   var bones=driver.animator.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")&&t.name.Contains("Hand")&&!t.name.EndsWith("Hand")&&!t.name.EndsWith("_end")).ToArray();
   var positions=bones.Select(t=>t.localPosition).ToArray();var rotations=bones.Select(t=>t.localRotation).ToArray();
   BossHandRigWorkshop.Pose(driver.animator);
   var fists=bones.Select(t=>t.localRotation).ToArray();
   BossHandRigWorkshop.Pose(driver.animator,1);
   var relaxed=bones.Select(t=>t.localRotation).ToArray();
   BossHandRigWorkshop.Pose(driver.animator,2);
   var pose=driver.GetComponent<BossHandPose>();if(!pose)pose=driver.gameObject.AddComponent<BossHandPose>();
   pose.animator=driver.animator;pose.sword=driver.GetComponent<BossSwordAttachment>();
   pose.joints=bones.Select((t,i)=>new BossHandPose.FingerJoint{bone=t,localPosition=t.localPosition,fistRotation=fists[i],relaxedRotation=relaxed[i],gripRotation=t.localRotation,left=t.name.Contains("LeftHand")}).ToArray();
   for(int i=0;i<bones.Length;i++){bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];}
   EditorUtility.SetDirty(pose);
   EditorUtility.SetDirty(pose.sword);
  }
  static void ConfigureHandle(BossSwordAttachment sword)
  {
   if(!sword||!sword.sword)return;
   var filter=sword.sword.GetComponentInChildren<MeshFilter>();if(!filter)return;
   if(!sword.originalSwordMesh)sword.originalSwordMesh=filter.sharedMesh;
   var mesh=Object.Instantiate(sword.originalSwordMesh);mesh.name="GreatswordLongGrip";
   var vertices=mesh.vertices;var normals=mesh.normals;
   for(int i=0;i<vertices.Length;i++)
   {
    float y=vertices[i].y;
    if(y>.875f)
    {
     vertices[i].y=y<=.953f?.875f+(y-.875f)*2.5f:y+(.953f-.875f)*1.5f;
     if(y<.953f){normals[i].y/=2.5f;normals[i].Normalize();}
    }
   }
   mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();
   const string path=Root+"/Generated/GreatswordLongGrip.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
   filter.sharedMesh=mesh;sword.rightGripY=.92f;sword.leftGripY=1.018f;EditorUtility.SetDirty(filter);EditorUtility.SetDirty(sword);
  }
 }
}
