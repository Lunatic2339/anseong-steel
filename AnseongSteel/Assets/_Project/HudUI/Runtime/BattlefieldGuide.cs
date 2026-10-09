using UnityEngine;

namespace AnseongSteel.Hud
{
    // Feed cameras render this robot-world path into their panorama textures.
    // The HMD is deliberately not an input to path generation or projection.
    public sealed class BattlefieldGuide : MonoBehaviour
    {
        public const int GuideLayer = 29;
        public LineRenderer Line { get; private set; }
        Material material;
        public void Initialize()
        {
            if (Line) return;
            gameObject.layer=GuideLayer;
            Line=gameObject.AddComponent<LineRenderer>(); Line.useWorldSpace=true;
            Line.widthMultiplier=.025f; Line.numCapVertices=3;
            Line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            Line.receiveShadows=false; Line.generateLightingData=false;
            var shader=Shader.Find("AnseongSteel/HudWorldLine");
            material=new Material(shader); Line.sharedMaterial=material;
            SetOpacity(.25f);
        }
        public void SetWorldPath(Vector3[] points)
        {
            Initialize();
            if(points==null || points.Length<2) { Line.positionCount=0; return; }
            foreach(var p in points) if(!Finite(p)) { Line.positionCount=0; return; }
            Line.positionCount=points.Length; Line.SetPositions(points);
        }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&
            !float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z);
        public void SetSuggestion(Vector3 start,Vector3 target,bool hook,bool left)
        {
            var control=(start+target)*.5f;
            if(hook) control+=new Vector3(left?-.65f:.65f,.12f,-.18f);
            var points=new Vector3[25];
            for(int i=0;i<points.Length;i++) { float t=i/(float)(points.Length-1); points[i]=(1-t)*(1-t)*start+2*(1-t)*t*control+t*t*target; }
            SetWorldPath(points);
        }
        public void SetOpacity(float opacity)
        { InitializeIfNeeded(); material.SetColor("_Color",new Color(.25f,.8f,1f,Mathf.Clamp01(opacity))); }
        void InitializeIfNeeded() { if(!Line) Initialize(); }
        public static bool Project(Camera feed,Vector3 point,out Vector2 uv)
        {
            var p=feed.WorldToViewportPoint(point); uv=new Vector2(p.x,p.y);
            return p.z>0 && p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1;
        }
        void OnDestroy() { if(material) Destroy(material); }
    }
}
