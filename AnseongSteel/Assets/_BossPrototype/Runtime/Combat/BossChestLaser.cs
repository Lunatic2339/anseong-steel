using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace AnseongSteel.Bosses
{
    [DefaultExecutionOrder(350)]
    public sealed class BossChestLaser : MonoBehaviour
    {
        public Transform muzzle;
        public BossCombatController combat;
        public Vector3 Origin=>muzzle?muzzle.position:transform.TransformPoint(new Vector3(0,2.2f,.5f));
        public Vector3 End {get;private set;}
        public bool IsVisible=>core&&core.enabled;
        LineRenderer core,glow,ring;
        Transform charge,impact;
        Material hotMaterial,glowMaterial;
        Texture2D glowTexture;
        float lastFire=float.NegativeInfinity;
        bool initialized;
        void Awake()=>Initialize();
        public void Initialize()
        {
            if(initialized||!muzzle)return;initialized=true;
            glowTexture=new Texture2D(8,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<64;y++)for(int x=0;x<8;x++){float a=Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(y-31.5f)/31.5f),2);glowTexture.SetPixel(x,y,new Color(1,1,1,a));}
            glowTexture.Apply();
            hotMaterial=Material(new Color(1,.91f,.65f,1),false);
            glowMaterial=Material(new Color(1,.12f,.015f,.8f),true);glowMaterial.SetTexture("_BaseMap",glowTexture);
            core=Line("ChestLaser_Core",hotMaterial,.045f);glow=Line("ChestLaser_Glow",glowMaterial,.24f);
            ring=Line("ChestLaser_ChargeRing",hotMaterial,.018f);ring.positionCount=49;
            charge=Orb("ChestLaser_Charge");impact=Orb("ChestLaser_Impact");Stop();
        }
        Material Material(Color color,bool transparent)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.SetColor("_BaseColor",color);
            if(transparent){m.SetFloat("_Surface",1);m.SetFloat("_Blend",2);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.One);m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=(int)RenderQueue.Transparent;m.SetOverrideTag("RenderType","Transparent");}
            return m;
        }
        LineRenderer Line(string name,Material material,float width)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=material;line.positionCount=2;line.widthCurve=AnimationCurve.Linear(0,1,1,1);line.widthMultiplier=width;line.numCapVertices=8;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;return line;
        }
        Transform Orb(string name)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.transform.SetParent(transform,false);
            var collider=go.GetComponent<Collider>();collider.enabled=false;
            if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
            var r=go.GetComponent<Renderer>();r.sharedMaterial=hotMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return go.transform;
        }
        public void Charge(float t)
        {
            Initialize();if(!initialized)return;
            charge.gameObject.SetActive(true);charge.position=Origin;charge.localScale=Vector3.one*Mathf.Lerp(.035f,.18f,t);
            ring.enabled=true;
            float radius=Mathf.Lerp(.24f,.10f,t);
            for(int i=0;i<49;i++){float angle=i*Mathf.PI*2/48+Time.time*3;ring.SetPosition(i,Origin+(muzzle.right*Mathf.Cos(angle)+muzzle.up*Mathf.Sin(angle))*radius);}
        }
        public Vector3 Trace(Vector3 desiredEnd,LayerMask layers)
        {
            var delta=desiredEnd-Origin;
            foreach(var hit in Physics.RaycastAll(Origin,delta.normalized,delta.magnitude,layers,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
                if(!hit.transform.IsChildOf(transform))return hit.point;
            return desiredEnd;
        }
        public void Fire(Vector3 end)
        {
            Initialize();if(!initialized)return;
            End=end;lastFire=Time.time;Charge(1);
            core.enabled=glow.enabled=true;
            core.SetPosition(0,Origin);core.SetPosition(1,end);glow.SetPosition(0,Origin);glow.SetPosition(1,end);
            core.widthMultiplier=.045f;glow.widthMultiplier=.24f;
            impact.gameObject.SetActive(true);impact.position=end;impact.localScale=Vector3.one*.13f;
        }
        void LateUpdate()
        {
            if(!initialized)return;
            if(!combat||combat.CurrentAttack!=BossAttackKind.LaserSweep||!combat.IsBusy){Stop();return;}
            if(combat.State==BossCombatState.Recovery)
            {
                float fade=Mathf.Clamp01(1-(Time.time-lastFire)/.18f);
                core.widthMultiplier=.045f*fade;glow.widthMultiplier=.24f*fade;
                charge.localScale=Vector3.one*.18f*fade;impact.localScale=Vector3.one*.13f*fade;ring.enabled=false;
                if(fade<=0)Stop();
            }
        }
        public void Stop()
        {
            if(!initialized)return;core.enabled=glow.enabled=ring.enabled=false;charge.gameObject.SetActive(false);impact.gameObject.SetActive(false);
        }
        void OnDisable()=>Stop();
        void OnDestroy(){Release(hotMaterial);Release(glowMaterial);Release(glowTexture);}
        void Release(Object value){if(!value)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
