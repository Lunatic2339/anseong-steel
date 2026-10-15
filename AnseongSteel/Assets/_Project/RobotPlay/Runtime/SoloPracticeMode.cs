using System;
using System.Collections.Generic;
using System.Collections;
using AnseongSteel.PlayerMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnseongSteel.RobotPlay
{
    [DefaultExecutionOrder(-200)]
    public sealed class SoloPracticeMode : MonoBehaviour
    {
        public SoloPracticeSettings settings;
        public bool Active {get;private set;}
        public bool ContactDistance {get;private set;}
        public bool HookDistance {get;private set;}
        public string Message {get;private set;}="";
        public int LeftHits {get;private set;}
        public int RightHits {get;private set;}
        RobotPlaySession session;RobotPilotInput pilot;SoloPracticeView view;SoloPracticePlayerRig rig;SoloPracticeObserver observer;
        Renderer[] cockpitRenderers;
        MechaVRBossTarget dummy,oldTarget;MechaVRBossContact.Arm[] oldArms;
        Vector3[] practiceGuards;
        PrototypeInput oldSource;RobotPilotInput.Startup oldStartup;
        Transform cameraParent;Vector3 cameraPosition,cameraScale,origin,rightOffset,rootPosition;
        Quaternion cameraRotation,rootRotation;bool previewRight,oldTraining,oldTimeline;
        readonly List<Behaviour> suspended=new List<Behaviour>();readonly List<bool> enabledStates=new List<bool>();
        readonly List<GameObject> hidden=new List<GameObject>();readonly List<bool> activeStates=new List<bool>();
        ReachCalibrationGuide guide;IntegratedPlayFlow priorGuideFlow;
        Canvas panel;Text label;Material uiMaterial,pillarMaterial;Renderer pillarRenderer;float flashUntil;
        readonly float[] homeSince={-1,-1};readonly bool[] hitArmed={false,false};
        bool distanceStickHeld;
        bool creating;
        float oldHitSpeed;readonly bool[] heldPreviously=new bool[2];readonly float[] outward=new float[2];
        readonly ObservedPunch[] intent={ObservedPunch.Unknown,ObservedPunch.Unknown};
        readonly InputAction reset=new InputAction("Reset solo practice",InputActionType.Button,"<XRController>{RightHand}/secondaryButton");
        IEnumerator Start()
        {
            session=GetComponent<RobotPlaySession>();pilot=GetComponent<RobotPilotInput>();
            if(!settings||!settings.startInPractice)yield break;
            var startup=pilot.startup;pilot.startup=RobotPilotInput.Startup.Offline;
            // Let the authored selector/HUD finish Start before suspending them.
            yield return null;Enter();oldStartup=startup;if(!Active)pilot.startup=startup;
        }
        void OnEnable()=>reset.Enable();
        void Suspend(Behaviour b){if(!b)return;suspended.Add(b);enabledStates.Add(b.enabled);b.enabled=false;}
        void Hide(GameObject go){if(!go||hidden.Contains(go))return;hidden.Add(go);activeStates.Add(go.activeSelf);go.SetActive(false);}
        public void Enter()
        {
            if(Active||creating||!session||!pilot)return;
            if(!session.contact||!session.target||!session.robotRoot||!pilot.cockpitCamera){Message="연습 모드의 기존 세션 연결이 누락됐습니다.";return;}
            if(session.NetworkActive){Message="연결을 종료한 뒤 연습 모드로 전환하세요.";return;}
            view=GetComponent<SoloPracticeView>();if(view&&view.Direct){Message="기존 직접 시야를 해제한 뒤 전환하세요.";return;}
            if(!settings||(!settings.playerPrefab&&!settings.rigProfile)){Message="연습용 플레이어 리그 설정이 필요합니다.";Debug.LogError(Message);return;}
            if(!rig)
            {
                try{rig=settings.playerPrefab?Instantiate(settings.playerPrefab,session.robotRoot):SoloPracticeRigFactory.Create(session,settings);}
                catch(Exception error){Message="플레이어 구성 실패: "+error.Message;Debug.LogException(error);return;}
                rig.name="Solo practice player";creating=true;StartCoroutine(FinishCreating());return;
            }
            rig.gameObject.SetActive(true);
            foreach(var t in rig.GetComponentsInChildren<Transform>(true))t.gameObject.layer=26;
            foreach(var animator in rig.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            if(!rig.TryBind(session.robotRoot,out var arms,out var guards))
            {Destroy(rig.gameObject);rig=null;Message="연습용 플레이어의 팔/가드/시야 연결이 올바르지 않습니다.";Debug.LogError(Message);return;}
            // Preserve each subsystem before switching its runtime references.
            oldTarget=session.target;oldArms=session.contact.arms;practiceGuards=guards;oldSource=pilot.source;oldStartup=pilot.startup;
            oldTraining=session.trainingWindow;oldTimeline=session.externalBossTimeline;
            oldHitSpeed=session.contact.minimumHitSpeed;
            rootPosition=session.robotRoot.position;rootRotation=session.robotRoot.rotation;
            var camera=pilot.cockpitCamera.transform;cameraParent=camera.parent;cameraPosition=camera.localPosition;cameraRotation=camera.localRotation;cameraScale=camera.localScale;
            origin=pilot.CameraOrigin;rightOffset=pilot.RightSeatOffset;previewRight=pilot.PreviewRight;
            guide=GetComponent<ReachCalibrationGuide>();priorGuideFlow=guide?guide.flow:null;
            Active=true;
            try
            {
            var flow=GetComponent<IntegratedPlayFlow>();var bridge=GetComponent<IntegratedCombatHud>();
            Suspend(flow);Suspend(GetComponent<IntegratedBossEncounter>());Suspend(GetComponent<IntegratedOnlineState>());Suspend(GetComponent<IntegratedPunchCue>());Suspend(bridge);
            foreach(var visual in session.robotRoot.GetComponentsInChildren<RobotRigVisual>(true))if(!visual.transform.IsChildOf(rig.transform)){Suspend(visual);Hide(visual.gameObject);}
            Hide(oldTarget.gameObject);if(session.statusText)Hide(session.statusText.gameObject);
            pilot.startup=RobotPilotInput.Startup.Offline;pilot.source=PrototypeInput.XRLocalBoth;
            session.trainingWindow=true;session.externalBossTimeline=true;
            CreatePillar();session.ConfigurePractice(true,dummy,arms,guards);
            session.contact.minimumHitSpeed=0; // Button/path/return gates replace the speed cutoff in practice only.
            for(int i=0;i<2;i++)
            {
                var a=arms[i];float length=Vector3.Distance(a.upperArm.position,a.forearm.position)+Vector3.Distance(a.forearm.position,a.hand.position);
                session.Pose(i,Vector3.zero);var follower=a.upperArm.GetComponent<ControllerArmFollower>();follower.targetSpeedLimit=length*settings.armReachPerSecond;follower.targetSmoothSeconds=settings.armResponseSeconds;
            }
            camera.SetParent(session.robotRoot,false);camera.localScale=Vector3.one;
            pilot.SetCockpitOrigin(session.robotRoot.InverseTransformPoint(rig.viewAnchor.position),Vector3.zero,false);
            view=GetComponent<SoloPracticeView>();if(!view)view=gameObject.AddComponent<SoloPracticeView>();
            var hide=new List<Renderer>(rig.localHiddenBody??Array.Empty<Renderer>());var pause=new List<Behaviour>();
            foreach(var r in session.robotRoot.GetComponentsInChildren<Renderer>(true))if(!r.transform.IsChildOf(rig.transform))hide.Add(r);
            foreach(var selector in UnityEngine.Object.FindObjectsByType<IntegratedCockpitSelector>(FindObjectsSortMode.None))
            {
                if(selector.pilot!=pilot)continue;pause.Add(selector);
                if(selector.originalShell)hide.AddRange(selector.originalShell.GetComponentsInChildren<Renderer>(true));
                foreach(var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    for(var t=r.transform;t;t=t.parent)foreach(var variant in selector.variants??Array.Empty<GameObject>())
                        if(variant&&t.name==variant.name+"(Clone)")hide.Add(r);
            }
            if(bridge&&bridge.cockpitContext)
            {
                hide.AddRange(bridge.cockpitContext.GetComponentsInChildren<Renderer>(true));
                var comparison=bridge.cockpitContext.GetComponent<RobotViewComparison>();if(comparison)pause.Add(comparison);
                if(bridge.Hud)Hide(bridge.Hud.HelmetCanvas.gameObject);
            }
            view.Configure(bridge?bridge.feed:null,pause.ToArray(),hide.ToArray(),rig.visibleArms);
            cockpitRenderers=hide.FindAll(r=>r&&!r.transform.IsChildOf(rig.transform)).ToArray();
            Active=true;session.ResetSimulation();session.robotRoot.SetPositionAndRotation(rootPosition,rootRotation);
            // Refresh world anchors after ResetSimulation restores the root.
            pilot.SetCockpitOrigin(session.robotRoot.InverseTransformPoint(rig.viewAnchor.position),Vector3.zero,false);
            if(!view.Enter(pilot,session.robotRoot)){string error=view.LastError;Exit();Message="직접 시야 전환 실패: "+error;Debug.LogError(Message);return;}
            guide=ReachCalibrationGuide.Attach(pilot,null);
            CreatePanel();ContactDistance=HookDistance=false;PlacePillar();ResetCounts();Message="자유공간: 그립을 누르고 잽/훅을 비교하세요. F10: 기둥 거리";
            }
            catch(Exception error){Exit();Message="연습 모드 전환 실패: "+error.Message;Debug.LogException(error);}
        }
        IEnumerator FinishCreating(){yield return null;creating=false;if(isActiveAndEnabled)Enter();else if(rig)Destroy(rig.gameObject);}
        void CreatePillar()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Solo practice pillar";go.layer=26;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,gameObject.scene);
            dummy=go.AddComponent<MechaVRBossTarget>();dummy.logHits=false;dummy.onHit.AddListener(Hit);
            pillarRenderer=go.GetComponent<Renderer>();var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)shader=Shader.Find("Standard");
            pillarMaterial=new Material(shader);pillarMaterial.color=new Color(.2f,.55f,.8f);pillarRenderer.sharedMaterial=pillarMaterial;
        }
        void PlacePillar()
        {
            if(!dummy||!rig)return;
            float reach=0,shoulderZ=float.NegativeInfinity,fistRadius=0;
            foreach(var a in session.contact.arms)
            {
                reach=Mathf.Max(reach,Vector3.Distance(a.upperArm.position,a.forearm.position)+Vector3.Distance(a.forearm.position,a.hand.position));
                shoulderZ=Mathf.Max(shoulderZ,session.robotRoot.InverseTransformPoint(a.upperArm.position).z);
                var box=a.volumes[a.volumes.Length-1];var scale=box.transform.lossyScale;
                fistRadius=Mathf.Max(fistRadius,Vector3.Distance(box.transform.TransformPoint(box.center),a.hand.position)+Vector3.Scale(box.size*.5f,scale).magnitude);
            }
            float width=Mathf.Max(.2f,settings.pillarWidth),height=Mathf.Max(1,session.robotRoot.InverseTransformPoint(rig.viewAnchor.position).y+.3f);
            // The initial free lane lies beyond the new rig's full envelope.
            // Contact lane starts closer; existing pose/collision remains authoritative.
            float distance=shoulderZ+reach+fistRadius+width*.5f+(ContactDistance?-.3f:.5f);
            if(HookDistance)distance=shoulderZ+reach*.45f+fistRadius*.35f+width*.5f;
            float x=0;
            if(ContactDistance&&!HookDistance&&pilot.RequiresPersonalCalibration)
            {
                if(!pilot.ReachProfile(0).IsComplete||!pilot.ReachProfile(1).IsComplete){ContactDistance=false;Message="먼저 양팔 도달점을 저장하세요. 기둥은 자유공간 거리에 유지합니다.";PlacePillar();return;}
                var tips=new Vector3[2];
                for(int i=0;i<2;i++)
                {
                    var a=session.contact.arms[i];var p=pilot.ReachProfile(i);float upper=Vector3.Distance(a.upperArm.position,a.forearm.position),lower=Vector3.Distance(a.forearm.position,a.hand.position);
                    var shoulder=session.robotRoot.InverseTransformPoint(a.upperArm.position);
                    var goal=p.Map(p.ReachPoint-p.Guard,shoulder,practiceGuards[i],upper+lower,ArmReachCalibration.DistanceForBend(upper,lower,3)/(upper+lower),ArmReachCalibration.DistanceForBend(upper,lower,2)/(upper+lower));
                    var forward=(a.hand.position-a.forearm.position).normalized;var box=a.volumes[a.volumes.Length-1];
                    var e=box.size*.5f;
                    float extent=Mathf.Abs(Vector3.Dot(box.transform.TransformVector(Vector3.right*e.x),forward))+Mathf.Abs(Vector3.Dot(box.transform.TransformVector(Vector3.up*e.y),forward))+Mathf.Abs(Vector3.Dot(box.transform.TransformVector(Vector3.forward*e.z),forward));
                    float tip=Vector3.Dot(box.transform.TransformPoint(box.center)-a.hand.position,forward)+extent;
                    tips[i]=goal+(goal-shoulder).normalized*Mathf.Max(0,tip);
                }
                width=Mathf.Max(width,Mathf.Abs(tips[0].x-tips[1].x)+.15f);height=Mathf.Max(height,Mathf.Max(tips[0].y,tips[1].y)+.3f);
                distance=Mathf.Min(tips[0].z,tips[1].z)-reach*.0005f+width*.5f;x=(tips[0].x+tips[1].x)*.5f;
            }
            dummy.transform.SetPositionAndRotation(session.robotRoot.TransformPoint(new Vector3(x,height*.5f,distance)),session.robotRoot.rotation);
            dummy.transform.localScale=new Vector3(width,height,width);session.contact.ResetContactState();
            if(observer&&observer.Observed){observer.Hide();ToggleObservation();}
        }
        void Hit()
        {
            int side=dummy.LastArm=="L"?0:dummy.LastArm=="R"?1:-1;if(side<0||!hitArmed[side]||!session.AttackHeld(side)||!GripHeld(side)||intent[side]==ObservedPunch.Unknown)return;
            var arm=session.contact.arms[side];if(arm.ContactVolume!=arm.volumes[arm.volumes.Length-1])return;
            if(pilot.RequiresPersonalCalibration&&(!pilot.ReachProfile(side).IsComplete||pilot.NeedsGripRelease(side)||!pilot.HandTracked(side)||!pilot.HeadTracked))return;
            hitArmed[side]=false;if(side==0)LeftHits++;else RightHits++;
            Message=(side==0?"왼팔":"오른팔")+(intent[side]==ObservedPunch.Hook?" 훅":" 잽")+" 명중 — 가드로 돌아오면 다음 타격을 받습니다.";flashUntil=Time.unscaledTime+.18f;
            session.PracticeHitFeedback(side);
        }
        void ResetCounts(){LeftHits=RightHits=0;for(int i=0;i<2;i++){homeSince[i]=-1;hitArmed[i]=false;intent[i]=ObservedPunch.Unknown;outward[i]=0;heldPreviously[i]=false;}}
        public void Exit()
        {
            if(!Active)return;Active=false;
            if(observer)observer.Hide();
            if(view)view.Exit();if(panel)panel.gameObject.SetActive(false);
            if(session&&session.contact&&oldTarget&&oldArms!=null){session.ConfigurePractice(false,oldTarget,oldArms,null);session.trainingWindow=oldTraining;session.externalBossTimeline=oldTimeline;session.ResetSimulation();session.robotRoot.SetPositionAndRotation(rootPosition,rootRotation);}
            if(session&&session.contact)session.contact.minimumHitSpeed=oldHitSpeed;
            if(pilot&&pilot.cockpitCamera)
            {
                pilot.source=oldSource;pilot.startup=oldStartup;var c=pilot.cockpitCamera.transform;c.SetParent(cameraParent,false);c.localPosition=cameraPosition;c.localRotation=cameraRotation;c.localScale=cameraScale;
                pilot.SetCockpitOrigin(origin,rightOffset,previewRight);pilot.Recenter();
            }
            if(guide)guide.flow=priorGuideFlow;
            for(int i=hidden.Count-1;i>=0;i--)if(hidden[i])hidden[i].SetActive(activeStates[i]);hidden.Clear();activeStates.Clear();
            for(int i=suspended.Count-1;i>=0;i--)if(suspended[i])suspended[i].enabled=enabledStates[i];suspended.Clear();enabledStates.Clear();
            var flow=GetComponent<IntegratedPlayFlow>();if(flow&&flow.enabled)flow.Retry();
            if(session&&session.robotRoot)session.robotRoot.SetPositionAndRotation(rootPosition,rootRotation);
            if(dummy){dummy.onHit.RemoveListener(Hit);dummy.gameObject.SetActive(false);Destroy(dummy.gameObject);}if(rig){rig.gameObject.SetActive(false);Destroy(rig.gameObject);}
            if(pillarMaterial)Destroy(pillarMaterial);dummy=null;rig=null;Message="기존 모드로 복귀했습니다. 팔을 다시 보정하세요.";
        }
        void Update()
        {
            var keys=Keyboard.current;
            if(keys!=null&&keys.f9Key.wasPressedThisFrame){if(Active)Exit();else Enter();return;}
            if(!Active)return;
            if(keys!=null&&keys.f1Key.wasPressedThisFrame){pilot.source=pilot.source==PrototypeInput.XRLocalBoth?PrototypeInput.DesktopBoth:PrototypeInput.XRLocalBoth;ResetPractice();if(pilot.source==PrototypeInput.DesktopBoth)Message="키보드: F/G 공격 유지 · 1/2 잽 · 3/4 훅 · F1 XR 입력";}
            if((keys!=null&&keys.rKey.wasPressedThisFrame)||reset.WasPressedThisFrame())ResetPractice();
            float stick=pilot.rightStick.ReadValue<Vector2>().y;if(Mathf.Abs(stick)<.3f)distanceStickHeld=false;
            bool toggleStick=pilot.source!=PrototypeInput.DesktopBoth&&stick>.8f&&!distanceStickHeld;
            if(toggleStick)distanceStickHeld=true;
            if((keys!=null&&keys.f10Key.wasPressedThisFrame)||toggleStick)
            {
                if(!ContactDistance){ContactDistance=true;HookDistance=false;}else if(!HookDistance)HookDistance=true;else{ContactDistance=HookDistance=false;}
                PlacePillar();Message=HookDistance?"가까운 훅 거리: 팔이 굽은 접촉 자세를 비교하세요.":ContactDistance?"잽 거리: 그립을 누르고 때린 뒤 가드로 돌아오세요.":"자유공간: 끝까지 펴지는 자세를 비교하세요.";
            }
            if(keys!=null&&keys.f11Key.wasPressedThisFrame)ToggleObservation();
        }
        void ResetPractice(){session.ResetSimulation();session.robotRoot.SetPositionAndRotation(rootPosition,rootRotation);ResetCounts();ContactDistance=HookDistance=false;PlacePillar();Message="다시 보정하세요. Y 가드 / X 왼팔 / A 오른팔";}
        void LateUpdate()
        {
            if(!Active)return;
            if(ContactDistance&&pilot.RequiresPersonalCalibration&&(!pilot.ReachProfile(0).IsComplete||!pilot.ReachProfile(1).IsComplete))
            {ContactDistance=HookDistance=false;PlacePillar();Message="보정이 초기화돼 자유공간으로 돌아왔습니다. Y / X / A로 다시 저장하세요.";}
            for(int i=0;i<2;i++)
            {
                bool held=GripHeld(i);Vector3 delta;bool valid=pilot.RequiresPersonalCalibration?pilot.TryGetCalibratedControlDelta(i,out delta):ReadDesktopDelta(i,out delta);
                float reach=pilot.RequiresPersonalCalibration?pilot.ReachProfile(i).EffectiveReach:.65f;
                if(!valid||!held){intent[i]=ObservedPunch.Unknown;outward[i]=0;if(heldPreviously[i]||!valid)session.CancelPracticeTargetLag(i);}
                heldPreviously[i]=held;
                bool home=valid&&delta.magnitude<Mathf.Clamp(reach*.12f,.025f,.055f);
                if(home){intent[i]=ObservedPunch.Unknown;outward[i]=0;}
                if(!home)homeSince[i]=-1;else if(homeSince[i]<0)homeSince[i]=Time.unscaledTime;
                if(homeSince[i]>=0&&Time.unscaledTime-homeSince[i]>.12f)hitArmed[i]=true;
                if(valid&&held&&!home&&hitArmed[i])
                {
                    float sideward=delta.x*(i==0?-1:1);outward[i]=Mathf.Max(outward[i],sideward);
                    bool forward=delta.z>Mathf.Max(.035f,reach*.18f);
                    bool hookForward=delta.z>Mathf.Clamp(reach*.08f,.025f,.06f);
                    if(hookForward&&outward[i]>Mathf.Clamp(reach*.12f,.04f,.09f)&&outward[i]-sideward>Mathf.Clamp(reach*.08f,.025f,.055f))intent[i]=ObservedPunch.Hook;
                    else if(forward&&Mathf.Abs(delta.x)<delta.z*.6f&&intent[i]!=ObservedPunch.Hook)intent[i]=ObservedPunch.Jab;
                }
            }
            if(pillarMaterial)pillarMaterial.color=Time.unscaledTime<flashUntil?new Color(1,.65f,.15f):new Color(.2f,.55f,.8f);
            if(label)label.text="1인 양팔 연습 · "+(HookDistance?"근거리 훅":ContactDistance?"잽 거리":"자유공간")+"\n왼팔 "+LeftHits+" ("+IntentLabel(0)+") / 오른팔 "+RightHits+" ("+IntentLabel(1)+") · B/R 초기화 · F10 거리 · F9 복귀\n"+Message;
        }
        bool GripHeld(int side)
        {if(pilot.source==PrototypeInput.DesktopBoth){var k=Keyboard.current;return k!=null&&(side==0?k.fKey:k.gKey).isPressed;}return (side==0?pilot.leftAttack:pilot.rightAttack).IsPressed()&&!pilot.NeedsGripRelease(side);}
        bool ReadDesktopDelta(int side,out Vector3 delta){delta=session.LocalControlDelta(side);return session.FreshInput(side);}
        string IntentLabel(int side)=>intent[side]==ObservedPunch.Hook?"훅 경로":intent[side]==ObservedPunch.Jab?"잽 경로":hitArmed[side]?"가드 준비":"가드 복귀";
        void ToggleObservation()
        {
            if(observer&&observer.Observed){observer.Hide();return;}
            if(!observer){observer=gameObject.AddComponent<SoloPracticeObserver>();}
            var body=new List<Renderer>(rig.visibleArms);body.AddRange(rig.localHiddenBody);
            observer.Configure(session.robotRoot,body.ToArray(),new[]{pillarRenderer},cockpitRenderers);
            if(!observer.Show())Message="PC 관찰 전환 실패: "+observer.LastError;
        }
        void OnGUI()
        {
            if(!Active)return;
            if(observer&&observer.Observed&&observer.Output)
            {
                var area=new Rect(0,0,Screen.width,Screen.height);GUI.Box(area,GUIContent.none);
                GUI.DrawTexture(area,observer.Output,ScaleMode.ScaleToFit,false);
            }
            if(GUI.Button(new Rect(12,12,235,34),observer&&observer.Observed?"PC: 1인칭 미러 / F11":"PC: 3인칭 관찰 / F11"))ToggleObservation();
            GUI.Label(new Rect(12,50,700,65),"F9 기존 모드 · F10/오른스틱 위 거리 · B/R 초기화\n왼팔 "+LeftHits+" / 오른팔 "+RightHits+" — "+Message);
        }
        void CreatePanel()
        {
            if(panel){panel.gameObject.SetActive(true);return;}
            var shader=Shader.Find("AnseongSteel/ReachCalibrationOverlay");var font=Resources.Load<Font>("ReachCalibrationFont");
            if(!shader||!font)return;uiMaterial=new Material(shader);
            panel=new GameObject("Solo practice feedback",typeof(RectTransform)).AddComponent<Canvas>();panel.renderMode=RenderMode.WorldSpace;panel.worldCamera=pilot.cockpitCamera;panel.sortingOrder=31999;
            panel.transform.SetParent(pilot.cockpitCamera.transform,false);panel.transform.localPosition=new Vector3(0,-.4f,1.2f);panel.transform.localScale=Vector3.one*.001f;((RectTransform)panel.transform).sizeDelta=new Vector2(850,180);
            var image=panel.gameObject.AddComponent<Image>();image.material=uiMaterial;image.color=new Color(.015f,.035f,.065f,.85f);image.raycastTarget=false;
            label=new GameObject("Practice status",typeof(RectTransform)).AddComponent<Text>();label.transform.SetParent(panel.transform,false);label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=new Vector2(15,10);label.rectTransform.offsetMax=new Vector2(-15,-10);label.font=font;label.material=uiMaterial;label.fontSize=24;label.alignment=TextAnchor.MiddleLeft;label.color=Color.white;label.raycastTarget=false;
            foreach(var t in panel.GetComponentsInChildren<Transform>())t.gameObject.layer=AnseongSteel.Hud.SuitHudController.HelmetLayer;
        }
        void OnDisable(){reset.Disable();StopAllCoroutines();creating=false;Exit();if(rig&&!Active)Destroy(rig.gameObject);}
        void OnDestroy(){Exit();reset.Dispose();if(panel)Destroy(panel.gameObject);if(uiMaterial)Destroy(uiMaterial);}
    }
}
