using UnityEngine;

namespace AnseongSteel.PilotV01
{
    /// <summary>Explicitly invoked only for the locally owned avatar. No network ownership assumptions.</summary>
    public sealed class PilotLocalView : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer head;
        private Camera localCamera;
        private int originalLayer;
        private int hiddenLayer;
        private bool cameraOriginallyIncludedLayer;

        public void SetHead(SkinnedMeshRenderer renderer) => head = renderer;

        // Caller reserves an unused layer and calls this after deciding local network ownership.
        // Remote avatars never call this, so their heads stay on their normal layer.
        public void ConfigureLocalCamera(Camera camera, int reservedHeadLayer)
        {
            Restore();
            if (!camera || !head || reservedHeadLayer < 0 || reservedHeadLayer > 31)
                throw new System.ArgumentException("Assign the local camera, head renderer and a reserved layer (0..31).");
            localCamera = camera;
            hiddenLayer = reservedHeadLayer;
            originalLayer = head.gameObject.layer;
            cameraOriginallyIncludedLayer = (camera.cullingMask & (1 << hiddenLayer)) != 0;
            head.gameObject.layer = hiddenLayer;
            camera.cullingMask &= ~(1 << hiddenLayer);
        }

        public void Restore()
        {
            if (!localCamera) return;
            if (head) head.gameObject.layer = originalLayer;
            if (cameraOriginallyIncludedLayer) localCamera.cullingMask |= 1 << hiddenLayer;
            else localCamera.cullingMask &= ~(1 << hiddenLayer);
            localCamera = null;
        }
        private void OnDisable() => Restore();
    }
}
