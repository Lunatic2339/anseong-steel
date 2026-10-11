using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace AnseongSteel.Cockpit.Editor {
 public static class CockpitVariantsBuildAll {
  [MenuItem("Anseong Steel/Cockpit/Variants/Build All Three")]
  public static void Build() {
   if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
   CockpitVariant_A_CloseArc.Build();
   CockpitVariant_B_RearDome.Build();
   CockpitVariant_C_SideDome.Build();
   AssetDatabase.SaveAssets();
   var output=Environment.GetEnvironmentVariable("COCKPIT_VARIANTS_OUTPUT") ?? Path.GetFullPath("CockpitVariantReview");
   Directory.CreateDirectory(output);
   AssetDatabase.ExportPackage("Assets/CockpitVariants",Path.Combine(output,"CockpitVariants.unitypackage"),ExportPackageOptions.Recurse);
   UnityEngine.Debug.Log("COCKPIT_VARIANTS_ALL_OK");
  }
 }
}
