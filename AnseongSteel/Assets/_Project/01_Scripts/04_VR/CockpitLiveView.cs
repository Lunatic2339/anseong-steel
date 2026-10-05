using UnityEngine;
using UnityEngine.Rendering;

namespace AnseongSteel.PlayerMotion
{
    // A fixed robot panorama on the existing physical cockpit display.
    // Three angular sectors preserve pixel density without stretching one wide lens.
    public sealed class CockpitLiveView : MonoBehaviour
    {
        public Camera exteriorCamera, leftCamera, rightCamera;
        public Camera cockpitCamera;
        public Camera inspectionCamera;
        public Renderer screen, playerBody, firstPersonArms;
        public int materialSlot;
        public RenderTexture centerFeed, leftFeed, rightFeed;
        public const float SectorDegrees = 212f / 3f;
        bool renderingFeed, bodyWasHidden, armsWereEnabled;
        void OnEnable()
        {
            if (!exteriorCamera || !leftCamera || !rightCamera || !screen) return;
            var properties = new MaterialPropertyBlock(); screen.GetPropertyBlock(properties, materialSlot);
            properties.SetTexture("_CenterFeed", centerFeed); properties.SetTexture("_LeftFeed", leftFeed); properties.SetTexture("_RightFeed", rightFeed);
            screen.SetPropertyBlock(properties, materialSlot);
            exteriorCamera.targetTexture = centerFeed; leftCamera.targetTexture = leftFeed; rightCamera.targetTexture = rightFeed;
            foreach (var camera in new[] { exteriorCamera, leftCamera, rightCamera })
            { camera.stereoTargetEye = StereoTargetEyeMask.None; camera.enabled = true; }
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }
        bool IsRobotCamera(Camera camera) => camera == exteriorCamera || camera == leftCamera || camera == rightCamera || camera == inspectionCamera;
        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!IsRobotCamera(camera) || renderingFeed) return;
            renderingFeed = true;
            if (playerBody) { bodyWasHidden = playerBody.forceRenderingOff; playerBody.forceRenderingOff = true; }
            if (firstPersonArms) { armsWereEnabled = firstPersonArms.enabled; firstPersonArms.enabled = true; }
        }
        void RestorePlayer()
        {
            if (!renderingFeed) return;
            if (playerBody) playerBody.forceRenderingOff = bodyWasHidden;
            if (firstPersonArms) firstPersonArms.enabled = armsWereEnabled;
            renderingFeed = false;
        }
        void EndCamera(ScriptableRenderContext context, Camera camera) { if (IsRobotCamera(camera)) RestorePlayer(); }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestorePlayer();
        }
    }
}
