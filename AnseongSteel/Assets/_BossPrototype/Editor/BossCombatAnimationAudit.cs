using System.Linq;
using UnityEditor;
using UnityEngine;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossCombatAnimationAudit
 {
  public static void Run()
  {
   AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
   foreach(string guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/_BossPrototype/Animation/CombatSource"}))
   {
    string path=AssetDatabase.GUIDToAssetPath(guid);var imp=(ModelImporter)AssetImporter.GetAtPath(path);
    imp.animationType=ModelImporterAnimationType.Generic;imp.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;imp.importAnimation=true;imp.animationCompression=ModelImporterAnimationCompression.Off;imp.SaveAndReimport();
    var c=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(a=>!a.name.StartsWith("__preview__"));
    var b=AnimationUtility.GetCurveBindings(c);
    var hips=b.Where(a=>a.path.EndsWith("mixamorig:Hips")&&a.propertyName.Contains("Position")).Select(a=>a.path+"/"+a.propertyName+"="+AnimationUtility.GetEditorCurve(c,a).Evaluate(0));
    Debug.Log("CLIP AUDIT "+path+" duration="+c.length+" curves="+b.Length+" hips="+string.Join(";",hips));
   }
  }
 }
}
