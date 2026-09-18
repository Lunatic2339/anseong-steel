using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using AnseongSteel.PilotV04;

[InitializeOnLoad]
public static class PilotV04PlayValidation
{
    const string Active="PilotV04.PlayCheck.Active",Finish="PilotV04.PlayCheck.Finish",ReportKey="PilotV04.PlayCheck.Report";
    static int stage,frame;
    static Keyboard keyboard;
    static PilotReviewControls controls;
    static Vector3 initial;
    static Result result;
    static InputSettings originalSettings,temporarySettings;
    static double started;
    static PilotV04PlayValidation(){EditorApplication.update+=Tick;started=EditorApplication.timeSinceStartup;}
    public static void Run()
    {
        EditorSceneManager.OpenScene(PilotV04Builder.ScenePath);
        SessionState.SetBool(Active,true);SessionState.SetBool(Finish,false);
        EditorApplication.isPlaying=true;
    }
    public static void BuildAndRun(){PilotV04Builder.Build();Run();}
    static void Tick()
    {
        if(!SessionState.GetBool(Active,false))return;
        try
        {
            if(SessionState.GetBool(Finish,false))
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                string json=SessionState.GetString(ReportKey,"{}");
                File.WriteAllText(PilotV04Builder.Root+"/Validation/playmode_validation.json",json);
                AssetDatabase.Refresh();
                var r=JsonUtility.FromJson<Result>(json);
                SessionState.SetBool(Active,false);
                if(r.pass)AssetDatabase.ExportPackage(PilotV04Builder.Root,Path.GetFullPath("PilotV04_Review.unitypackage"),ExportPackageOptions.Recurse);
                Debug.Log("PILOT_PLAYMODE_"+(r.pass?"PASS ":"FAIL ")+json);
                EditorApplication.Exit(r.pass?0:1);return;
            }
            if(EditorApplication.timeSinceStartup-started>180)throw new Exception("Play-mode check timed out");
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<15)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(stage==0)
            {
                controls=UnityEngine.Object.FindFirstObjectByType<PilotReviewControls>();
                if(!controls||!controls.rig||!controls.rig.IsReady)throw new Exception("Serialized review rig missing after scene reload");
                controls.rig.SetPose(0);initial=controls.rig.left.target.position;
                originalSettings=InputSystem.settings;temporarySettings=UnityEngine.Object.Instantiate(originalSettings);
                temporarySettings.hideFlags=HideFlags.DontSave;
                temporarySettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                temporarySettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                temporarySettings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                InputSystem.settings=temporarySettings;Application.runInBackground=true;
                keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();
                result=new Result{serializedRigLoaded=true};
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));frame=Time.frameCount;stage=1;
            }
            else if(stage==1&&Time.frameCount-frame>=35)
            {
                result.targetTravel=Vector3.Distance(initial,controls.rig.left.target.position);
                result.keyboardMovesTarget=result.targetTravel>.001f;
                result.wristFollowsTarget=controls.rig.left.targetError<.001f;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());frame=Time.frameCount;stage=2;
            }
            else if(stage==2&&Time.frameCount-frame>=3)
            {InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.G));frame=Time.frameCount;stage=3;}
            else if(stage==3&&Time.frameCount-frame>=3)
            {
                result.keyboardGripWorks=controls.rig.leftGrip>.99f&&controls.rig.rightGrip>.99f;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());frame=Time.frameCount;stage=4;
            }
            else if(stage==4&&Time.frameCount-frame>=3)
            {InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Tab));frame=Time.frameCount;stage=5;}
            else if(stage==5&&Time.frameCount-frame>=4)
            {
                result.keyboardPoseWorks=controls.rig.left.target.position.y>1.5f;
                result.finalWristError=controls.rig.left.targetError;
                PilotV04Builder.Capture(controls.reviewCamera,PilotV04Builder.Root+"/Validation/playmode_guard.png",false);
                result.pass=result.serializedRigLoaded&&result.keyboardMovesTarget&&result.wristFollowsTarget&&result.keyboardGripWorks&&result.keyboardPoseWorks&&result.finalWristError<.001f;
                Complete();
            }
        }
        catch(Exception ex){result=result??new Result();result.error=ex.ToString();result.pass=false;Complete();}
    }
    static void Complete()
    {
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}
        if(originalSettings){InputSystem.settings=originalSettings;UnityEngine.Object.DestroyImmediate(temporarySettings);originalSettings=null;}
        SessionState.SetString(ReportKey,JsonUtility.ToJson(result,true));SessionState.SetBool(Finish,true);EditorApplication.isPlaying=false;
    }
    [Serializable] public class Result
    {
        public bool serializedRigLoaded,keyboardMovesTarget,wristFollowsTarget,keyboardGripWorks,keyboardPoseWorks,pass;
        public float targetTravel,finalWristError;public string error;
    }
}
