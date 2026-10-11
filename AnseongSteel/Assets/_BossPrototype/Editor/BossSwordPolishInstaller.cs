using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossSwordPolishInstaller
 {
  const string Root="Assets/_BossPrototype";
  [MenuItem("Tools/Boss Prototype/Polish Sword Grip and Test Distances")]
  public static void Apply()
  {
   BuildBackwardWalk();
   foreach(var guid in AssetDatabase.FindAssets("t:BossAttackDefinition",new[]{Root+"/Combat"}))
   {
    var d=AssetDatabase.LoadAssetAtPath<BossAttackDefinition>(AssetDatabase.GUIDToAssetPath(guid));
    d.preferredDistance=d.IsSword?2.8f:1.05f;
    if(d.IsSword)d.range=Mathf.Max(d.range,3.4f);
    if(d.kind==BossAttackKind.MissileBarrage||d.kind==BossAttackKind.LaserSweep)d.preferredDistance=5f;
    if(d.kind==BossAttackKind.EMP||d.kind==BossAttackKind.SensorJam||d.kind==BossAttackKind.VisionDisruption)d.preferredDistance=3.5f;
    EditorUtility.SetDirty(d);
   }
   string path=Root+"/Generated/BossMecha.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
   try{ConfigureGrip(prefab.GetComponent<BossAnimationDriver>());PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
   var delta=demo.target.position-demo.driver.transform.position;delta.y=0;if(delta.sqrMagnitude<.01f)delta=Vector3.forward;delta.Normalize();
   demo.driver.transform.SetPositionAndRotation(new Vector3(demo.target.position.x, demo.driver.transform.position.y,demo.target.position.z)-delta*2.8f,Quaternion.LookRotation(delta));
   ConfigureGrip(demo.driver);demo.approachDistance=2.8f;demo.autoDemo=false;
   var tester=demo.GetComponent<BossCombatTester>();tester.swordTestDistance=2.8f;tester.positionForAttack=true;tester.reviveTestTarget=true;
   var view=demo.GetComponent<BossCockpitView>();
   view.cockpitPosition=new Vector3(demo.target.position.x,2.25f,demo.target.position.z)+delta*.55f;
   view.cockpitEuler=Quaternion.LookRotation(-delta+Vector3.down*.20f).eulerAngles;
   view.SetCockpitView(true);
   EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);EditorSceneManager.SaveScene(demo.gameObject.scene);AssetDatabase.SaveAssets();
   Debug.Log("SWORD POLISH PASS: palm grip, support-hand IK, separate melee distances and first-person framing.");
  }
  public static void ConfigureGrip(BossAnimationDriver driver)
  {
   var a=driver.GetComponent<BossSwordAttachment>();if(!a)return;
   var bones=driver.animator.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First(), StringComparer.Ordinal);
   var stance=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/Combat/SwordIdle.anim");if(!stance)return;
   stance.SampleAnimation(driver.animator.gameObject,0);
   a.rightHand=bones["mixamorig:RightHand"];a.leftHand=bones["mixamorig:LeftHand"];
   a.backSocket=bones["BackSwordSocket"];a.sword=bones["BackGreatsword"];
   Vector3 Palm(string side)=>(bones["mixamorig:"+side+"HandMiddle1"].position+bones["mixamorig:"+side+"HandMiddle3"].position)*.5f;
   var blade=(bones["mixamorig:RightHandIndex1"].position-bones["mixamorig:RightHandPinky1"].position).normalized;
   var normal=Vector3.ProjectOnPlane(driver.transform.forward,blade).normalized;
   var gripRotation=Quaternion.LookRotation(normal,-blade);
   a.handPosition=a.rightHand.InverseTransformPoint(Palm("Right"));
   a.handEuler=(Quaternion.Inverse(a.rightHand.rotation)*gripRotation).eulerAngles;
   a.leftPalmLocal=a.leftHand.InverseTransformPoint(Palm("Left"));
   a.leftGripRotation=Quaternion.Inverse(gripRotation)*a.leftHand.rotation;
   a.rightGripY=.86f;a.leftGripY=.965f;
   a.fingerPose=bones.Values.Where(t=>t.name.Contains("Hand")&&!t.name.EndsWith("Hand")&&!t.name.EndsWith("_end")&&t.name.StartsWith("mixamorig:")).Select(t=>new BossSwordAttachment.FingerPose{bone=t,rotation=t.localRotation}).ToArray();
   driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle").SampleAnimation(driver.animator.gameObject,0);
   a.backSocket.position=driver.transform.TransformPoint(new Vector3(.37f,2.55f,-.34f));
   a.backSocket.rotation=driver.transform.rotation*Quaternion.Euler(0,0,-25);
   EditorUtility.SetDirty(a);
  }
  static void BuildBackwardWalk()
  {
   var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/Generated/BossAnimator.controller");
   var source=controller.animationClips.Single(c=>c.name=="Walk");var reversed=new AnimationClip{name="WalkBackward",frameRate=source.frameRate};
   foreach(var binding in AnimationUtility.GetCurveBindings(source))
   {
    var curve=AnimationUtility.GetEditorCurve(source,binding);
    // This rig's armature is rotated -90 degrees: local X/Y are horizontal.
    // Standalone reversed clips retain the imported hip travel unless removed.
    if(binding.path.EndsWith("mixamorig:Hips")&&(binding.propertyName=="m_LocalPosition.x"||binding.propertyName=="m_LocalPosition.y"))
    {
     var idle=controller.animationClips.Single(c=>c.name=="Idle");
     var reference=AnimationUtility.GetEditorCurve(idle,binding);
     float value=reference!=null?reference.Evaluate(0):curve.Evaluate(0);
     AnimationUtility.SetEditorCurve(reversed,binding,AnimationCurve.Constant(0,source.length,value));continue;
    }
    var keys=curve.keys.Reverse().Select(k=>new Keyframe(source.length-k.time,k.value,-k.outTangent,-k.inTangent,k.outWeight,k.inWeight){weightedMode=k.weightedMode}).ToArray();
    AnimationUtility.SetEditorCurve(reversed,binding,new AnimationCurve(keys));
   }
   var settings=AnimationUtility.GetAnimationClipSettings(source);settings.loopTime=true;settings.startTime=0;settings.stopTime=source.length;AnimationUtility.SetAnimationClipSettings(reversed,settings);reversed.EnsureQuaternionContinuity();
   string path=Root+"/Animation/Combat/WalkBackward.anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
   if(existing){EditorUtility.CopySerialized(reversed,existing);UnityEngine.Object.DestroyImmediate(reversed);reversed=existing;EditorUtility.SetDirty(existing);}else AssetDatabase.CreateAsset(reversed,path);
   var sm=controller.layers[0].stateMachine;var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="WalkBackward")??sm.AddState("WalkBackward");state.motion=reversed;state.writeDefaultValues=false;EditorUtility.SetDirty(controller);
  }
 }
}

