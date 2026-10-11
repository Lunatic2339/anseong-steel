using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
namespace AnseongSteel.RobotPlay.Editor
{
    public static class IntegratedNetworkBuild
    {
        [MenuItem("Anseong Steel/Robot Play/Build integrated LAN validation player")]
        public static void Build()
        {
            RobotCombatChecks.Run();IntegratedRulesChecks.Run();IntegratedPlayBuilder.UpdateOwnedScene();
            var options=new BuildPlayerOptions{scenes=new[]{IntegratedPlayBuilder.ScenePath},locationPathName="RobotPlayValidation/IntegratedPlayer/IntegratedPlay.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development};
            var result=BuildPipeline.BuildPlayer(options);
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Integrated player build failed: "+result.summary.result);
        }
    }
}
