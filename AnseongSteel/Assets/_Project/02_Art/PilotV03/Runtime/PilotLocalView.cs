using UnityEngine;

namespace AnseongSteel.PilotV03
{
    public sealed class PilotLocalView : MonoBehaviour
    {
        [SerializeField] SkinnedMeshRenderer head;
        Camera localCamera;
        int originalLayer,hiddenLayer;
        bool originalIncluded;
        public SkinnedMeshRenderer Head=>head;
        public void SetHead(SkinnedMeshRenderer renderer)=>head=renderer;
        public void ConfigureLocalCamera(Camera camera,int reservedLayer)
        {
            Restore();
            if(!camera||!head||reservedLayer<0||reservedLayer>31)throw new System.ArgumentException("Camera, head and reserved layer required.");
            localCamera=camera;hiddenLayer=reservedLayer;originalLayer=head.gameObject.layer;originalIncluded=(camera.cullingMask&(1<<hiddenLayer))!=0;
            head.gameObject.layer=hiddenLayer;camera.cullingMask&=~(1<<hiddenLayer);
        }
        public void Restore()
        {
            if(!localCamera)return;
            if(head)head.gameObject.layer=originalLayer;
            if(originalIncluded)localCamera.cullingMask|=1<<hiddenLayer;else localCamera.cullingMask&=~(1<<hiddenLayer);
            localCamera=null;
        }
        void OnDisable()=>Restore();
        void OnDestroy()=>Restore();
    }
}
