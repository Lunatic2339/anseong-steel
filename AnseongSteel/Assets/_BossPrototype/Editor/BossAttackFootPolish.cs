using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossAttackFootPolish
 {
  const string Root="Assets/_BossPrototype";
  const string Stamp="BossFootContacts/v1=";
  static readonly string[] ClipNames={"SwordSlash","OverheadSmash","AlternatingCombo","Grab","Feint","GroundCast","PowerUp"};
  static string Digest(string path){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
  static bool AlreadyPolished(string path)=>AssetImporter.GetAtPath(path).userData.Split('\n').Contains(Stamp+Digest(path));
  public static void StampCurrent()
  {
   foreach(string name in ClipNames)
   {
    string path=Root+"/Animation/Combat/"+name+".anim";var importer=AssetImporter.GetAtPath(path);
    importer.userData=string.Join("\n",importer.userData.Split('\n').Where(s=>s.Length>0&&!s.StartsWith(Stamp)).Concat(new[]{Stamp+Digest(path)}));
    AssetDatabase.WriteImportSettingsIfDirty(path);
   }
  }
  public static void FinalizeReview(){StampCurrent();Apply();BossCombatFinishInstaller.Review();}
  public static void InstallPoseInput()
  {
   string path=Root+"/Generated/BossMecha.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
   try{if(!prefab.GetComponent<BossFootPoseInput>())prefab.AddComponent<BossFootPoseInput>();PrefabUtility.SaveAsPrefabAsset(prefab,path);}
   finally{PrefabUtility.UnloadPrefabContents(prefab);}
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);var driver=Object.FindFirstObjectByType<BossModelDemo>().driver;
   if(!driver.GetComponent<BossFootPoseInput>())driver.gameObject.AddComponent<BossFootPoseInput>();
   EditorSceneManager.MarkSceneDirty(driver.gameObject.scene);EditorSceneManager.SaveScene(driver.gameObject.scene);AssetDatabase.SaveAssets();
  }
  [MenuItem("Tools/Boss Prototype/Polish Attack Foot Contacts")]
  public static void Apply()
  {
   var prefab=PrefabUtility.LoadPrefabContents(Root+"/Generated/BossMecha.prefab");
   try
   {
    var animator=prefab.GetComponent<BossAnimationDriver>().animator;
    var restBones=animator.GetComponentsInChildren<Transform>();var restRotations=restBones.Select(t=>t.localRotation).ToArray();
    foreach(string name in ClipNames)
    {
     string path=Root+"/Animation/Combat/"+name+".anim";
     if(AlreadyPolished(path))continue;
     for(int i=0;i<restBones.Length;i++)restBones[i].localRotation=restRotations[i];
     Polish(animator,AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
    }
   }
   finally{PrefabUtility.UnloadPrefabContents(prefab);}
   AssetDatabase.SaveAssets();StampCurrent();InstallPoseInput();Debug.Log("ATTACK FOOT CONTACTS SAVED");
  }
  public static void ApplyAndReview(){Apply();BossAttackPoseAudit.Run();}
  static void Polish(Animator animator,AnimationClip clip)
  {
   if(!clip)throw new InvalidOperationException("Attack clip missing");
   var bones=animator.GetComponentsInChildren<Transform>();
   Transform Bone(string name)=>bones.Single(t=>t.name=="mixamorig:"+name);
   var joints=new[]{Bone("LeftUpLeg"),Bone("LeftLeg"),Bone("LeftFoot"),Bone("RightUpLeg"),Bone("RightLeg"),Bone("RightFoot")};
   var idle=animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle");
   var restRotations=joints.Select(t=>t.localRotation).ToArray();
   void Sample(float time)
   {
    // Imported constant foot channels are absent. Never feed the last IK result
    // back into the next source sample.
    for(int j=0;j<joints.Length;j++)joints[j].localRotation=restRotations[j];
    idle.SampleAnimation(animator.gameObject,0);clip.SampleAnimation(animator.gameObject,time);
   }
   idle.SampleAnimation(animator.gameObject,0);
   var floor=new[]{joints[2].position.y,joints[5].position.y};
   int count=Mathf.CeilToInt(clip.length*60)+1;
   var positions=new Vector3[2,count];var targets=new Vector3[2,count];
   var footRotations=new Quaternion[2,count];
   for(int i=0;i<count;i++)
   {
    Sample(Mathf.Min(i/60f,clip.length));
    for(int side=0;side<2;side++){positions[side,i]=joints[side*3+2].position;footRotations[side,i]=joints[side*3+2].rotation;}
   }
   for(int side=0;side<2;side++)
   {
    var anchor=positions[side,0];bool grounded=false;
    for(int i=0;i<count;i++)
    {
     var p=positions[side,i];float height=p.y-floor[side];
     if(height>=.065f)grounded=false;
     if(!grounded&&height<.045f){anchor=p;grounded=true;}
     float weight=grounded?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.018f,.065f,height)):0;
     // Keep vertical lift and toe pivot; only remove horizontal contact drift.
     var target=new Vector3(anchor.x,p.y,anchor.z);
     targets[side,i]=Vector3.Lerp(p,target,weight);
    }
    // Spread release over time: a height threshold alone makes a planted foot
    // snap back to the source trajectory on fast lifts. Bound the correction
    // so long lunges retain their intended travel and remain within leg reach.
    var offsets=new Vector3[count];
    for(int i=0;i<count;i++)offsets[i]=Vector3.ClampMagnitude(targets[side,i]-positions[side,i],.30f);
    for(int i=0;i<count;i++)
    {
     var offset=Vector3.zero;float total=0;
     for(int k=-15;k<=15;k++)
     {
      int index=Mathf.Clamp(i+k,0,count-1);float w=Mathf.Exp(-k*k/50f);
      offset+=offsets[index]*w;total+=w;
     }
     float t=Mathf.Min(i/60f,clip.length);
     float edge=Mathf.SmoothStep(0,1,t/.10f)*Mathf.SmoothStep(0,1,(clip.length-t)/.15f);
     targets[side,i]=positions[side,i]+offset/total*edge;
    }
   }
   var curves=new AnimationCurve[6,4];for(int j=0;j<6;j++)for(int axis=0;axis<4;axis++)curves[j,axis]=new AnimationCurve();
   for(int i=0;i<count;i++)
   {
    float time=Mathf.Min(i/60f,clip.length);Sample(time);
    for(int side=0;side<2;side++)Solve(joints[side*3],joints[side*3+1],joints[side*3+2],targets[side,i]);
    for(int j=0;j<6;j++)for(int axis=0;axis<4;axis++)curves[j,axis].AddKey(time,joints[j].localRotation[axis]);
   }
   for(int j=0;j<6;j++)for(int axis=0;axis<4;axis++)
   {
    var curve=curves[j,axis];for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(joints[j],animator.transform),typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),curve);
   }
   clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);
   float rotationError=0;
   for(int i=0;i<count;i++)
   {
    Sample(Mathf.Min(i/60f,clip.length));
    for(int side=0;side<2;side++)rotationError=Mathf.Max(rotationError,Quaternion.Angle(footRotations[side,i],joints[side*3+2].rotation));
   }
   Debug.Log("FOOT ROTATION "+clip.name+" error="+rotationError);
   if(rotationError>1)throw new InvalidOperationException("Foot orientation changed: "+clip.name);
  }
  static void Solve(Transform upper,Transform lower,Transform foot,Vector3 target)
  {
   var root=upper.position;float a=Vector3.Distance(root,lower.position),b=Vector3.Distance(lower.position,foot.position);
   if(Vector3.Distance(target,root)>a+b-.0001f)
   {
    float low=0,high=1;var original=foot.position;
    for(int i=0;i<18;i++){float t=(low+high)*.5f;if(Vector3.Distance(Vector3.Lerp(original,target,t),root)>a+b-.0001f)high=t;else low=t;}
    target=Vector3.Lerp(original,target,low);
   }
   var rotation=foot.rotation;var delta=target-root;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);var direction=delta.normalized;
   var pole=Vector3.ProjectOnPlane(lower.position-root,direction).normalized;
   if(pole.sqrMagnitude<.01f)return;
   float x=(a*a-b*b+distance*distance)/(2*distance);
   var knee=root+direction*x+pole*Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
   upper.rotation=Quaternion.FromToRotation(lower.position-root,knee-root)*upper.rotation;
   lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,root+direction*distance-lower.position)*lower.rotation;
   foot.rotation=rotation;
  }
 }
}
