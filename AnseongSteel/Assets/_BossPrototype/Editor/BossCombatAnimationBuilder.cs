using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossCombatAnimationBuilder
 {
  const string Root="Assets/_BossPrototype";
  static AnimationClip Source(string name)=>AssetDatabase.LoadAllAssetsAtPath(Root+"/Animation/CombatSource/"+name+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  [MenuItem("Tools/Boss Prototype/Build Downloaded Combat Animations")]
  public static void Build()
  {
   if(!AssetDatabase.IsValidFolder(Root+"/Animation/Combat"))AssetDatabase.CreateFolder(Root+"/Animation","Combat");
   var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/Generated/BossAnimator.controller");
   var idle=controller.animationClips.Single(c=>c.name=="Idle");
   string hipPath=AnimationUtility.GetCurveBindings(idle).First(b=>b.path.EndsWith("mixamorig:Hips")).path;
   string prefix=hipPath.Substring(0,hipPath.IndexOf("mixamorig:",StringComparison.Ordinal));
   var sources=new Dictionary<string,AnimationClip>();
   foreach(string name in new[]{"SwordSlash","OverheadSmash","AlternatingCombo","Grab","SwordIdle","GroundCast","PowerUp","Stagger","Death","DrawReach","DrawPull"})sources[name]=Source(name);
   foreach(var entry in sources)Debug.Log("COMBAT SOURCE "+entry.Key+" firstBinding="+AnimationUtility.GetCurveBindings(entry.Value)[0].path+" / "+AnimationUtility.GetCurveBindings(entry.Value)[0].propertyName);
   var clips=new Dictionary<string,AnimationClip>();
   foreach(var entry in sources)
   {
    float duration=entry.Key=="Grab"?1.15f:entry.Value.length;
    clips[entry.Key]=Bake(entry.Key,new[]{entry.Value},duration,t=>(entry.Value,Mathf.Min(t,duration)),prefix,hipPath,idle,entry.Key=="SwordIdle");
   }
   var reach=sources["DrawReach"];var pull=sources["DrawPull"];float total=reach.length+pull.length;
   clips["DrawSword"]=Bake("DrawSword",new[]{reach,pull},total,t=>t<=reach.length?(reach,t):(pull,t-reach.length),prefix,hipPath,idle,false);
   clips["SheatheSword"]=Bake("SheatheSword",new[]{reach,pull},total,t=>total-t<=reach.length?(reach,total-t):(pull,total-t-reach.length),prefix,hipPath,idle,false);
   var slash=sources["OverheadSmash"];float feintLength=.75f+slash.length*.9f;
   clips["Feint"]=Bake("Feint",new[]{slash},feintLength,t=>(slash,t<.45f?Mathf.Lerp(0,slash.length*.3f,t/.45f):t<.75f?Mathf.Lerp(slash.length*.3f,slash.length*.1f,(t-.45f)/.3f):slash.length*.1f+t-.75f),prefix,hipPath,idle,false);
   foreach(var item in new[]{("SwordSlash","SwordSlash",.24f,.67f),("OverheadSmash","OverheadSmash",.28f,.63f),("AlternatingCombo","AlternatingCombo",.14f,.42f),("Grab","Grab",.55f,.91f),("ArmLock","Grab",.55f,.91f),("WeaponBreak","SwordSlash",.24f,.67f),("Shockwave","GroundCast",.4f,.72f),("EMP","PowerUp",.28f,.75f),("SensorJam","PowerUp",.28f,.75f),("VisionDisruption","PowerUp",.28f,.75f),("Feint","Feint",.46f,.80f)})
   {
    var d=AssetDatabase.LoadAssetAtPath<BossAttackDefinition>(Root+"/Combat/"+item.Item1+".asset");d.clip=clips[item.Item2];d.windupEnd=item.Item3;d.activeEnd=item.Item4;
    if(d.kind==BossAttackKind.AlternatingCombo)d.strikeTimes=new[]{.2f,.63f};
    EditorUtility.SetDirty(d);
   }
   foreach(string name in new[]{"SwordIdle","DrawSword","SheatheSword","Stagger","Death"})
   {
    var sm=controller.layers[0].stateMachine;var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??sm.AddState(name);state.motion=clips[name];state.writeDefaultValues=false;
   }
   EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();BossCombatInstaller.BindClips();
   BossCombatInstaller.Install();
   CalibrateSword(clips["SwordIdle"]);
   Debug.Log("COMBAT ANIMATIONS PASS: same-v026 Generic curves retargeted to "+prefix+"; ground-plane hip drift removed.");
  }
  static AnimationClip Bake(string name,AnimationClip[] inputs,float duration,Func<float,(AnimationClip,float)> sample,string prefix,string hipPath,AnimationClip idle,bool loop)
  {
   var result=new AnimationClip{name=name,frameRate=60};
   var all=inputs.SelectMany(AnimationUtility.GetCurveBindings).Where(b=>b.type==typeof(Transform)&&b.path.Contains("mixamorig:")).GroupBy(b=>b.path+"|"+b.propertyName).Select(g=>g.First()).ToArray();
   foreach(var binding in all)
   {
    var destination=binding;destination.path=prefix+binding.path.Substring(binding.path.IndexOf("mixamorig:",StringComparison.Ordinal));
    var curves=inputs.ToDictionary(c=>c,c=>AnimationUtility.GetEditorCurve(c,binding));
    var reference=AnimationUtility.GetEditorCurve(idle,destination);
    int count=Mathf.CeilToInt(duration*60)+1;var keys=new Keyframe[count];
    for(int i=0;i<count;i++)
    {
     float time=Mathf.Min(i/60f,duration);var point=sample(time);var curve=curves[point.Item1];
     float value=curve!=null?curve.Evaluate(point.Item2):reference!=null?reference.Evaluate(0):0;
     // This rig is Z-up under its Armature (-90deg X), so X/Y are the ground plane.
     if(destination.path==hipPath&&(binding.propertyName=="m_LocalPosition.x"||binding.propertyName=="m_LocalPosition.y")&&reference!=null)value=reference.Evaluate(0);
     keys[i]=new Keyframe(time,value);
    }
    var baked=new AnimationCurve(keys);for(int i=0;i<count;i++){AnimationUtility.SetKeyLeftTangentMode(baked,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(baked,i,AnimationUtility.TangentMode.Linear);}
    AnimationUtility.SetEditorCurve(result,destination,baked);
   }
   result.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(result);settings.loopTime=loop;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(result,settings);
   string path=Root+"/Animation/Combat/"+name+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
   if(existing){EditorUtility.CopySerialized(result,existing);UnityEngine.Object.DestroyImmediate(result);result=existing;EditorUtility.SetDirty(result);}else AssetDatabase.CreateAsset(result,path);
   return result;
  }
  static void CalibrateSword(AnimationClip stance)
  {
   string path=Root+"/Generated/BossMecha.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
   try{Calibrate(prefab.GetComponent<BossAnimationDriver>(),stance);PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);var demo=UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();Calibrate(demo.driver,stance);
   EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);EditorSceneManager.SaveScene(demo.gameObject.scene);AssetDatabase.SaveAssets();
  }
  static void Calibrate(BossAnimationDriver driver,AnimationClip stance)
  {
   BossSwordPolishInstaller.ConfigureGrip(driver);
  }
 }
}

