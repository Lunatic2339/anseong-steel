using UnityEngine;
using UnityEngine.UI;

namespace AnseongSteel.Hud
{
    // Line-only uGUI mesh. No panel, fill, texture, or raycast surface.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SuitWireframe : MaskableGraphic
    {
        Material lineMaterial;
        public SuitDamageState State { get; private set; }
        public void Bind(SuitDamageState state)
        {
            if (State != null) State.Changed -= Refresh;
            State = state; if (State != null) State.Changed += Refresh;
            if(!lineMaterial) lineMaterial=new Material(Shader.Find("AnseongSteel/HudUiLines"));
            material=lineMaterial;
            raycastTarget = false; SetVerticesDirty();
        }
        void Refresh() => SetVerticesDirty();
        protected override void OnDestroy() { if (State != null) State.Changed -= Refresh; if(lineMaterial) Destroy(lineMaterial); base.OnDestroy(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Shape(vh, SuitPart.Head, new[] { V(-.10f,.46f),V(.10f,.46f),V(.13f,.34f),V(0,.28f),V(-.13f,.34f) });
            Shape(vh, SuitPart.Torso, new[] { V(-.22f,.27f),V(.22f,.27f),V(.16f,.04f),V(.11f,-.07f),V(-.11f,-.07f),V(-.16f,.04f) });
            Line(vh,V(-.22f,.27f),V(0,.13f),SuitPart.Torso); Line(vh,V(.22f,.27f),V(0,.13f),SuitPart.Torso);
            Line(vh,V(0,.13f),V(0,-.07f),SuitPart.Torso);
            // Front-facing anatomical left is on the viewer's right.
            Shape(vh,SuitPart.Head,new[]{V(-.07f,.42f),V(.07f,.42f),V(.055f,.35f),V(0,.32f),V(-.055f,.35f)});
            Line(vh,V(-.10f,.38f),V(.10f,.38f),SuitPart.Head);
            Shape(vh,SuitPart.Torso,new[]{V(-.12f,.21f),V(.12f,.21f),V(.08f,.12f),V(-.08f,.12f)});
            Shape(vh,SuitPart.Torso,new[]{V(-.10f,.06f),V(.10f,.06f),V(.08f,-.035f),V(-.08f,-.035f)});
            Arm(vh,1,SuitPart.LeftArm); Arm(vh,-1,SuitPart.RightArm);
            Leg(vh,1,SuitPart.LeftLeg); Leg(vh,-1,SuitPart.RightLeg);
        }
        static Vector2 V(float x,float y) => new Vector2(x,y);
        void Arm(VertexHelper vh,float s,SuitPart part)
        {
            Shape(vh,part,new[]{V(s*.23f,.27f),V(s*.34f,.25f),V(s*.38f,.07f),V(s*.30f,.05f)});
            Shape(vh,part,new[]{V(s*.30f,.04f),V(s*.39f,.06f),V(s*.43f,-.14f),V(s*.34f,-.16f)});
            Line(vh,V(s*.31f,.16f),V(s*.36f,.08f),part);
            Line(vh,V(s*.34f,-.01f),V(s*.39f,-.10f),part);
            Shape(vh,part,new[]{V(s*.34f,-.16f),V(s*.44f,-.15f),V(s*.44f,-.22f),V(s*.35f,-.23f)});
        }
        void Leg(VertexHelper vh,float s,SuitPart part)
        {
            Shape(vh,part,new[]{V(s*.02f,-.08f),V(s*.16f,-.08f),V(s*.21f,-.27f),V(s*.07f,-.28f)});
            Shape(vh,part,new[]{V(s*.07f,-.30f),V(s*.21f,-.29f),V(s*.22f,-.45f),V(s*.06f,-.46f)});
            Line(vh,V(s*.09f,-.12f),V(s*.17f,-.24f),part);
            Line(vh,V(s*.10f,-.33f),V(s*.18f,-.42f),part);
            Shape(vh,part,new[]{V(s*.06f,-.46f),V(s*.22f,-.45f),V(s*.27f,-.50f),V(s*.04f,-.50f)});
        }
        void Shape(VertexHelper vh,SuitPart part,Vector2[] p)
        { for(int i=0;i<p.Length;i++) Line(vh,p[i],p[(i+1)%p.Length],part); }
        void Line(VertexHelper vh,Vector2 a,Vector2 b,SuitPart part)
        {
            a=Vector2.Scale(a,rectTransform.rect.size); b=Vector2.Scale(b,rectTransform.rect.size);
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*.7f;
            Color c=SuitDamageState.ColorFor(State==null?0:State[part]); c.a=.78f;
            int i=vh.currentVertCount;
            vh.AddVert(a-n,c,Vector2.zero); vh.AddVert(a+n,c,Vector2.zero);
            vh.AddVert(b+n,c,Vector2.zero); vh.AddVert(b-n,c,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
        }
    }
}
