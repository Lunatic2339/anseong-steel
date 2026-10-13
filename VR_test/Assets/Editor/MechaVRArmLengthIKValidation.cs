using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRArmLengthIKValidation
{
    static MechaVRArmLengthIKValidation(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(!File.Exists("Library/Arms20IK.request")||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete("Library/Arms20IK.request"); Validate();
    }
    public static void Validate()
    {
        var preview=EditorSceneManager.NewPreviewScene(); RigBuilder builder=null;
        try
        {
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/MainVRScene_BodyTracking.unity");
            var source=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MechaVRBodyFollower>()).Single();
            var clone=UnityEngine.Object.Instantiate(source.gameObject);SceneManager.MoveGameObjectToScene(clone,preview);
            builder=clone.GetComponentInChildren<RigBuilder>();
            var animator=builder.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var arms=clone.GetComponentsInChildren<TwoBoneIKConstraint>();
            var lengths=arms.Select(a=>new Vector2(Vector3.Distance(a.data.root.position,a.data.mid.position),Vector3.Distance(a.data.mid.position,a.data.tip.position))).ToArray();
            if(!builder.Build())throw new Exception("Rig graph did not build.");
            builder.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            for(int i=0;i<arms.Length;i++)
            {
                var d=arms[i].data;
                d.target.position=d.root.position+(d.tip.position-d.root.position).normalized*(lengths[i].x+lengths[i].y)*.75f+Vector3.forward*.03f;
            }
            for(int frame=0;frame<4;frame++)builder.Evaluate(1f/60f);
            float error=0;
            for(int i=0;i<arms.Length;i++)
            {
                var d=arms[i].data;
                if(Mathf.Abs(Vector3.Distance(d.root.position,d.mid.position)-lengths[i].x)>.0001f||Mathf.Abs(Vector3.Distance(d.mid.position,d.tip.position)-lengths[i].y)>.0001f)throw new Exception("Animator reset extended bone lengths.");
                error=Mathf.Max(error,Vector3.Distance(d.tip.position,d.target.position));
            }
            if(error>.005f)throw new Exception("IK target error="+error);
            File.WriteAllText("Library/Arms20IK.txt","PASS\nRig graph evaluates; extended lengths preserved; max hand target error="+error+"m\n");
        }
        catch(Exception e){File.WriteAllText("Library/Arms20IK.txt","FAIL\n"+e);Debug.LogException(e);}
        finally{if(builder)builder.Clear();EditorSceneManager.ClosePreviewScene(preview);}
    }
}