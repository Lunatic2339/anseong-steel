using UnityEngine;
using UnityEngine.UI;
namespace AnseongSteel.Hud
{
    // Pure line geometry; white reference-sheet background is never imported.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SuitHudDetail : MaskableGraphic
    {
        public int mode;
        public float integrity=1;
        float armIntegrity=1,legIntegrity=1;
        Material lines;
        protected override void Awake()
        {
            base.Awake(); raycastTarget=false;
            lines=new Material(Shader.Find("AnseongSteel/HudUiLines")); material=lines;
        }
        protected override void OnDestroy() { if(lines) Destroy(lines); base.OnDestroy(); }
        public void SetConditions(float upper,float arms,float legs)
        {
            if(Mathf.Abs(upper-integrity)+Mathf.Abs(arms-armIntegrity)+Mathf.Abs(legs-legIntegrity)<.001f) return;
            integrity=upper; armIntegrity=arms; legIntegrity=legs; SetVerticesDirty();
        }
        public void SetIntegrity(float value)
        { value=Mathf.Clamp01(value); if(Mathf.Abs(value-integrity)<.001f) return; integrity=value; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); float w=rectTransform.rect.width*.5f,h=rectTransform.rect.height*.5f;
            Frame(vh,w,h);
            if(mode==0)
            {
                for(int side=-1;side<=1;side+=2)
                {
                    var p=new Vector2(side*45,0);
                    Arc(vh,p,27,15,280,.8f); Arc(vh,p,32,50,115,.55f); Arc(vh,p,32,225,65,.55f);
                    L(vh,p+new Vector2(-8,7),p+new Vector2(-8,-5));
                    L(vh,p+new Vector2(-8,-5),p+new Vector2(0,-12)); L(vh,p+new Vector2(0,-12),p+new Vector2(8,-5));
                    L(vh,p+new Vector2(8,-5),p+new Vector2(8,7)); L(vh,p+new Vector2(8,7),p+new Vector2(-8,7));
                    L(vh,p+new Vector2(-12,-17),p+new Vector2(0,-12)); L(vh,p+new Vector2(0,-12),p+new Vector2(12,-17));
                }
                L(vh,new Vector2(-10,3),new Vector2(10,3));
                L(vh,new Vector2(10,3),new Vector2(5,7)); L(vh,new Vector2(-10,-3),new Vector2(10,-3));
                L(vh,new Vector2(-10,-3),new Vector2(-5,-7));
            }
            else if(mode==1)
            {
                for(int row=0;row<3;row++)
                {
                    float y=18-row*18; float value=row==0?integrity:row==1?armIntegrity:legIntegrity;
                    Arc(vh,new Vector2(-w+19,y),5,0,320,.65f);
                    for(int i=0;i<20;i++) L(vh,new Vector2(-w+38+i*5,y-3),new Vector2(-w+38+i*5,y+3),i/20f<value?.9f:.16f,1.3f);
                }
            }
            else if(mode==2)
            {
                Arc(vh,new Vector2(-w+22,0),10,0,290,.85f); Arc(vh,new Vector2(-w+22,0),15,45,140,.4f);
                for(int i=0;i<38;i++)
                {
                    float x=-w+48+i*3; float a=Mathf.Sin(i*2.2f)*Mathf.Exp(-i*.12f)*9;
                    float b=Mathf.Sin((i+1)*2.2f)*Mathf.Exp(-(i+1)*.12f)*9;
                    L(vh,new Vector2(x,a),new Vector2(x+3,b),.65f);
                }
            }
            else
            {
                for(int i=0;i<5;i++)
                { L(vh,new Vector2(-w+5,-h+20+i*17),new Vector2(-w+9,-h+20+i*17),.4f); }
                L(vh,new Vector2(-10,-h+4),new Vector2(10,-h+4),.5f);
            }
        }
        void Frame(VertexHelper v,float w,float h)
        {
            foreach(int sx in new[]{-1,1}) foreach(int sy in new[]{-1,1})
            {
                L(v,new Vector2(sx*w,sy*(h-12)),new Vector2(sx*w,sy*(h-5)),.6f);
                L(v,new Vector2(sx*w,sy*(h-5)),new Vector2(sx*(w-5),sy*h),.6f);
                L(v,new Vector2(sx*(w-5),sy*h),new Vector2(sx*(w-22),sy*h),.6f);
            }
        }
        void Arc(VertexHelper v,Vector2 p,float r,float start,float sweep,float alpha)
        {
            int steps=Mathf.CeilToInt(sweep/10);
            for(int i=0;i<steps;i++)
            {
                float a=(start+sweep*i/steps)*Mathf.Deg2Rad,b=(start+sweep*(i+1)/steps)*Mathf.Deg2Rad;
                L(v,p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,p+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r,alpha);
            }
        }
        void L(VertexHelper v,Vector2 a,Vector2 b,float alpha=.8f,float half=.8f)
        {
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*half;
            var c=new Color(.25f,.78f,1,alpha); int i=v.currentVertCount;
            v.AddVert(a-n,c,Vector2.zero); v.AddVert(a+n,c,Vector2.zero);
            v.AddVert(b+n,c,Vector2.zero); v.AddVert(b-n,c,Vector2.zero);
            v.AddTriangle(i,i+1,i+2); v.AddTriangle(i,i+2,i+3);
        }
    }
}