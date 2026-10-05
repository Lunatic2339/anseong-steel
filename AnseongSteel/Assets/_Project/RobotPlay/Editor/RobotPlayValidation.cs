using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AnseongSteel.RobotPlay.Editor
{
    [InitializeOnLoad]
    public static class RobotPlayValidation
    {
        const string Active="RobotPlay.Check.Active",Finish="RobotPlay.Check.Finish",Report="RobotPlay.Check.Report";
        static int stage;static float at;static double started;
        static Keyboard keyboard;static RobotPlaySession session;
        static InputSettings original,temporary;
        static readonly List<string> checks=new List<string>();
        static RobotPlayValidation(){EditorApplication.update+=Tick;started=EditorApplication.timeSinceStartup;}
        [MenuItem("Anseong Steel/Robot Play/Validate build and play")]
        public static void Run()
        {
            RobotCombatChecks.Run();RobotPlayBuilder.Build();
            SessionState.SetBool(Active,true);SessionState.SetBool(Finish,false);EditorApplication.isPlaying=true;
        }
        static void Assert(bool ok,string message)
        {if(!ok)throw new Exception(message+" | "+(session?JsonUtility.ToJson(session.State):"no session"));checks.Add("PASS: "+message);}
        static void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        static void Next(){stage++;at=Time.time;}
        static void Tick()
        {
            if(!SessionState.GetBool(Active,false))return;
            try
            {
                if(SessionState.GetBool(Finish,false))
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    Directory.CreateDirectory("RobotPlayValidation");string result=SessionState.GetString(Report,"");File.WriteAllText("RobotPlayValidation/play-checks.txt",result);
                    SessionState.SetBool(Active,false);Debug.Log(result);EditorApplication.Exit(result.Contains("FAIL:")?1:0);return;
                }
                if(EditorApplication.timeSinceStartup-started>240)throw new Exception("Validation timeout");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<15)return;
                EditorApplication.QueuePlayerLoopUpdate();
                float elapsed=Time.time-at;
                switch(stage)
                {
                    case 0:
                        session=UnityEngine.Object.FindFirstObjectByType<RobotPlaySession>();Assert(session&&session.target&&session.contact.arms.Length==2,"serialized scene references loaded");
                        var rig=UnityEngine.Object.FindFirstObjectByType<RobotRigVisual>();Assert(rig&&rig.arms[0].hand&&rig.arms[1].hand,"uploaded left and right hand bones bound");
                        original=InputSystem.settings;temporary=UnityEngine.Object.Instantiate(original);temporary.hideFlags=HideFlags.DontSave;
                        temporary.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;temporary.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=temporary;
                        Application.runInBackground=true;keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();Next();break;
                    case 1:if(elapsed>.4f){Keys(Key.Digit1);Next();}break;
                    case 2:if(elapsed>.06f){Keys();Next();}break;
                    case 3:if(elapsed>.94f){Assert(session.State.hits==1,"keyboard left jab reaches existing contact solver and damages once");Keys(Key.Digit2);Next();}break;
                    case 4:if(elapsed>.06f){Keys();Next();}break;
                    case 5:if(elapsed>.94f){Assert(session.State.hits==2&&session.State.combo==2,"opposite jab advances cooperative state");Keys(Key.Digit3);Next();}break;
                    case 6:if(elapsed>.06f){Keys();Next();}break;
                    case 7:if(elapsed>.29f){Keys(Key.Q);Next();}break;
                    case 8:if(elapsed>.06f){Keys();Next();}break;
                    case 9:if(elapsed>.6f){Assert(session.State.hits==3&&session.State.bossHealth==104,"hook plus in-flight button produces boosted combo");Capture("combo.png");Keys(Key.R);Next();}break;
                    case 10:if(elapsed>.06f){Keys();Next();}break;
                    case 11:if(elapsed>.4f){Keys(Key.Digit3);Next();}break;
                    case 12:if(elapsed>.06f){Keys(Key.Q);Next();}break;
                    case 13:if(elapsed>.8f){Keys();Assert(session.State.bossHealth==160,"standalone hook and early boost cause no damage");session.trainingWindow=false;session.ResetSimulation();Next();}break;
                    case 14:if(elapsed>.3f){Keys(Key.Z,Key.C);Next();}break;
                    case 15:if(elapsed>2.1f){Assert(session.State.robotHealth==100&&session.State.phase==1,"raised arms block boss and open recovery window");Capture("guard.png");Keys();session.ResetSimulation();Next();}break;
                    case 16:if(elapsed>2.25f){Assert(session.State.robotHealth==88,"unguarded boss attack damages robot");session.ResetSimulation();Keys(Key.S);Next();}break;
                    case 17:if(elapsed>1.15f){Keys();Next();}break;
                    case 18:if(elapsed>1.15f){Assert(session.State.robotHealth==100&&session.robotRoot.position.z<-.55f,"retreat moves shared robot out of boss reach");Complete();}break;
                }
            }
            catch(Exception ex){checks.Add("FAIL: "+ex);Complete();}
        }
        static void Capture(string name)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            Directory.CreateDirectory("RobotPlayValidation");var camera=Camera.main;var rt=new RenderTexture(1280,720,24);var previous=camera.targetTexture;var active=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("RobotPlayValidation/"+name,image.EncodeToPNG());camera.targetTexture=previous;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);
        }
        static void Complete()
        {
            if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}
            if(original){InputSystem.settings=original;UnityEngine.Object.DestroyImmediate(temporary);}
            SessionState.SetString(Report,string.Join("\n",checks));SessionState.SetBool(Finish,true);EditorApplication.isPlaying=false;
        }
    }
}
