using UnityEngine;
using UnityEngine.InputSystem;
using AnseongSteel.PlayerMotion;

namespace AnseongSteel.RobotPlay
{
    public enum PrototypeInput { DesktopBoth, XRSinglePilot, XRLocalBoth }
    [DefaultExecutionOrder(-100)]
    public sealed class RobotPilotInput : MonoBehaviour
    {
        public RobotPlaySession session;
        public PrototypeInput source=PrototypeInput.DesktopBoth;
        public PilotSide offlineSide;
        public Camera cockpitCamera;
        public Transform seat;
        public float motionScale=2.6f;
        [Header("Local personal reach: control-point measurements, not anatomical arm length")]
        public bool usePersonalCalibration=true;
        public bool compensateHeadTranslation=true;
        public Vector3 gripToControlPoint=Vector3.zero;
        public Vector3 shoulderProbeOffset=Vector3.zero;
        public Vector3 leftShoulderEstimate=new Vector3(-.18f,-.2f,0);
        public Vector3 rightShoulderEstimate=new Vector3(.18f,-.2f,0);
        public InputAction leftReach=new InputAction("Save left reach",InputActionType.Button,"<XRController>{LeftHand}/primaryButton");
        public InputAction rightReach=new InputAction("Save right reach",InputActionType.Button,"<XRController>{RightHand}/primaryButton");
        public InputAction leftShoulder=new InputAction("Left shoulder reference",InputActionType.Button,"<XRController>{LeftHand}/thumbstickClicked");
        public InputAction rightShoulder=new InputAction("Right shoulder reference",InputActionType.Button,"<XRController>{RightHand}/thumbstickClicked");
        [Header("Editable XR bindings. Trigger = boost edge; opposite stick = provisional movement")]
        public InputAction leftPosition=new InputAction("Left pose",InputActionType.Value,"<XRController>{LeftHand}/devicePosition",expectedControlType:"Vector3");
        public InputAction rightPosition=new InputAction("Right pose",InputActionType.Value,"<XRController>{RightHand}/devicePosition",expectedControlType:"Vector3");
        public InputAction leftRotation=new InputAction("Left rotation",InputActionType.Value,"<XRController>{LeftHand}/deviceRotation",expectedControlType:"Quaternion");
        public InputAction rightRotation=new InputAction("Right rotation",InputActionType.Value,"<XRController>{RightHand}/deviceRotation",expectedControlType:"Quaternion");
        public InputAction leftTracked=new InputAction("Left tracked",InputActionType.Button,"<XRController>{LeftHand}/isTracked");
        public InputAction rightTracked=new InputAction("Right tracked",InputActionType.Button,"<XRController>{RightHand}/isTracked");
        public InputAction leftTrackingState=new InputAction("Left tracking flags",InputActionType.Value,"<XRController>{LeftHand}/trackingState",expectedControlType:"Integer");
        public InputAction rightTrackingState=new InputAction("Right tracking flags",InputActionType.Value,"<XRController>{RightHand}/trackingState",expectedControlType:"Integer");
        public static bool ValidRotation(Quaternion q)=>AnseongSteel.PlayerMotion.PunchTrajectoryRecognizer.Finite(q)&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.25f&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w<4;
        public InputAction leftAttack=new InputAction("Left attack hold",InputActionType.Button,"<XRController>{LeftHand}/gripPressed");
        public InputAction rightAttack=new InputAction("Right attack hold",InputActionType.Button,"<XRController>{RightHand}/gripPressed");
        public InputAction leftBoost=new InputAction("Left boost",InputActionType.Button,"<XRController>{LeftHand}/trigger");
        public InputAction rightBoost=new InputAction("Right boost",InputActionType.Button,"<XRController>{RightHand}/trigger");
        public InputAction leftStick=new InputAction("Left stick",InputActionType.Value,"<XRController>{LeftHand}/thumbstick",expectedControlType:"Vector2");
        public InputAction rightStick=new InputAction("Right stick",InputActionType.Value,"<XRController>{RightHand}/thumbstick",expectedControlType:"Vector2");
        public InputAction headPosition=new InputAction("Head position",InputActionType.Value,"<XRHMD>/centerEyePosition",expectedControlType:"Vector3");
        public InputAction headRotation=new InputAction("Head rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion");
        public InputAction headTrackingState=new InputAction("Head tracking flags",InputActionType.Value,"<XRHMD>/trackingState",expectedControlType:"Integer");
        public InputAction recenter=new InputAction("Recenter",InputActionType.Button,"<XRController>{LeftHand}/secondaryButton");
        public enum Startup { Offline, Host, Client }
        public Startup startup;
        readonly Vector3[] baseline=new Vector3[2];
        readonly Quaternion[] rotationBaseline={Quaternion.identity,Quaternion.identity};
        readonly bool[] calibrated=new bool[2],pendingBoost=new bool[2],releaseAfterLoss=new bool[2];
        readonly float[] animationStart={-100,-100};
        readonly bool[] hook=new bool[2];
        Vector3 headBaseline;
        Quaternion trackingToRobot=Quaternion.identity;
        bool headCalibrated;
        readonly ArmReachCalibration[] reachProfiles={new ArmReachCalibration(),new ArmReachCalibration()};
        readonly Vector3[] bodyPoints=new Vector3[2],shoulderPoints=new Vector3[2];
        readonly Quaternion[] bodyRotations={Quaternion.identity,Quaternion.identity};
        readonly bool[] shoulderCaptured=new bool[2],guardRequested=new bool[2];
        readonly float[] poseAt={-100,-100};
        Vector3 currentHead;
        public const float PersonalDeltaLimit=2.8f; // At most two valid shoulder radii, without the legacy transport cap.
        public string LastCalibrationMessage {get;private set;}="";
        public float LastCalibrationMessageAt {get;private set;}=-100;
        public bool HeadTracked {get;private set;}
        readonly bool[] handsTracked=new bool[2],reachPressed=new bool[2];
        public bool HandTracked(int side)=>side>=0&&side<2&&handsTracked[side];
        public bool NeedsBothCalibration=>OwnsBoth;
        public bool NeedsGripRelease(int side)=>releaseAfterLoss[side];
        public bool TryGetCalibratedControlDelta(int side,out Vector3 delta)
        {
            delta=Vector3.zero;
            if(side<0||side>1||!RequiresPersonalCalibration||!HeadTracked||!handsTracked[side]||!reachProfiles[side].IsComplete||Time.time-poseAt[side]>.1f)return false;
            delta=bodyPoints[side]-reachProfiles[side].Guard;return PunchTrajectoryRecognizer.Finite(delta);
        }
        void CalibrationFeedback(string value){LastCalibrationMessage=value;LastCalibrationMessageAt=Time.unscaledTime;}
        string HandName(int side)=>side==0?"왼팔":"오른팔";
        static string KoreanReason(string reason)
        {
            if(reason=="Capture your comfortable guard first.")return "먼저 편한 가드에서 Y를 눌러 주세요.";
            if(reason=="Extend comfortably farther than guard; check the shoulder reference if necessary.")return "가드보다 조금 더 뻗은 위치가 필요합니다. 무리하게 뻗지 말고 가드와 어깨 기준을 확인해 주세요.";
            if(reason=="Check the shoulder estimate and hold a comfortable bent-arm guard.")return "팔을 편하게 굽힌 가드와 어깨 기준을 확인해 주세요.";
            return "머리와 손의 추적을 확인한 뒤 다시 눌러 주세요.";
        }
        public bool RequiresPersonalCalibration=>usePersonalCalibration&&source!=PrototypeInput.DesktopBoth&&session&&!session.NetworkActive;
        bool OwnsBoth=>source==PrototypeInput.XRLocalBoth&&!session.NetworkActive;
        public ArmReachCalibration ReachProfile(int side)=>reachProfiles[side];
        public bool PersonalInputArmed=>!RequiresPersonalCalibration||(OwnsBoth
            ?reachProfiles[0].IsComplete&&reachProfiles[1].IsComplete&&!releaseAfterLoss[0]&&!releaseAfterLoss[1]
            :reachProfiles[(int)offlineSide].IsComplete&&!releaseAfterLoss[(int)offlineSide]);
        public string CalibrationStatus
        {
            get
            {
                if(!RequiresPersonalCalibration)return session&&session.NetworkActive?"LAN: legacy pose; personal reach/rotation sync not supported":"Legacy input";
                if(!headCalibrated)return "WAIT FOR HEAD/CONTROLLER TRACKING / FACE FORWARD TO CALIBRATE";
                int first=OwnsBoth?0:(int)offlineSide,last=OwnsBoth?1:first;
                for(int side=first;side<=last;side++)if(!reachProfiles[side].HasGuard)return "HOLD COMFORTABLE GUARD / Y: SAVE GUARD";
                for(int side=first;side<=last;side++)if(!reachProfiles[side].IsComplete)return side==0?"EXTEND LEFT COMFORTABLY / X: SAVE REACH":"EXTEND RIGHT COMFORTABLY / A: SAVE REACH";
                return PersonalInputArmed?"REACH READY / SHOULDER REFERENCE IS APPROXIMATE":"REACH SAVED / RELEASE GRIP AND RETURN TO GUARD";
            }
        }
        float nextSend;
        uint sequence;
        int lastSide=-1;uint connectionEpoch;
        Vector3 cameraOrigin;
        Vector3 rightSeatOffset=Vector3.right*.96f;public bool PreviewRight { get; private set; }
        public Vector3 SeatOffset => (session.NetworkActive?session.LocalSide==1:PreviewRight)?rightSeatOffset:Vector3.zero;
        public void SetCockpitOrigin(Vector3 left,Vector3 rightOffset,bool previewRight){cameraOrigin=left;rightSeatOffset=rightOffset;PreviewRight=previewRight;cockpitCamera.transform.localPosition=cameraOrigin+SeatOffset;}
        public Vector3 CameraOrigin => cameraOrigin;
        public Vector3 RightSeatOffset => rightSeatOffset;
        InputAction[] Actions => new[]{leftPosition,rightPosition,leftRotation,rightRotation,leftTracked,rightTracked,leftTrackingState,rightTrackingState,leftAttack,rightAttack,leftBoost,rightBoost,leftStick,rightStick,headPosition,headRotation,headTrackingState,recenter,leftReach,rightReach,leftShoulder,rightShoulder};
        void OnEnable(){foreach(var action in Actions)action.Enable();}
        void OnDisable(){foreach(var action in Actions)action.Disable();Recenter();}
        void Start(){cameraOrigin=cockpitCamera.transform.localPosition;if(startup!=Startup.Offline)session.StartNetwork(startup==Startup.Host);}
        public void Recenter()
        {
            calibrated[0]=calibrated[1]=headCalibrated=false;
            for(int side=0;side<2;side++)
            {
                pendingBoost[side]=guardRequested[side]=shoulderCaptured[side]=false;
                releaseAfterLoss[side]=true;poseAt[side]=-100;reachProfiles[side].Reset();
                if(session){session.ClearPersonalReach(side);session.LoseTracking(side);}
            }
            LastCalibrationMessage="";LastCalibrationMessageAt=-100;
        }
        void Update()
        {
            if(!session)return;
            int side=session.NetworkActive?session.LocalSide:(int)offlineSide;
            if(side!=lastSide||connectionEpoch!=session.ConnectionEpoch){lastSide=side;connectionEpoch=session.ConnectionEpoch;sequence=0;Recenter();cockpitCamera.transform.localPosition=cameraOrigin+SeatOffset;}
            if(seat&&!session.PracticeActive)seat.localPosition=new Vector3(side==0?-.48f:.48f,0,-1.8f);
            if(source!=PrototypeInput.DesktopBoth&&Keyboard.current!=null&&Keyboard.current.f8Key.wasPressedThisFrame)Recenter();
            HeadTracked=ArmMotionInput.ReadTrackedPose(headPosition,headRotation,headTrackingState,out _,out var headQ)&&ValidRotation(headQ);
            for(int i=0;i<2;i++){handsTracked[i]=ReadController(i,out _,out _);reachPressed[i]=(i==0?leftReach:rightReach).WasPressedThisFrame();guardRequested[i]=false;}
            if(recenter.WasPressedThisFrame())
            {
                if(RequiresPersonalCalibration)
                {
                    int first=OwnsBoth?0:side,last=OwnsBoth?1:side;
                    bool available=HeadTracked;
                    for(int i=first;i<=last;i++)available&=handsTracked[i];
                    if(!available)CalibrationFeedback("가드 저장 실패: 머리와 필요한 손의 추적을 확인하고 Y를 다시 눌러 주세요.");
                    else for(int i=first;i<=last;i++)guardRequested[i]=true;
                }
                else Recenter();
            }
            if (source == PrototypeInput.XRLocalBoth)
            {
                // Debug input owns both arms only while offline. LAN roles remain unchanged.
                if(session.NetworkActive)ReadXR(side);else {ReadXR(0);ReadXR(1);}
                if(Time.time>=nextSend)nextSend = Time.time + .02f;
            }
            else if(source==PrototypeInput.XRSinglePilot)ReadXR(side);else ReadDesktop();
            // A guard request belongs only to the frame of Y, never to a later recovered pose.
            guardRequested[0]=guardRequested[1]=false;
        }
        void ReadXR(int side)
        {
            var button=side==0?leftBoost:rightBoost;
            if(!ArmMotionInput.ReadTrackedPose(headPosition,headRotation,headTrackingState,out var hp,out var hq)||!ValidRotation(hq))
            {
                if(headCalibrated){Recenter();CalibrationFeedback("머리 추적이 끊겨 보정을 초기화했습니다. 추적 복구 후 다시 보정해 주세요.");}LoseHandTracking(side);
                if(RequiresPersonalCalibration&&recenter.WasPressedThisFrame())CalibrationFeedback("가드 저장 실패: 머리 추적을 확인하고 Y를 다시 눌러 주세요.");
                if(RequiresPersonalCalibration&&reachPressed[side])CalibrationFeedback(HandName(side)+" 저장 실패: 머리 추적을 확인하고 다시 눌러 주세요.");
                return;
            }
            currentHead=hp;
            hq=hq.normalized;
            if(!headCalibrated){headBaseline=hp;trackingToRobot=Quaternion.Inverse(Quaternion.Euler(0,hq.eulerAngles.y,0));headCalibrated=true;}
            cockpitCamera.transform.localPosition=cameraOrigin+SeatOffset+trackingToRobot*(hp-headBaseline);
            cockpitCamera.transform.localRotation=trackingToRobot*hq;
            if(!ReadController(side,out var p,out var q))
            {
                LoseHandTracking(side);
                if(RequiresPersonalCalibration&&reachPressed[side])CalibrationFeedback(HandName(side)+" 저장 실패: 손 추적을 확인하고 다시 눌러 주세요.");
                return;
            }
            var handRotation=(trackingToRobot*q).normalized;
            bodyPoints[side]=ControlInBody(p,q,gripToControlPoint);bodyRotations[side]=handRotation;poseAt[side]=Time.time;
            if(!PunchTrajectoryRecognizer.Finite(bodyPoints[side])){LoseHandTracking(side);return;}
            if(RequiresPersonalCalibration)
            {
                if((side==0?leftShoulder:rightShoulder).WasPressedThisFrame())CaptureShoulder(side);
                if(guardRequested[side]){guardRequested[side]=false;CaptureGuard(side);}
                if(reachPressed[side])CaptureReach(side);
                if(!reachProfiles[side].IsComplete)return;
                calibrated[side]=true;
            }
            else if(!calibrated[side]){baseline[side]=p;rotationBaseline[side]=handRotation;calibrated[side]=true;return;}
            Vector3 delta=RequiresPersonalCalibration?bodyPoints[side]-reachProfiles[side].Guard:trackingToRobot*(p-baseline[side]);
            Quaternion rotationDelta=handRotation*Quaternion.Inverse(RequiresPersonalCalibration?reachProfiles[side].GuardRotation:rotationBaseline[side]);
            session.SetLocalHandRotation(side,rotationDelta);
            var attack=side==0?leftAttack:rightAttack;if(!attack.IsPressed())releaseAfterLoss[side]=false;
            pendingBoost[side]|=!releaseAfterLoss[side]&&button.WasPressedThisFrame();
            if(Time.time<nextSend)return;
            if(source != PrototypeInput.XRLocalBoth)nextSend=Time.time+.02f;
            float transportScale=RequiresPersonalCalibration?session.tuning.controllerMotionScale:motionScale;
            session.Submit(side,RequiresPersonalCalibration?Vector3.ClampMagnitude(delta,PersonalDeltaLimit)*transportScale:Vector3.ClampMagnitude(delta*transportScale,2.45f),pendingBoost[side],(session.tuning.cooperativeMovement?(side==0?leftStick:rightStick):(side==0?rightStick:leftStick)).ReadValue<Vector2>(),++sequence,
                attack.IsPressed()&&!releaseAfterLoss[side]);
            pendingBoost[side]=false;
        }
        bool ReadController(int side,out Vector3 p,out Quaternion q)
        {
            p=default;q=Quaternion.identity;
            return (side==0?leftTracked:rightTracked).IsPressed()
                &&ArmMotionInput.ReadTrackedPose(side==0?leftPosition:rightPosition,side==0?leftRotation:rightRotation,
                    side==0?leftTrackingState:rightTrackingState,out p,out q)&&ValidRotation(q);
        }
        Vector3 ControlInBody(Vector3 p,Quaternion q,Vector3 offset)
            =>trackingToRobot*(p+q*offset-(compensateHeadTranslation?currentHead:headBaseline));
        bool CaptureAllowed(int side)
            =>side>=0&&side<2&&(OwnsBoth||side==(int)offlineSide)&&RequiresPersonalCalibration&&headCalibrated&&Time.time-poseAt[side]<=.1f;
        public bool CaptureShoulder(int side)
        {
            if(!CaptureAllowed(side)||!ReadController(1-side,out var p,out var q)){CalibrationFeedback("어깨 기준 저장 실패: 양손 추적이 필요합니다.");return false;}
            shoulderPoints[side]=ControlInBody(p,q,shoulderProbeOffset);shoulderCaptured[side]=true;
            reachProfiles[side].Reset();session.ClearPersonalReach(side);session.LoseTracking(side);pendingBoost[side]=false;releaseAfterLoss[side]=true;
            CalibrationFeedback("어깨 근사 기준 저장됨. 편한 가드에서 Y를 다시 눌러 주세요.");return true;
        }
        public bool CaptureGuard(int side)
        {
            if(!CaptureAllowed(side)){CalibrationFeedback("가드 저장 실패: 머리와 손 추적을 확인해 주세요.");return false;}
            Vector3 shoulder=shoulderCaptured[side]?shoulderPoints[side]:(side==0?leftShoulderEstimate:rightShoulderEstimate);
            bool ok=reachProfiles[side].CaptureGuard(shoulder,bodyPoints[side],bodyRotations[side],shoulderCaptured[side]?ShoulderReference.AuxiliaryControlPoint:ShoulderReference.HeadRelativeEstimate,out var reason);
            if(ok){session.ClearPersonalReach(side);session.LoseTracking(side);pendingBoost[side]=false;releaseAfterLoss[side]=true;}
            CalibrationFeedback(ok?HandName(side)+" 가드 저장됨. 편하게 최대한 뻗은 위치를 저장해 주세요.":HandName(side)+" 가드 저장 실패: "+KoreanReason(reason));return ok;
        }
        public bool CaptureReach(int side)
        {
            if(!CaptureAllowed(side)){CalibrationFeedback(HandName(side)+" 저장 실패: 머리와 손 추적을 확인해 주세요.");return false;}
            if(!reachProfiles[side].CaptureReach(bodyPoints[side],out var reason)){CalibrationFeedback(HandName(side)+" 저장 실패: "+KoreanReason(reason));return false;}
            if(!session.SetPersonalReach(side,reachProfiles[side])){reachProfiles[side].Reset();CalibrationFeedback("로봇 팔 준비 실패: 장면의 팔 연결을 확인해야 합니다.");return false;}
            session.LoseTracking(side);pendingBoost[side]=false;releaseAfterLoss[side]=true;
            CalibrationFeedback(HandName(side)+" 최대 도달점 저장됨. 그립을 놓고 가드로 돌아오세요.");return true;
        }
        void LoseHandTracking(int side)
        {
            // Includes loss before first calibration. Reacquisition requires a grip release.
            if(calibrated[side]||!releaseAfterLoss[side])session.LoseTracking(side);calibrated[side]=false;pendingBoost[side]=false;releaseAfterLoss[side]=true;
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
            if(k.rKey.wasPressedThisFrame&&session.Authority&&!GetComponent<IntegratedPlayFlow>())session.ResetSimulation();
            if(Time.time<nextSend)return;nextSend=Time.time+.02f;
            Vector2 move=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            for(int i=0;i<2;i++)
            {
                if(session.NetworkActive&&i!=session.LocalSide)continue;
                Vector3 p=DesktopPose(i,Time.time-animationStart[i],hook[i]);
                if(Time.time-animationStart[i]>.8f && (i==0?k.zKey.isPressed:k.cKey.isPressed))p=new Vector3(0,.65f,0);
                session.Submit(i,p,pendingBoost[i],(session.tuning.cooperativeMovement||i==0)?move:Vector2.zero,++sequence,(i==0?k.fKey:k.gKey).isPressed);pendingBoost[i]=false;
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
            if(t<.5f) { float u=(t-.28f)/.22f; return Vector3.Lerp(windup,end,u)+Vector3.forward*.35f*Mathf.Sin(u*Mathf.PI); }
            return Vector3.Lerp(end,Vector3.zero,(t-.5f)/.25f);
        }
        void OnGUI()
        {
            if(GetComponent<IntegratedOnlineState>()||source!=PrototypeInput.DesktopBoth)return;
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
