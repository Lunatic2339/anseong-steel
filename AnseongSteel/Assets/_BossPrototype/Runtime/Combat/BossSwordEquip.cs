using UnityEngine;
namespace AnseongSteel.Bosses
{
    [DefaultExecutionOrder(190)]
    public sealed class BossSwordEquip : MonoBehaviour
    {
        public bool IsBusy {get;private set;}
        public bool Drawing {get;private set;}
        public float Progress {get;private set;}
        [Range(0,1)] public float drawContact=.36f, sheatheContact=.46f;
        public float GripWeight=>Drawing?Mathf.SmoothStep(0,1,Mathf.InverseLerp(drawContact-.10f,drawContact,Progress)):1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(sheatheContact,sheatheContact+.12f,Progress));
        public float SupportWeight=>Drawing?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,.98f,Progress)):1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.18f,Progress));
        public float GuardWeight=>Drawing?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.86f,1,Progress)):1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.10f,Progress));
        BossAnimationDriver driver;
        BossSwordAttachment sword;
        bool switched;
        Quaternion rightInput,leftInput;
        void Awake(){driver=GetComponent<BossAnimationDriver>();sword=GetComponent<BossSwordAttachment>();rightInput=sword.rightHand.localRotation;leftInput=sword.leftHand.localRotation;driver.MotionReset+=Cancel;}
        // Equip clips animate wrists that other Generic clips leave constant.
        void Update(){sword.rightHand.localRotation=rightInput;sword.leftHand.localRotation=leftInput;}
        public bool TryDraw()=>TryChange(true);
        public bool TrySheathe()=>TryChange(false);
        public bool TryChange(bool draw)
        {
            if(IsBusy||!isActiveAndEnabled||!driver||!sword||!driver.animator.isActiveAndEnabled)return false;
            if(sword.IsDrawn==draw)return true;
            string state=draw?"DrawSword":"SheatheSword";
            if(!driver.animator.HasState(0,Animator.StringToHash(state))||!driver.TryBeginCombatAction(state))return false;
            sword.PrepareEquip();Drawing=draw;Progress=0;switched=false;IsBusy=true;return true;
        }
        void LateUpdate()
        {
            if(!IsBusy)return;
            if(!driver.isActiveAndEnabled||!driver.animator.isActiveAndEnabled){Cancel();return;}
            string name=Drawing?"DrawSword":"SheatheSword";
            var state=driver.animator.GetCurrentAnimatorStateInfo(0);
            if(driver.animator.IsInTransition(0)&&driver.animator.GetNextAnimatorStateInfo(0).IsName(name))state=driver.animator.GetNextAnimatorStateInfo(0);
            if(!state.IsName(name)){Cancel();return;}
            Progress=Mathf.Clamp01(state.normalizedTime);
            if(!switched&&Progress>=(Drawing?drawContact:sheatheContact))
            {
                sword.Transfer(Drawing);switched=true;
            }
            if(Progress>=1){IsBusy=false;driver.EndCombatAction();}
        }
        public void Cancel(){if(!IsBusy)return;IsBusy=false;driver.EndCombatAction();}
        void OnDisable()=>Cancel();
        void OnDestroy(){if(driver)driver.MotionReset-=Cancel;}
    }
}
