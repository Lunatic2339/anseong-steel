using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace AnseongSteel.RobotPlay.Editor
{
    public static class RobotPlayBuilder
    {
        public const string Root="Assets/_Project/RobotPlay";
        public const string ScenePath=Root+"/Scenes/RobotPlay.unity";
        const string RigPath="Assets/_Project/02_Art/01_Models/mecha-red-points-mixamo-v025.fbx";
        [MenuItem("Anseong Steel/Robot Play/Build prototype scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Root+"/Scenes");Directory.CreateDirectory(Root+"/Materials");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var manager=new GameObject("Prototype NGO LAN (uses installed Netcode + UTP)");
            var transport=manager.AddComponent<UnityTransport>();var net=manager.AddComponent<NetworkManager>();
            net.NetworkConfig=new NetworkConfig {NetworkTransport=transport,EnableSceneManagement=true};
            var session=new GameObject("Robot Combat Authority").AddComponent<RobotPlaySession>();
            session.gameObject.AddComponent<NetworkObject>();
            session.robotRoot=new GameObject("Shared Robot Body").transform;
            var contact=session.robotRoot.gameObject.AddComponent<MechaVRBossContact>();session.contact=contact;
            contact.maxSubsteps=128;contact.minimumHitSpeed=.05f;contact.enabled=false;
            contact.arms=new MechaVRBossContact.Arm[2];
            var steel=Mat("RobotSteel",new Color(.16f,.25f,.3f));
            var cyan=Mat("Cyan",new Color(.12f,.7f,1));var amber=Mat("Amber",new Color(1,.45f,.12f));
            var floorMat=Mat("Floor",new Color(.055f,.075f,.11f));
            var feedback=session.gameObject.AddComponent<RobotFeedbackView>();feedback.session=session;feedback.boosters=new Renderer[2];
            for(int side=0;side<2;side++)
            {
                var upper=new GameObject(side==0?"L Upper":"R Upper").transform;upper.SetParent(session.robotRoot,false);upper.localPosition=new Vector3(side==0?-.8f:.8f,1.6f,0);
                var lower=new GameObject("Forearm").transform;lower.SetParent(upper,false);lower.localPosition=Vector3.forward*.95f;
                var hand=new GameObject("Fist").transform;hand.SetParent(lower,false);hand.localPosition=Vector3.forward*.95f;
                var volumes=new[]{Volume(upper,new Vector3(0,0,.46f),new Vector3(.18f,.18f,.8f)),Volume(lower,new Vector3(0,0,.46f),new Vector3(.22f,.22f,.8f)),Volume(hand,Vector3.zero,new Vector3(.32f,.32f,.32f))};
                contact.arms[side]=new MechaVRBossContact.Arm {label=side==0?"L":"R",upperArm=upper,forearm=lower,hand=hand,volumes=volumes};
                var booster=Cube("Elbow booster feedback",lower,new Vector3(0,0,-.12f),new Vector3(.19f,.19f,.35f),amber);feedback.boosters[side]=booster.GetComponent<Renderer>();
            }
            var enemy=new GameObject("Replaceable Enemy Target");enemy.transform.position=new Vector3(0,0,1.85f);
            var collider=enemy.AddComponent<BoxCollider>();collider.center=new Vector3(0,1.45f,0);collider.size=new Vector3(1.8f,1.4f,.4f);
            session.target=enemy.AddComponent<MechaVRBossTarget>();session.target.logHits=false;contact.boss=session.target;
            // The uploaded rig is the temporary player body; a second instance is a static target.
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if(!asset)throw new Exception("Uploaded rig FBX missing: "+RigPath);
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(asset);visual.name="Uploaded rig - playable arms";visual.transform.SetParent(session.robotRoot,false);visual.transform.localScale=Vector3.one*6;
            ConvertMaterials(visual);
            foreach(var animator in visual.GetComponentsInChildren<Animator>())animator.enabled=false;
            Transform Bone(string name) => visual.GetComponentsInChildren<Transform>().First(t=>t.name.EndsWith(name,StringComparison.Ordinal));
            if(Bone("LeftArm").position.x>Bone("RightArm").position.x)visual.transform.localRotation=Quaternion.Euler(0,180,0);
            var rig=visual.AddComponent<RobotRigVisual>();rig.session=session;
            for(int side=0;side<2;side++)
            {
                string prefix=side==0?"Left":"Right";var upper=Bone(prefix+"Arm");var lower=Bone(prefix+"ForeArm");var hand=Bone(prefix+"Hand");
                rig.arms[side]=new RobotRigVisual.Binding {upper=upper,lower=lower,hand=hand,upperAxis=upper.InverseTransformDirection(lower.position-upper.position),lowerAxis=lower.InverseTransformDirection(hand.position-lower.position),handOffset=Quaternion.FromToRotation((hand.position-lower.position).normalized,Vector3.forward)*hand.rotation};
            }
            // Hide body surfaces around the cockpit; preserve the original asset and all skin weights.
            foreach(var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var bones=renderer.bones;
                bool ArmBone(int index) => index>=0&&index<bones.Length&&bones[index]&&(bones[index].IsChildOf(rig.arms[0].upper)||bones[index].IsChildOf(rig.arms[1].upper));
                renderer.enabled=renderer.sharedMesh.boneWeights.Any(w=>(w.weight0>.2f&&ArmBone(w.boneIndex0))||(w.weight1>.2f&&ArmBone(w.boneIndex1))||(w.weight2>.2f&&ArmBone(w.boneIndex2))||(w.weight3>.2f&&ArmBone(w.boneIndex3)));
                renderer.updateWhenOffscreen=true;
            }
            var enemyVisual=(GameObject)PrefabUtility.InstantiatePrefab(asset);enemyVisual.name="Uploaded rig target visual (replace here)";enemyVisual.transform.SetParent(enemy.transform,false);enemyVisual.transform.localRotation=Quaternion.Euler(0,180,0);
            ConvertMaterials(enemyVisual);
            foreach(var animator in enemyVisual.GetComponentsInChildren<Animator>())animator.enabled=false;
            var bounds=new Bounds();bool first=true;foreach(var r in enemyVisual.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
            enemyVisual.transform.localScale=Vector3.one*(3.2f/bounds.size.y);
            enemyVisual.transform.localPosition=new Vector3(0,-bounds.min.y*enemyVisual.transform.localScale.x,.2f);
            session.targetMarker=Cube("Vulnerability indicator",enemy.transform,new Vector3(0,3.5f,0),new Vector3(1.8f,.1f,.1f),cyan).GetComponent<Renderer>();
            Cube("Arena floor",null,new Vector3(0,-.12f,0),new Vector3(12,.12f,12),floorMat);
            // Light cockpit frame keeps both first-person stations visible without blocking the view.
            Cube("Cockpit lower console",session.robotRoot,new Vector3(0,.65f,-1.3f),new Vector3(2,.15f,.6f),steel);
            Cube("Cockpit left frame",session.robotRoot,new Vector3(-1.15f,1.35f,-.4f),new Vector3(.08f,1.7f,.08f),steel);
            Cube("Cockpit right frame",session.robotRoot,new Vector3(1.15f,1.35f,-.4f),new Vector3(.08f,1.7f,.08f),steel);
            var seat=new GameObject("Local Pilot Station").transform;seat.SetParent(session.robotRoot,false);seat.localPosition=new Vector3(-.48f,0,-1.8f);
            var camera=new GameObject("Cockpit Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.SetParent(seat,false);camera.transform.localPosition=new Vector3(0,1.65f,0);camera.nearClipPlane=.03f;camera.farClipPlane=100;camera.fieldOfView=75;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.085f);camera.gameObject.AddComponent<AudioListener>();
            var input=session.gameObject.AddComponent<RobotPilotInput>();input.session=session;input.seat=seat;input.cockpitCamera=camera;
            var text=new GameObject("Cockpit state / event display").AddComponent<TextMesh>();text.transform.SetParent(session.robotRoot,false);text.transform.localPosition=new Vector3(-.9f,1.25f,-.5f);text.transform.localScale=Vector3.one*.10f;text.fontSize=38;text.characterSize=.12f;text.anchor=TextAnchor.UpperLeft;text.color=Color.cyan;session.statusText=text;
            var light=new GameObject("Arena key light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-25,0);
            RenderSettings.ambientLight=new Color(.4f,.45f,.55f);
            session.Pose(0,Vector3.zero);session.Pose(1,Vector3.zero);
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_SCENE_BUILT "+ScenePath);
        }
        static BoxCollider Volume(Transform parent,Vector3 center,Vector3 size)
        {var g=new GameObject("Boss Contact Volume");g.transform.SetParent(parent,false);g.layer=2;var c=g.AddComponent<BoxCollider>();c.center=center;c.size=size;c.isTrigger=true;return c;}
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=size;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=material;return g;}
        static Material Mat(string name,Color color)
        {string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}m.color=color;return m;}
        static void ConvertMaterials(GameObject model)
        {
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Mat("Rig_"+(m?m.name.Replace('/','_'):"Default"),m&&m.HasProperty("_Color")?m.color:Color.gray)).ToArray();
        }
    }
}
