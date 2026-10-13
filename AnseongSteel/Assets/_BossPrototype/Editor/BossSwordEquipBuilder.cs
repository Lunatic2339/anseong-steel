using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace AnseongSteel.Bosses.Editor
{
 // Retarget real Mixamo clips. No authored arm trajectories or synthetic body motion.
 public static class BossSwordEquipBuilder
 {
  const string Root="Assets/_BossPrototype";
  const float Speed=.85f, Entry=.16f, Join=.12f, Exit=.26f;
  struct Pose {public Vector3[] p; public Quaternion[] q;}
  static Pose Read(Transform[] bones)=>new Pose{p=bones.Select(b=>b.localPosition).ToArray(),q=bones.Select(b=>b.localRotation).ToArray()};
  static void Set(Transform[] bones,Pose p){for(int i=0;i<bones.Length;i++){bones[i].localPosition=p.p[i];bones[i].localRotation=p.q[i];}}
  static Pose Blend(Pose a,Pose b,float t)=>new Pose{p=a.p.Select((p,i)=>Vector3.Lerp(p,b.p[i],t)).ToArray(),q=a.q.Select((q,i)=>Quaternion.Slerp(q,b.q[i],t)).ToArray()};
  static AnimationClip Source(string name,string prefix){
   var path=Root+"/Animation/CombatSource/"+name+".fbx";
   var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.animationType=ModelImporterAnimationType.Generic;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.SaveAndReimport();
   var original=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var clip=new AnimationClip{name=name};
   foreach(var b in AnimationUtility.GetCurveBindings(original)){
    if(b.type!=typeof(Transform)||!b.path.Contains("mixamorig:"))continue;
    var dest=b;dest.path=prefix+b.path.Substring(b.path.IndexOf("mixamorig:",StringComparison.Ordinal));
    AnimationUtility.SetEditorCurve(clip,dest,AnimationUtility.GetEditorCurve(original,b));
   }
   return clip;
  }
  [MenuItem("Tools/Boss Prototype/Build Mixamo Sword Draw and Sheathe")]
  public static void Build(){
   string path=Root+"/Generated/BossMecha.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
   try{Configure(prefab.GetComponent<BossAnimationDriver>(),true);PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);var driver=Object.FindFirstObjectByType<BossModelDemo>().driver;
   Configure(driver,false);EditorSceneManager.MarkSceneDirty(driver.gameObject.scene);EditorSceneManager.SaveScene(driver.gameObject.scene);AssetDatabase.SaveAssets();Review();
   Debug.Log("MIXAMO EQUIP BUILD PASS: Draw A Great Sword 1/2 + Sheath A Great Sword 1/2");
  }
  static void Configure(BossAnimationDriver driver,bool bake){
   var animator=driver.animator;var controller=(AnimatorController)animator.runtimeAnimatorController;
   var bones=animator.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToArray();var rest=Read(bones);
   var sword=driver.GetComponent<BossSwordAttachment>();var equip=driver.GetComponent<BossSwordEquip>()??driver.gameObject.AddComponent<BossSwordEquip>();
   var idle=controller.animationClips.Single(c=>c.name=="Idle");var guard=controller.animationClips.Single(c=>c.name=="SwordIdle");
   var hip=AnimationUtility.GetCurveBindings(idle).First(b=>b.path.EndsWith("mixamorig:Hips")).path;
   var prefix=hip.Substring(0,hip.IndexOf("mixamorig:",StringComparison.Ordinal));
   var inputs=new[]{Source("DrawGreatSword1",prefix),Source("DrawGreatSword2",prefix),Source("SheathGreatSword1",prefix),Source("SheathGreatSword2",prefix)};
   Set(bones,rest);idle.SampleAnimation(animator.gameObject,0);var neutral=Read(bones);
   Set(bones,rest);guard.SampleAnimation(animator.gameObject,0);var armed=Read(bones);
   int hips=Array.FindIndex(bones,b=>b.name=="mixamorig:Hips");
   Pose Sample(AnimationClip clip,float t){Set(bones,rest);clip.SampleAnimation(animator.gameObject,t);var pose=Read(bones);pose.p[hips].x=neutral.p[hips].x;pose.p[hips].y=neutral.p[hips].y;return pose;}
   float DrawTime=Entry+(inputs[0].length+Join+inputs[1].length)/Speed+Exit;
   float SheathTime=Entry+(inputs[2].length+Join+inputs[3].length)/Speed+Exit;
   float drawContact=Entry+inputs[0].length/Speed;
   float sheathContact=Entry+(inputs[2].length+Join+.12f)/Speed;
   equip.drawContact=drawContact/DrawTime;equip.sheatheContact=sheathContact/SheathTime;
   // Fit the back mount to the captured hand contact, preserving the original motion.
   Set(bones,Sample(inputs[0],inputs[0].length));
   var rotation=sword.rightHand.rotation*Quaternion.Euler(sword.handEuler);
   var scale=sword.sword.lossyScale;
   sword.sword.localPosition=Vector3.zero;sword.sword.localRotation=Quaternion.identity;
   sword.backSocket.rotation=rotation;
   sword.backSocket.position=sword.rightHand.TransformPoint(sword.handPosition)-rotation*Vector3.Scale(new Vector3(0,sword.rightGripY,0),scale);
   sword.backSocket.position-=driver.transform.forward*.12f;
   Debug.Log("MIXAMO MOUNT blade="+(-sword.sword.up).ToString("F3")+" draw="+equip.drawContact+" sheath="+equip.sheatheContact);
   if(bake)foreach(bool draw in new[]{true,false}){
    var a=inputs[draw?0:2];var b=inputs[draw?1:3];float duration=draw?DrawTime:SheathTime;
    var start=draw?neutral:armed;var end=draw?armed:neutral;
    var first=Sample(a,0);var lastA=Sample(a,a.length);var firstB=Sample(b,0);var last=Sample(b,b.length);
    int count=Mathf.CeilToInt(duration*60)+1;var poses=new Pose[count];
    for(int f=0;f<count;f++){
     float time=Mathf.Min(f/60f,duration),t=(time-Entry)*Speed;
     Pose pose;
     if(time<Entry)pose=Blend(start,first,Mathf.SmoothStep(0,1,time/Entry));
     else if(t<=a.length)pose=Sample(a,t);
     else if(t<a.length+Join)pose=Blend(lastA,firstB,Mathf.SmoothStep(0,1,(t-a.length)/Join));
     else if(t<=a.length+Join+b.length)pose=Sample(b,t-a.length-Join);
     else pose=Blend(last,end,Mathf.SmoothStep(0,1,(time-(duration-Exit))/Exit));
     Set(bones,pose);
     // A short contact constraint accounts for the difference between the two mocap takes.
     // Outside the socket contact window, the Mixamo body and wrist rotations are unchanged.
     if(draw){float w=1-Mathf.SmoothStep(0,1,Mathf.Abs(time-drawContact)/.32f);if(w>0)Contact(sword,w);}
     if(!draw){
      float w=Mathf.SmoothStep(0,1,Mathf.InverseLerp(sheathContact-.85f,sheathContact-.16f,time))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(sheathContact+.10f,sheathContact+.42f,time)));
      if(w>0)Contact(sword,w);
     }
     poses[f]=Read(bones);
    }
    var result=new AnimationClip{name=draw?"DrawSword":"SheatheSword",frameRate=60};
    for(int i=0;i<bones.Length;i++)for(int axis=0;axis<7;axis++){
     var curve=new AnimationCurve();for(int f=0;f<count;f++)curve.AddKey(Mathf.Min(f/60f,duration),axis<3?poses[f].p[i][axis]:poses[f].q[i][axis-3]);
     for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
     AnimationUtility.SetEditorCurve(result,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[i],animator.transform),typeof(Transform),(axis<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[axis<3?axis:axis-3]),curve);
    }
    result.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(result);settings.loopTime=false;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(result,settings);
    var output=Root+"/Animation/Combat/"+result.name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
    if(clip){EditorUtility.CopySerialized(result,clip);Object.DestroyImmediate(result);EditorUtility.SetDirty(clip);}else{clip=result;AssetDatabase.CreateAsset(clip,output);}
    var sm=controller.layers[0].stateMachine;var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==clip.name)??sm.AddState(clip.name);state.motion=clip;state.writeDefaultValues=false;
   }
   Set(bones,rest);foreach(var clip in inputs)Object.DestroyImmediate(clip);EditorUtility.SetDirty(equip);EditorUtility.SetDirty(sword);EditorUtility.SetDirty(controller);
  }
  static void Contact(BossSwordAttachment sword,float weight){
   var hand=sword.rightHand;var lower=hand.parent;var upper=lower.parent;
   var originalUpper=upper.rotation;var originalLower=lower.rotation;var originalHand=hand.rotation;
   var rotation=sword.sword.rotation*Quaternion.Inverse(Quaternion.Euler(sword.handEuler));
   var wrist=sword.sword.TransformPoint(new Vector3(0,sword.rightGripY,0))-rotation*Vector3.Scale(sword.handPosition,hand.lossyScale);
   var origin=upper.position;float a=Vector3.Distance(origin,lower.position),b=Vector3.Distance(lower.position,hand.position);
   var delta=wrist-origin;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);var dir=delta.normalized;
   var pole=Vector3.ProjectOnPlane(lower.position-origin,dir).normalized;
   float along=(a*a-b*b+distance*distance)/(2*distance);
   var elbow=origin+dir*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
   upper.rotation=Quaternion.FromToRotation(lower.position-origin,elbow-origin)*upper.rotation;
   lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,origin+dir*distance-lower.position)*lower.rotation;hand.rotation=rotation;
   var solvedUpper=upper.rotation;var solvedLower=lower.rotation;var solvedHand=hand.rotation;
   upper.rotation=Quaternion.Slerp(originalUpper,solvedUpper,weight);lower.rotation=Quaternion.Slerp(originalLower,solvedLower,weight);hand.rotation=Quaternion.Slerp(originalHand,solvedHand,weight);
  }
  public static void Review(){
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);var driver=Object.FindFirstObjectByType<BossModelDemo>().driver;
   driver.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);var sword=driver.GetComponent<BossSwordAttachment>();sword.Initialize();sword.PrepareEquip();
   var equip=driver.GetComponent<BossSwordEquip>();var hands=driver.GetComponent<BossHandPose>();
   var bones=driver.animator.GetComponentsInChildren<Transform>().Where(b=>b.name.StartsWith("mixamorig:")).ToArray();var rest=Read(bones);
   System.IO.Directory.CreateDirectory("Library/MixamoEquipReview");
   foreach(bool draw in new[]{true,false})for(int i=0;i<=10;i++){
    float n=i/10f;var clip=driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name==(draw?"DrawSword":"SheatheSword"));
    sword.Sheathe();Set(bones,rest);clip.SampleAnimation(driver.animator.gameObject,n*clip.length);
    if(draw?n>=equip.drawContact:n<equip.sheatheContact)sword.Draw();
    hands.Apply(0,1,sword.IsDrawn?1:0,0);
    var baked=BossHandRigWorkshop.Bake(driver.animator);var cam=Camera.main;cam.orthographic=true;cam.orthographicSize=2.4f;cam.nearClipPlane=.01f;
    cam.transform.position=new Vector3(4,3,6);cam.transform.LookAt(new Vector3(0,1.7f,0));BossHandRigWorkshop.Render(cam,"Library/MixamoEquipReview/"+(draw?"Draw":"Sheath")+i.ToString("D2")+".png");
    foreach(var go in baked){Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);}hands.Restore();
   }
  }
 }
}
