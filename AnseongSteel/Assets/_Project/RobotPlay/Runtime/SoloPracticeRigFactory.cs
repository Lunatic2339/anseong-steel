using System;
using System.Collections.Generic;
using AnseongSteel.PlayerMotion;
using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    static class SoloPracticeRigFactory
    {
        [Serializable]sealed class Profile
        {public string head;public ArmData left,right;public string[] visible,hidden;}
        [Serializable]sealed class ArmData
        {public string upper,forearm,hand;public BoxData[] volumes;public string[] fingers;public Quaternion[] rotations;public Vector3 palmForward,palmInward;}
        [Serializable]sealed class BoxData
        {public string role,bone;public Vector3 center,size;public Quaternion rotation;}
        // Clone only the confirmed scene visual. Inactive staging prevents NPC
        // initialization; caller activates next frame, after deferred removals.
        public static SoloPracticePlayerRig Create(RobotPlaySession session,SoloPracticeSettings settings)
        {
            if(!settings.rigProfile)throw new InvalidOperationException("Missing practice rig profile.");
            var encounter=session.GetComponent<IntegratedBossEncounter>();
            if(!encounter||!encounter.driver||!encounter.driver.animator)throw new InvalidOperationException("Missing confirmed scene boss visual.");
            var data=JsonUtility.FromJson<Profile>(settings.rigProfile.text);
            var source=encounter.driver.animator.transform;
            if(data==null||!source.Find(data.head))throw new InvalidOperationException("Boss visual does not match the provided rig profile.");
            var stage=new GameObject("Practice clone staging");stage.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage,session.gameObject.scene);
            GameObject clone=null;
            try
            {
                clone=UnityEngine.Object.Instantiate(source.gameObject,stage.transform,false);clone.SetActive(false);
                foreach(var b in clone.GetComponentsInChildren<MonoBehaviour>(true)){b.enabled=false;UnityEngine.Object.Destroy(b);}
                foreach(var a in clone.GetComponentsInChildren<Animator>(true)){a.enabled=false;UnityEngine.Object.Destroy(a);}
                foreach(var c in clone.GetComponentsInChildren<Collider>(true)){c.enabled=false;UnityEngine.Object.Destroy(c);}
                foreach(var c in clone.GetComponentsInChildren<Camera>(true))c.enabled=false;
                foreach(var a in clone.GetComponentsInChildren<AudioSource>(true)){a.enabled=false;UnityEngine.Object.Destroy(a);}
                foreach(var p in clone.GetComponentsInChildren<ParticleSystem>(true)){p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=p.main;main.playOnAwake=false;UnityEngine.Object.Destroy(p);}
                clone.transform.SetParent(session.robotRoot,false);clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.identity;
                var scale=source.lossyScale;var rootScale=session.robotRoot.lossyScale;
                clone.transform.localScale=new Vector3(scale.x/rootScale.x,scale.y/rootScale.y,scale.z/rootScale.z);
                var rig=clone.AddComponent<SoloPracticePlayerRig>();rig.left=Bind(clone.transform,data.left,session.robotRoot,settings,-1);rig.right=Bind(clone.transform,data.right,session.robotRoot,settings,1);
                rig.visibleArms=Renderers(clone.transform,data.visible);rig.localHiddenBody=Renderers(clone.transform,data.hidden);
                var known=new HashSet<Renderer>(rig.visibleArms);foreach(var r in rig.localHiddenBody)known.Add(r);
                foreach(var r in clone.GetComponentsInChildren<Renderer>(true))if(!known.Contains(r))r.enabled=false;
                var head=clone.transform.Find(data.head);var view=new GameObject("Practice view anchor").transform;view.SetParent(clone.transform,false);
                view.position=head.position+session.robotRoot.TransformVector(settings.viewOffset);rig.viewAnchor=view;
                return rig;
            }
            catch{if(clone)UnityEngine.Object.Destroy(clone);throw;}
            finally{UnityEngine.Object.Destroy(stage);}
        }
        static SoloPracticePlayerRig.ArmBinding Bind(Transform visual,ArmData data,Transform frame,SoloPracticeSettings settings,int side)
        {
            if(data==null||data.volumes==null||data.volumes.Length!=3||data.fingers==null||data.rotations==null||data.fingers.Length!=data.rotations.Length)throw new InvalidOperationException("Incomplete arm/finger mapping.");
            Transform Bone(string path){var t=visual.Find(path);if(!t)throw new InvalidOperationException("Missing mapped bone: "+path);return t;}
            var upper=Bone(data.upper);var lower=Bone(data.forearm);var hand=Bone(data.hand);
            var fist=hand.gameObject.AddComponent<DefaultFistPose>();fist.joints=new Transform[data.fingers.Length];
            for(int i=0;i<data.fingers.Length;i++)fist.joints[i]=Bone(data.fingers[i]);
            fist.closedRotations=data.rotations;fist.restRotations=new Quaternion[fist.joints.Length];for(int i=0;i<fist.joints.Length;i++)fist.restRotations[i]=fist.joints[i].localRotation;
            fist.palmForwardLocal=data.palmForward.normalized;fist.palmInwardLocal=data.palmInward.normalized;fist.Apply();
            var guard=new GameObject(side<0?"Practice left guard":"Practice right guard").transform;guard.SetParent(visual,false);
            float length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position);
            // Explicit initial tuning, scaled by this rig's measured segment lengths.
            var direction=frame.TransformDirection(new Vector3(-side*.12f,-.3f,.8f).normalized);
            guard.position=upper.position+direction*(length*settings.guardReachFraction);
            var volumes=new BoxCollider[3];
            for(int i=0;i<3;i++)
            {
                var d=data.volumes[i];if(d.size.x<=0||d.size.y<=0||d.size.z<=0)throw new InvalidOperationException("Invalid mapped query box.");
                var parent=new GameObject("Practice "+d.role).transform;parent.SetParent(Bone(d.bone),false);parent.localRotation=d.rotation;
                var query=new GameObject("Query");query.transform.SetParent(parent,false);var box=query.AddComponent<BoxCollider>();box.center=d.center;box.size=d.size;box.isTrigger=true;query.layer=2;volumes[i]=box;
            }
            return new SoloPracticePlayerRig.ArmBinding{upper=upper,forearm=lower,hand=hand,guardAnchor=guard,volumes=volumes};
        }
        static Renderer[] Renderers(Transform visual,string[] paths)
        {
            if(paths==null)throw new InvalidOperationException("Missing visibility mapping.");var result=new Renderer[paths.Length];
            for(int i=0;i<paths.Length;i++){var t=visual.Find(paths[i]);var r=t?t.GetComponent<Renderer>():null;if(!r)throw new InvalidOperationException("Missing mapped renderer: "+paths[i]);result[i]=r;}return result;
        }
    }
}
