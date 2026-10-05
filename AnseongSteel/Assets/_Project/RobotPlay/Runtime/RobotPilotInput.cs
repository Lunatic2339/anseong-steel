using UnityEngine;
using UnityEngine.InputSystem;

namespace AnseongSteel.RobotPlay
{
    public enum PrototypeInput { DesktopBoth, XRSinglePilot }
    [DefaultExecutionOrder(-100)]
    public sealed class RobotPilotInput : MonoBehaviour
    {
        public RobotPlaySession session;
        public PrototypeInput source=PrototypeInput.DesktopBoth;
        public PilotSide offlineSide;
        public Camera cockpitCamera;
        public Transform seat;
        public float motionScale=2.6f;
        [Header("Editable XR bindings. Trigger = boost edge; opposite stick = provisional movement")]
        public InputAction leftPosition=new InputAction("Left pose",InputActionType.Value,"<XRController>{LeftHand}/devicePosition",expectedControlType:"Vector3");
        public InputAction rightPosition=new InputAction("Right pose",InputActionType.Value,"<XRController>{RightHand}/devicePosition",expectedControlType:"Vector3");
        public InputAction leftTracked=new InputAction("Left tracked",InputActionType.Button,"<XRController>{LeftHand}/isTracked");
        public InputAction rightTracked=new InputAction("Right tracked",InputActionType.Button,"<XRController>{RightHand}/isTracked");
        public InputAction leftBoost=new InputAction("Left boost",InputActionType.Button,"<XRController>{LeftHand}/trigger");
        public InputAction rightBoost=new InputAction("Right boost",InputActionType.Button,"<XRController>{RightHand}/trigger");
        public InputAction leftStick=new InputAction("Left stick",InputActionType.Value,"<XRController>{LeftHand}/thumbstick",expectedControlType:"Vector2");
        public InputAction rightStick=new InputAction("Right stick",InputActionType.Value,"<XRController>{RightHand}/thumbstick",expectedControlType:"Vector2");
        public InputAction headPosition=new InputAction("Head position",InputActionType.Value,"<XRHMD>/centerEyePosition",expectedControlType:"Vector3");
        public InputAction headRotation=new InputAction("Head rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion");
        public InputAction recenter=new InputAction("Recenter",InputActionType.Button,"<XRController>{LeftHand}/secondaryButton");
        public enum Startup { Offline, Host, Client }
        public Startup startup;
        readonly Vector3[] baseline=new Vector3[2];
        readonly bool[] calibrated=new bool[2],pendingBoost=new bool[2];
        readonly float[] animationStart={-100,-100};
        readonly bool[] hook=new bool[2];
        Vector3 headBaseline;
        Quaternion trackingToRobot=Quaternion.identity;
        bool headCalibrated;
        float nextSend;
        uint sequence;
        int lastSide=-1;
        InputAction[] Actions => new[]{leftPosition,rightPosition,leftTracked,rightTracked,leftBoost,rightBoost,leftStick,rightStick,headPosition,headRotation,recenter};
        void OnEnable(){foreach(var action in Actions)action.Enable();}
        void OnDisable(){foreach(var action in Actions)action.Disable();Recenter();}
        void Start(){if(startup!=Startup.Offline)session.StartNetwork(startup==Startup.Host);}
        public void Recenter(){calibrated[0]=calibrated[1]=headCalibrated=false;pendingBoost[0]=pendingBoost[1]=false;}
        void Update()
        {
            if(!session)return;
            int side=session.NetworkActive?session.LocalSide:(int)offlineSide;
            if(side!=lastSide){lastSide=side;Recenter();}
            if(seat)seat.localPosition=new Vector3(side==0?-.48f:.48f,0,-1.8f);
            if(source==PrototypeInput.XRSinglePilot)ReadXR(side);else ReadDesktop();
        }
        void ReadXR(int side)
        {
            if(recenter.WasPressedThisFrame())Recenter();
            var position=side==0?leftPosition:rightPosition;
            var tracked=side==0?leftTracked:rightTracked;
            var button=side==0?leftBoost:rightBoost;
            if(headPosition.activeControl!=null && headRotation.activeControl!=null)
            {
                Vector3 hp=headPosition.ReadValue<Vector3>();
                Quaternion rotation=headRotation.ReadValue<Quaternion>();
                if(!headCalibrated){headBaseline=hp;trackingToRobot=Quaternion.Inverse(Quaternion.Euler(0,rotation.eulerAngles.y,0));headCalibrated=true;}
                cockpitCamera.transform.localPosition=new Vector3(0,1.65f,0)+trackingToRobot*(hp-headBaseline);
                cockpitCamera.transform.localRotation=trackingToRobot*rotation;
            }
            if(!tracked.IsPressed()||position.activeControl==null)
            {calibrated[side]=false;pendingBoost[side]=false;return;}
            Vector3 p=position.ReadValue<Vector3>();
            if(!calibrated[side]){baseline[side]=p;calibrated[side]=true;return;}
            pendingBoost[side]|=button.WasPressedThisFrame();
            if(Time.time<nextSend)return;
            nextSend=Time.time+.02f;
            session.Submit(side,Vector3.ClampMagnitude(trackingToRobot*(p-baseline[side])*motionScale,2.45f),pendingBoost[side],(side==0?rightStick:leftStick).ReadValue<Vector2>(),++sequence);
            pendingBoost[side]=false;
        }
        void ReadDesktop()
        {
            var k=Keyboard.current;if(k==null)return;
            if(k.f8Key.wasPressedThisFrame)Recenter();
            if(k.digit1Key.wasPressedThisFrame)BeginStroke(0,false);
            if(k.digit2Key.wasPressedThisFrame)BeginStroke(1,false);
            if(k.digit3Key.wasPressedThisFrame)BeginStroke(0,true);
            if(k.digit4Key.wasPressedThisFrame)BeginStroke(1,true);
            pendingBoost[0]|=k.qKey.wasPressedThisFrame;pendingBoost[1]|=k.eKey.wasPressedThisFrame;
            if(k.rKey.wasPressedThisFrame&&session.Authority)session.ResetSimulation();
            if(Time.time<nextSend)return;nextSend=Time.time+.02f;
            Vector2 move=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            for(int i=0;i<2;i++)
            {
                if(session.NetworkActive&&i!=session.LocalSide)continue;
                Vector3 p=DesktopPose(i,Time.time-animationStart[i],hook[i]);
                if(Time.time-animationStart[i]>.8f && (i==0?k.zKey.isPressed:k.cKey.isPressed))p=new Vector3(0,.65f,0);
                session.Submit(i,p,pendingBoost[i],i==0?move:Vector2.zero,++sequence);pendingBoost[i]=false;
            }
        }
        public void BeginStroke(int side,bool isHook)
        {if(Time.time-animationStart[side]<.9f)return;animationStart[side]=Time.time;hook[side]=isHook;}
        // Deterministic pose source only. It goes through the same server motion/contact path as XR.
        public static Vector3 DesktopPose(int side,float t,bool isHook)
        {
            if(t<0||t>.75f)return Vector3.zero;
            if(!isHook)
            {
                float z=t<.22f?Mathf.Lerp(0,1.23f,t/.22f):Mathf.Lerp(1.23f,0,(t-.22f)/.45f);
                return new Vector3(0,.05f*z,z);
            }
            float sign=side==0?-1:1;
            // Wind-up travels diagonally (not a jab); inward sweep then crosses the target.
            Vector3 windup=new Vector3(sign*.85f,0,.8f),end=new Vector3(-sign*.58f,.03f,1.08f);
            if(t<.28f)return Vector3.Lerp(Vector3.zero,windup,t/.28f);
            if(t<.5f)return Vector3.Lerp(windup,end,(t-.28f)/.22f);
            return Vector3.Lerp(end,Vector3.zero,(t-.5f)/.25f);
        }
        void OnGUI()
        {
            if(source!=PrototypeInput.DesktopBoth)return;
            GUILayout.BeginArea(new Rect(12,12,390,310),GUI.skin.box);
            GUILayout.Label("ROBOT PLAY | PROTOTYPE / provisional controls");
            GUILayout.Label("1 / 2: L / R jab    3 / 4: L / R hook\nQ / E: boost DURING final hook\nZ / C hold: L / R guard    WASD: move\nR: reset   Recover hands between strokes (~1 sec)");
            GUILayout.Label($"Boss {session.State.bossHealth} | Robot {session.State.robotHealth} | Combo {session.State.combo} | Hits {session.State.hits}");
            GUILayout.Label(session.LastFeedback);
            if(!session.NetworkActive)
            {
                session.hostAddress=GUILayout.TextField(session.hostAddress);
                GUILayout.BeginHorizontal();if(GUILayout.Button("Host (left)"))session.StartNetwork(true);if(GUILayout.Button("Join (right)"))session.StartNetwork(false);GUILayout.EndHorizontal();
            }
            else if(GUILayout.Button("Disconnect"))session.StopNetwork();
            if(session.Authority)session.trainingWindow=GUILayout.Toggle(session.trainingWindow,"Always open target (off = timed boss attacks)");
            GUILayout.EndArea();
        }
    }
}
