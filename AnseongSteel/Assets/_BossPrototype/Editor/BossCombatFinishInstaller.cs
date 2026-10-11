using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossCombatFinishInstaller
 {
  const string Root="Assets/_BossPrototype";
  public static void ApplyAndReview(){Apply();Review();}
  public static void Review()
  {
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var c=UnityEngine.Object.FindFirstObjectByType<BossCombatController>();
   c.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);var s=c.sword;s.Initialize();s.Draw();
   var report=new System.Text.StringBuilder();
   foreach(var mesh in s.sword.GetComponentsInChildren<MeshFilter>())report.AppendLine(mesh.name+" bounds="+mesh.sharedMesh.bounds.size);
   foreach(var d in c.attacks.Where(d=>d.IsSword&&d.clip))
   {
    float maxError=0;int samples=0;
    for(float n=d.windupEnd+.015f;n<d.activeEnd-.015f;n+=.01f)
    {
     d.clip.SampleAnimation(c.driver.animator.gameObject,(n-.002f)*d.clip.length);s.ApplyGripPose(0,d.bladeRoll.Evaluate(n-.002f));var before=s.BladeTip;
     d.clip.SampleAnimation(c.driver.animator.gameObject,(n+.002f)*d.clip.length);s.ApplyGripPose(0,d.bladeRoll.Evaluate(n+.002f));var after=s.BladeTip;
     d.clip.SampleAnimation(c.driver.animator.gameObject,n*d.clip.length);s.ApplyGripPose(0,d.bladeRoll.Evaluate(n));
     var v=Vector3.ProjectOnPlane(after-before,s.sword.up);
     if(v.magnitude<.005f)continue;
     float error=Mathf.Abs(Vector3.Dot(v.normalized,s.sword.forward));maxError=Mathf.Max(maxError,error);samples++;
     if(Vector3.Distance(s.sword.TransformPoint(new Vector3(0,s.rightGripY,0)),s.rightHand.TransformPoint(s.handPosition))>.005f)throw new Exception("Palm anchor drift");
    }
    report.AppendLine(d.kind+" max broad-face velocity component="+maxError.ToString("F4")+" samples="+samples);
    if(maxError>.15f)throw new Exception("Edge alignment error: "+d.kind+" "+maxError);
   }
   File.WriteAllText("Library/BossCombatEdgeVerification.txt",report.ToString());Debug.Log(report.ToString());
   BossCombatPoseReview.Capture();
  }
  [MenuItem("Tools/Boss Prototype/Polish Blade Edge and Chest Laser")]
  public static void Apply()
  {
   BossSwordPolishInstaller.Apply();
   var path=Root+"/Generated/BossMecha.prefab";
   var prefab=PrefabUtility.LoadPrefabContents(path);
   try {Configure(prefab.GetComponent<BossCombatController>(),true);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
   finally{PrefabUtility.UnloadPrefabContents(prefab);}
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var combat=UnityEngine.Object.FindFirstObjectByType<BossCombatController>();Configure(combat,false);
   EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);EditorSceneManager.SaveScene(combat.gameObject.scene);AssetDatabase.SaveAssets();
   Debug.Log("COMBAT FINISH APPLY PASS: in-place retreat, edge alignment and chest laser.");
  }
  public static void Configure(BossCombatController combat,bool bake)
  {
   var driver=combat.driver;var sword=combat.sword;
   if(bake)foreach(var d in combat.attacks.Where(d=>d.IsSword&&d.clip))BakeEdge(driver,sword,d);
   driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle").SampleAnimation(driver.animator.gameObject,0);
   var spine=driver.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:Spine");
   var muzzle=spine.Find("ChestLaserMuzzle");if(!muzzle){muzzle=new GameObject("ChestLaserMuzzle").transform;muzzle.SetParent(spine,false);}
   muzzle.SetPositionAndRotation(driver.transform.TransformPoint(new Vector3(0,2.2f,.5f)),driver.transform.rotation);
   var laser=combat.GetComponent<BossChestLaser>();if(!laser)laser=combat.gameObject.AddComponent<BossChestLaser>();
   laser.muzzle=muzzle;laser.combat=combat;combat.laser=laser;EditorUtility.SetDirty(laser);EditorUtility.SetDirty(combat);
  }
  static void BakeEdge(BossAnimationDriver driver,BossSwordAttachment sword,BossAttackDefinition d)
  {
   int count=Mathf.CeilToInt(d.clip.length*120);var tips=new Vector3[count+1];var rotations=new Quaternion[count+1];
   for(int i=0;i<=count;i++)
   {
    d.clip.SampleAnimation(driver.animator.gameObject,d.clip.length*i/count);
    rotations[i]=sword.rightHand.rotation*Quaternion.Euler(sword.handEuler);
    tips[i]=sword.rightHand.TransformPoint(sword.handPosition)+rotations[i]*Vector3.down*(2.25f*(sword.rightGripY-.015f));
   }
   var keys=new List<Keyframe>{new Keyframe(0,0)};float last=0;bool first=true;
   for(int i=Mathf.CeilToInt(d.windupEnd*count);i<=Mathf.FloorToInt(d.activeEnd*count);i++)
   {
    var up=rotations[i]*Vector3.up;
    var velocity=Vector3.ProjectOnPlane(tips[Mathf.Min(count,i+1)]-tips[Mathf.Max(0,i-1)],up);
    if(velocity.sqrMagnitude<.000001f)continue;
    float angle=Vector3.SignedAngle(rotations[i]*Vector3.right,velocity,up);
    // Both edges cut: unwrap modulo 180 to avoid unnecessary handle spins.
    angle=last+Mathf.Repeat(angle-last+90,180)-90;
    if(first){keys.Add(new Keyframe(Mathf.Max(.02f,d.windupEnd-.08f),angle));first=false;}
    keys.Add(new Keyframe((float)i/count,angle));last=angle;
   }
   keys.Add(new Keyframe(Mathf.Min(.98f,d.activeEnd+.10f),last));keys.Add(new Keyframe(1,Mathf.Round(last/360)*360));
   d.bladeRoll=new AnimationCurve(keys.ToArray());
   for(int i=0;i<d.bladeRoll.length;i++){AnimationUtility.SetKeyLeftTangentMode(d.bladeRoll,i,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(d.bladeRoll,i,AnimationUtility.TangentMode.ClampedAuto);}
   EditorUtility.SetDirty(d);Debug.Log("EDGE BAKE "+d.kind+" keys="+keys.Count+" roll="+keys.Min(k=>k.value)+".."+keys.Max(k=>k.value));
  }
 }
}
