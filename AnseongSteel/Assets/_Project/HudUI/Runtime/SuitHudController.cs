using AnseongSteel.PlayerMotion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AnseongSteel.Hud
{
    [DefaultExecutionOrder(450)]
    public sealed class SuitHudController : MonoBehaviour
    {
        public const int HelmetLayer=31;
        public ArmMotionInput Input { get; private set; }
        public CockpitLiveView Feed { get; private set; }
        public SuitDamageState Damage { get; private set; }=new SuitDamageState();
        public BattlefieldGuide Guide { get; private set; }
        public Canvas HelmetCanvas { get; private set; }
        public SuitWireframe Wireframe { get; private set; }
        public bool MenuOpen { get; private set; }
        public bool GuideEnabled { get; private set; }=true;
        public float GuideOpacity { get; private set; }=.25f;
        public float HudScale { get; private set; }=1;
        public int ObservedPunchCount { get; private set; }
        GameObject statusRoot,menuRoot,eventSystem;
        Font font; Text linkText; Slider opacitySlider,scaleSlider,volumeSlider;
        Text guideAction;
        CockpitViewPosition mounts;
        Camera[] cameras; int[] masks;
        bool priorInputDebug,priorMountDebug,priorDirections,priorContact,subscribed,initialized,contactCaptured;
        Renderer statusRenderer,mountRenderer; bool priorStatusRenderer,priorMountRenderer;
        float priorVolume; bool demoGuide=true,hook;
        Vector3 lastStart,lastTarget; bool hasPath,lastHook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // Explicit opt-in test scene; original punch test scene remains unchanged.
            if(SceneManager.GetActiveScene().name!="Hud_V02_Test") return;
            var input=FindFirstObjectByType<ArmMotionInput>(); var feed=FindFirstObjectByType<CockpitLiveView>();
            if(input && feed) Attach(input,feed);
        }
        public static SuitHudController Attach(ArmMotionInput input,CockpitLiveView feed)
        {
            var existing=input.GetComponent<SuitHudController>(); if(existing) return existing;
            var hud=input.gameObject.AddComponent<SuitHudController>(); hud.Initialize(input,feed); return hud;
        }
        public void Initialize(ArmMotionInput input,CockpitLiveView feed)
        {
            if(initialized) return;
            Input=input; Feed=feed; initialized=true; font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mounts=FindFirstObjectByType<CockpitViewPosition>(); priorVolume=AudioListener.volume;
            priorInputDebug=input.showDebugPanels; input.showDebugPanels=false;
            priorDirections=input.showHandDirections; input.showHandDirections=false;
            if(mounts) { priorMountDebug=mounts.showDebugPanels; mounts.showDebugPanels=false;
                if(mounts.status) { mountRenderer=mounts.status.GetComponent<Renderer>(); priorMountRenderer=mountRenderer.enabled; mountRenderer.enabled=false; } }
            if(input.ContactTest) { priorContact=input.ContactTest.visible; input.ContactTest.visible=false; contactCaptured=true; }
            if(input.cockpitStatus) { statusRenderer=input.cockpitStatus.GetComponent<Renderer>(); priorStatusRenderer=statusRenderer.enabled; statusRenderer.enabled=false; }
            cameras=new[]{input.cockpitCamera,feed.exteriorCamera,feed.leftCamera,feed.rightCamera}; masks=new int[cameras.Length];
            for(int i=0;i<cameras.Length;i++)
            {
                masks[i]=cameras[i].cullingMask;
                cameras[i].cullingMask=i==0?(masks[i]|(1<<HelmetLayer))&~(1<<BattlefieldGuide.GuideLayer):
                    (masks[i]|(1<<BattlefieldGuide.GuideLayer))&~(1<<HelmetLayer);
            }
            HelmetCanvas=new GameObject("Helmet HUD - HMD relative",typeof(RectTransform)).AddComponent<Canvas>();
            HelmetCanvas.renderMode=RenderMode.WorldSpace; HelmetCanvas.worldCamera=input.cockpitCamera;
            HelmetCanvas.transform.SetParent(input.cockpitCamera.transform,false);
            var rect=(RectTransform)HelmetCanvas.transform; rect.sizeDelta=new Vector2(1000,650);
            rect.localPosition=new Vector3(0,0,.95f); rect.localRotation=Quaternion.identity; rect.localScale=Vector3.one*.001f;
            HelmetCanvas.gameObject.AddComponent<GraphicRaycaster>();
            statusRoot=Node("Battle status - no background",HelmetCanvas.transform,Vector2.zero,new Vector2(1000,650)).gameObject;
            Wireframe=Node("Accumulated robot condition",statusRoot.transform,new Vector2(-375,-225),new Vector2(58,86)).gameObject.AddComponent<SuitWireframe>();
            Wireframe.Bind(Damage);
            linkText=Label("Pilot",statusRoot.transform,new Vector2(-380,230),new Vector2(110,24),"L / LINK",17);
            Label("Prototype flag",statusRoot.transform,new Vector2(-375,-285),new Vector2(80,16),"TEST",9);
            BuildMenu();
            Guide=new GameObject("Tutorial path - robot world feed only").AddComponent<BattlefieldGuide>(); Guide.Initialize();
            Guide.SetOpacity(GuideOpacity);
            if(!FindFirstObjectByType<EventSystem>())
            {
                eventSystem=new GameObject("HUD menu input",typeof(EventSystem),typeof(InputSystemUIInputModule));
                eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            SetLayer(HelmetCanvas.gameObject,HelmetLayer); Subscribe(); SetMenuOpen(false);
            LayoutHelmet(input.cockpitCamera.aspect);
        }
        void Subscribe() { if(initialized&&!subscribed) { Input.PunchCompleted+=OnPunch; subscribed=true; } }
        void OnPunch(PunchObservation observation)
        {
            ObservedPunchCount++;
            // Recognition informs tutorial type only; outgoing punches never damage our robot.
            if(!demoGuide) return;
            if(observation.kind==ObservedPunch.Jab) hook=false;
            if(observation.kind==ObservedPunch.Hook) hook=true;
        }
        void Update()
        {
            if(!initialized) return;
            var k=Keyboard.current;
            if(k!=null && k.escapeKey.wasPressedThisFrame) SetMenuOpen(!MenuOpen);
        }
        void LateUpdate()
        {
            if(!initialized) return;
            LayoutHelmet(Input.cockpitCamera.aspect);
            if(!contactCaptured && Input.ContactTest) { priorContact=Input.ContactTest.visible; Input.ContactTest.visible=false; contactCaptured=true; }
            linkText.text=(Input.leftPilot?"L":"R")+" / "+(Input.TrackingValid?"LINK":"--");
            Guide.gameObject.SetActive(GuideEnabled&&!MenuOpen&&isActiveAndEnabled);
            if(!demoGuide) return;
            // One shared robot path/target, independent of which pilot is selected.
            var arm=Input.leftArm; if(!arm || !arm.robotFrame) return;
            var start=arm.robotFrame.TransformPoint(arm.neutralTarget);
            var target=Input.ContactTest && Input.ContactTest.Pads!=null ? Input.ContactTest.ContactCenter(0):start+arm.robotFrame.forward*1.2f;
            if(!hasPath || start!=lastStart || target!=lastTarget || hook!=lastHook)
            { Guide.SetSuggestion(start,target,hook,true); lastStart=start; lastTarget=target; lastHook=hook; hasPath=true; }
        }
        public void SupplyWorldGuide(Vector3[] points) { demoGuide=false; Guide.SetWorldPath(points); }
        public void LayoutHelmet(float aspect)
        {
            float height=2*.95f*Mathf.Tan(Input.cockpitCamera.fieldOfView*.5f*Mathf.Deg2Rad)*1000;
            float width=height*aspect;
            ((RectTransform)HelmetCanvas.transform).sizeDelta=new Vector2(width,height);
            ((RectTransform)statusRoot.transform).sizeDelta=new Vector2(width,height);
            Wireframe.rectTransform.anchoredPosition=new Vector2(-width*.5f+95,-height*.5f+115);
            linkText.rectTransform.anchoredPosition=new Vector2(-width*.5f+125,height*.5f-85);
            statusRoot.transform.Find("Prototype flag").GetComponent<RectTransform>().anchoredPosition=new Vector2(-width*.5f+100,-height*.5f+48);
        }
        public void UseTutorialDemo(bool useHook) { demoGuide=true; hook=useHook; hasPath=false; }
        public void SetMenuOpen(bool open)
        {
            MenuOpen=open;
            if(!initialized) return;
            menuRoot.SetActive(open); statusRoot.SetActive(!open);
            Guide.gameObject.SetActive(GuideEnabled&&!open&&isActiveAndEnabled);
            if(!open && EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
        public void SetGuideEnabled(bool enabled)
        { GuideEnabled=enabled; if(guideAction) guideAction.text="TUTORIAL GUIDE    "+(enabled?"ON":"OFF"); if(Guide) Guide.gameObject.SetActive(enabled&&!MenuOpen&&isActiveAndEnabled); }
        public void SetGuideOpacity(float value)
        { GuideOpacity=Mathf.Clamp01(value); if(Guide) Guide.SetOpacity(GuideOpacity); if(opacitySlider) opacitySlider.SetValueWithoutNotify(GuideOpacity); }
        public void SetHudScale(float value)
        { HudScale=Mathf.Clamp(value,.7f,1.2f); if(Wireframe) Wireframe.rectTransform.localScale=Vector3.one*HudScale; if(scaleSlider) scaleSlider.SetValueWithoutNotify(HudScale); }
        public void SetAudio(float value) { AudioListener.volume=Mathf.Clamp01(value); if(volumeSlider) volumeSlider.SetValueWithoutNotify(AudioListener.volume); }
        public void ResetSettings() { SetGuideOpacity(.25f); SetHudScale(1); SetAudio(priorVolume); SetGuideEnabled(true); }
        public void ShowDamageDemo()
        { Damage.Reset(); Damage.SetDamage(SuitPart.LeftArm,.45f); Damage.SetDamage(SuitPart.RightLeg,.85f); }
        void BuildMenu()
        {
            menuRoot=Node("Settings - separate mode",HelmetCanvas.transform,new Vector2(-40,15),new Vector2(420,360)).gameObject;
            Label("Title",menuRoot.transform,new Vector2(0,165),new Vector2(380,40),"SUIT SYSTEM",32);
            volumeSlider=SliderRow("AUDIO",menuRoot.transform,100,0,1,priorVolume,SetAudio);
            opacitySlider=SliderRow("GUIDE OPACITY",menuRoot.transform,50,0,1,GuideOpacity,SetGuideOpacity);
            scaleSlider=SliderRow("HUD SIZE",menuRoot.transform,0,.7f,1.2f,HudScale,SetHudScale);
            guideAction=ButtonRow("TUTORIAL GUIDE    ON",-55,()=>SetGuideEnabled(!GuideEnabled));
            ButtonRow("DAMAGE DEMO / RESET",-100,()=>{ if(Damage[SuitPart.RightLeg]>0) Damage.Reset(); else ShowDamageDemo(); });
            ButtonRow("RESET SETTINGS",-145,ResetSettings);
            ButtonRow("RESUME",-190,()=>SetMenuOpen(false));
            Label("Demo explanation",menuRoot.transform,new Vector2(0,-232),new Vector2(470,36),"TEST SCENE / NO LIVE DAMAGE SOURCE\nESC: CLOSE   TAB: PILOT",11);
        }
        Slider SliderRow(string label,Transform parent,float y,float min,float max,float value,UnityEngine.Events.UnityAction<float> change)
        {
            Label(label,parent,new Vector2(-105,y),new Vector2(170,30),label,20);
            var root=Node(label+" slider",parent,new Vector2(100,y),new Vector2(180,24));
            var track=Node("Track",root,Vector2.zero,new Vector2(180,1)).gameObject.AddComponent<Image>(); track.color=new Color(.2f,.7f,1,.4f);
            var handle=Node("Handle",root,Vector2.zero,new Vector2(7,13)).gameObject.AddComponent<Image>(); handle.color=new Color(.35f,.85f,1,.9f);
            var slider=root.gameObject.AddComponent<Slider>(); slider.handleRect=handle.rectTransform; slider.targetGraphic=handle;
            slider.minValue=min; slider.maxValue=max; slider.SetValueWithoutNotify(value); slider.onValueChanged.AddListener(change); return slider;
        }
        Text ButtonRow(string label,float y,UnityEngine.Events.UnityAction action)
        {
            var text=Label(label,menuRoot.transform,new Vector2(0,y),new Vector2(380,31),label,20); text.raycastTarget=true;
            var button=text.gameObject.AddComponent<Button>(); button.targetGraphic=text;
            var colors=button.colors; colors.normalColor=Color.white; colors.highlightedColor=new Color(.5f,1,1); colors.selectedColor=colors.highlightedColor; button.colors=colors;
            button.onClick.AddListener(action); return text;
        }
        Text Label(string name,Transform parent,Vector2 p,Vector2 size,string value,int fontSize)
        {
            var text=Node(name,parent,p,size).gameObject.AddComponent<Text>(); text.font=font; text.fontSize=fontSize;
            text.text=value; text.color=new Color(.45f,.84f,1,.9f); text.alignment=TextAnchor.MiddleLeft;
            text.horizontalOverflow=HorizontalWrapMode.Overflow; text.verticalOverflow=VerticalWrapMode.Overflow; text.raycastTarget=false; return text;
        }
        static RectTransform Node(string name,Transform parent,Vector2 p,Vector2 size)
        { var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false); r.anchorMin=r.anchorMax=new Vector2(.5f,.5f); r.anchoredPosition=p; r.sizeDelta=size; return r; }
        static void SetLayer(GameObject go,int layer) { go.layer=layer; foreach(Transform c in go.transform) SetLayer(c.gameObject,layer); }
        void OnEnable() { Subscribe(); if(HelmetCanvas) HelmetCanvas.gameObject.SetActive(true); }
        void OnDisable()
        {
            if(subscribed&&Input) { Input.PunchCompleted-=OnPunch; subscribed=false; }
            if(HelmetCanvas) HelmetCanvas.gameObject.SetActive(false); if(Guide) Guide.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if(!initialized) return;
            if(Input) { Input.showDebugPanels=priorInputDebug; Input.showHandDirections=priorDirections; if(Input.ContactTest) Input.ContactTest.visible=priorContact; }
            if(mounts) mounts.showDebugPanels=priorMountDebug; if(statusRenderer) statusRenderer.enabled=priorStatusRenderer;
            if(mountRenderer) mountRenderer.enabled=priorMountRenderer;
            for(int i=0;i<cameras.Length;i++) if(cameras[i]) cameras[i].cullingMask=masks[i];
            AudioListener.volume=priorVolume;
            if(HelmetCanvas) Destroy(HelmetCanvas.gameObject); if(Guide) Destroy(Guide.gameObject); if(eventSystem) Destroy(eventSystem);
        }
    }
}
