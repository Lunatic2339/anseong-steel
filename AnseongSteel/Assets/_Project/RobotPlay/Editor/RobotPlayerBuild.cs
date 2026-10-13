using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace AnseongSteel.RobotPlay.Editor
{
    public static class RobotPlayerBuild
    {
        public static void Build()
        {
            var options=new BuildPlayerOptions {scenes=new[]{RobotPlayBuilder.ScenePath},locationPathName="RobotPlayValidation/Player/RobotPlay.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development};
            var result=BuildPipeline.BuildPlayer(options);
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Robot player build failed: "+result.summary.result);
        }
    }
}
