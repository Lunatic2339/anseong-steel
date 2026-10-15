using AnseongSteel.PlayerMotion;
using UnityEngine;
using UnityEngine.UI;

namespace AnseongSteel.RobotPlay
{
    // A separate head-relative canvas: neither the cockpit feed nor the HUD menu owns its visibility.
    [DefaultExecutionOrder(1100)]
    public sealed class ReachCalibrationGuide : MonoBehaviour
    {
        public RobotPilotInput pilot;
        public IntegratedPlayFlow flow;
        public Canvas Panel { get; private set; }
        public Text Label { get; private set; }
        public Camera AttachedCamera { get; private set; }
        public bool Visible => Panel && Panel.gameObject.activeSelf;
        Material material;
        public static ReachCalibrationGuide Attach(RobotPilotInput pilot, IntegratedPlayFlow flow)
        {
            var guide=pilot.GetComponent<ReachCalibrationGuide>();
            if(!guide)guide=pilot.gameObject.AddComponent<ReachCalibrationGuide>();
            guide.pilot=pilot;guide.flow=flow;guide.Create();return guide;
        }
        void Create()
        {
            if(Panel)return;
            var font=Resources.Load<Font>("ReachCalibrationFont");
            var shader=Shader.Find("AnseongSteel/ReachCalibrationOverlay");
            if(!font||!shader){Debug.LogError("Calibration guide font/shader is missing.");return;}
            material=new Material(shader);
            Panel=new GameObject("보정 안내 - 양안 카메라",typeof(RectTransform)).AddComponent<Canvas>();
            Panel.renderMode=RenderMode.WorldSpace;Panel.overrideSorting=true;Panel.sortingOrder=32000;
            var rect=(RectTransform)Panel.transform;rect.sizeDelta=new Vector2(740,340);
            rect.localScale=Vector3.one*.001f;
            var background=new GameObject("읽기 배경",typeof(RectTransform)).AddComponent<Image>();
            background.transform.SetParent(rect,false);Stretch(background.rectTransform,Vector2.zero);
            background.color=new Color(.015f,.035f,.065f,.96f);background.material=material;background.raycastTarget=false;
            Label=new GameObject("보정 단계와 저장 결과",typeof(RectTransform)).AddComponent<Text>();
            Label.transform.SetParent(rect,false);Stretch(Label.rectTransform,new Vector2(24,18));
            Label.font=font;Label.material=material;Label.fontSize=28;Label.lineSpacing=1.1f;
            Label.color=Color.white;Label.alignment=TextAnchor.MiddleLeft;
            Label.horizontalOverflow=HorizontalWrapMode.Wrap;Label.verticalOverflow=VerticalWrapMode.Truncate;Label.raycastTarget=false;
            foreach(var t in Panel.GetComponentsInChildren<Transform>(true))t.gameObject.layer=AnseongSteel.Hud.SuitHudController.HelmetLayer;
            Panel.gameObject.SetActive(false);
        }
        static void Stretch(RectTransform rect,Vector2 inset)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=inset;rect.offsetMax=-inset;}
        Camera ViewCamera()
        {
            if(pilot.cockpitCamera&&pilot.cockpitCamera.enabled)return pilot.cockpitCamera;
            var context=flow&&flow.bridge?flow.bridge.cockpitContext:null;
            var comparison=context?context.GetComponent<RobotViewComparison>():null;
            return comparison&&comparison.Direct&&comparison.directCamera&&comparison.directCamera.enabled?comparison.directCamera:pilot.cockpitCamera;
        }
        string Tracking()=>"추적  머리 "+(pilot.HeadTracked?"정상":"없음")+" · 왼손 "+(pilot.HandTracked(0)?"정상":"없음")+" · 오른손 "+(pilot.HandTracked(1)?"정상":"없음");
        string Progress(int side)
        {
            var p=pilot.ReachProfile(side);
            return (side==0?"왼팔":"오른팔")+": "+(!p.HasGuard?"가드 필요":!p.IsComplete?"도달점 필요":"저장 완료");
        }
        public string BuildText()
        {
            int first=pilot.NeedsBothCalibration?0:(int)pilot.offlineSide,last=pilot.NeedsBothCalibration?1:first;
            string step="그립을 놓고 편한 가드로 돌아오세요.";
            if(!pilot.HeadTracked)step="머리 추적을 확인해 주세요. 복구 후 다시 보정합니다.";
            else if(!pilot.HandTracked(first)||!pilot.HandTracked(last))step="손이 추적되는 위치로 돌아온 뒤 버튼을 다시 눌러 주세요.";
            else
            {
                for(int i=first;i<=last;i++)if(!pilot.ReachProfile(i).HasGuard){step="팔을 편하게 굽힌 가드에서 Y: 가드 저장";break;}
                if(pilot.ReachProfile(first).HasGuard&&pilot.ReachProfile(last).HasGuard)
                    for(int i=first;i<=last;i++)if(!pilot.ReachProfile(i).IsComplete){step=i==0?"왼팔을 편하게 뻗은 위치에서 X: 도달점 저장":"오른팔을 편하게 뻗은 위치에서 A: 도달점 저장";break;}
            }
            string progress=pilot.NeedsBothCalibration?Progress(0)+" · "+Progress(1):Progress(first);
            bool blocked=false;
            if(pilot.session&&pilot.session.contact)
                for(int i=first;i<=last;i++)blocked|=pilot.ReachProfile(i).IsComplete&&pilot.session.contact.arms[i].IsBlocked;
            string feedback=pilot.LastCalibrationMessage;
            if(blocked&&pilot.PersonalInputArmed)feedback=pilot.session.PracticeActive?"기둥에 닿아 팔 이동이 제한됩니다. 자유공간에서 자세를 비교하세요.":"적에 닿아 팔 이동이 제한됩니다. 보정은 저장되어 있습니다.";
            return "팔 도달 보정\n"+Tracking()+"\n"+progress+"\n"+step+"\n"+feedback;
        }
        public void Refresh()
        {
            if(!pilot)return;Create();if(!Panel)return;
            var camera=ViewCamera();
            if(camera&&camera!=AttachedCamera)
            {
                AttachedCamera=camera;Panel.worldCamera=camera;Panel.transform.SetParent(camera.transform,false);
                Panel.transform.localPosition=new Vector3(0,-.08f,1.15f);Panel.transform.localRotation=Quaternion.identity;Panel.transform.localScale=Vector3.one*.001f;
            }
            int first=pilot.NeedsBothCalibration?0:(int)pilot.offlineSide,last=pilot.NeedsBothCalibration?1:first;
            bool lost=!pilot.HeadTracked||!pilot.HandTracked(first)||!pilot.HandTracked(last);
            bool blocked=false;if(pilot.session&&pilot.session.contact)for(int i=first;i<=last;i++)blocked|=pilot.ReachProfile(i).IsComplete&&pilot.session.contact.arms[i].IsBlocked;
            bool needed=pilot.RequiresPersonalCalibration&&(lost||!pilot.PersonalInputArmed||flow&&flow.State==IntegratedPlayFlow.RunState.Calibrating||Time.unscaledTime-pilot.LastCalibrationMessageAt<4||blocked);
            Label.text=BuildText();Panel.gameObject.SetActive(needed&&camera&&camera.enabled);
        }
        void LateUpdate()=>Refresh();
        void OnDisable(){if(Panel)Panel.gameObject.SetActive(false);}
        void OnDestroy(){if(Panel)Destroy(Panel.gameObject);if(material)Destroy(material);}
    }
}
