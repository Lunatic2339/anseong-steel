using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnseongSteel.PlayerMotion
{
    // Robot optics stay independent of HMD motion. Only the display is viewed by the HMD.
    public sealed class CockpitLiveView : MonoBehaviour
    {
        public Camera exteriorCamera, leftCamera, rightCamera;
        public Camera cockpitCamera;
        public Camera inspectionCamera;
        public Renderer screen, playerBody, firstPersonArms;
        public int materialSlot;
        public RenderTexture centerFeed, leftFeed, rightFeed;
        public const float SectorDegrees = 212f / 3f;
        public enum Sampling { FullResolutionSingleSample, AuthoredTextures }
        [Tooltip("Single sample keeps the authored size/FOV, avoiding three offscreen MSAA resolves. Authored is an A/B fallback.")]
        public Sampling sampling = Sampling.FullResolutionSingleSample;
        [Tooltip("CPU render submission diagnostics, not GPU completion or headset latency.")]
        public bool collectTiming;
        public int CockpitSamples { get; private set; }
        public int StaleFeedSamples { get; private set; }
        public int BeforeRenderSamples { get; private set; }
        public int LatePoseFrame { get; private set; } = -1;
        public int BeforeRenderPoseFrame { get; private set; } = -1;
        public Quaternion LateHeadRotation { get; private set; }
        public Quaternion BeforeRenderHeadRotation { get; private set; }
        public Quaternion RenderHeadRotation { get; private set; }
        public double HeadBeforeRenderToSubmissionMs { get; private set; }
        public int[] FeedCompletedFrames { get; } = new[] { -1, -1, -1 };
        public int[] FeedSubmissions { get; } = new int[3];
        public double[] FeedCpuSubmissionMs { get; } = new double[3];
        public double[] FeedIntervalMs { get; } = new double[3];
        public RenderTexture AuthoredFeed(int index) => index >= 0 && index < 3 ? authoredFeeds[index] : null;
        public RenderTexture ActiveFeed(int index) => index >= 0 && index < 3 ? activeFeeds[index] : null;
        readonly Camera[] cameras = new Camera[3];
        readonly RenderTexture[] activeFeeds = new RenderTexture[3];
        readonly RenderTexture[] authoredFeeds = new RenderTexture[3];
        readonly RenderTexture[] originalTargets = new RenderTexture[3];
        readonly bool[] originalEnabled = new bool[3], originalXR = new bool[3];
        readonly RenderTexture[] singleSampleFeeds = new RenderTexture[3];
        public int RuntimeTexturesCreated { get; private set; }
        readonly float[] originalDepth = new float[3];
        readonly StereoTargetEyeMask[] originalEyes = new StereoTargetEyeMask[3];
        readonly UniversalAdditionalCameraData[] cameraData = new UniversalAdditionalCameraData[3];
        readonly double[] began = new double[3], ended = new double[3];
        bool configured, renderingFeed, bodyWasHidden, armsWereEnabled;
        double headSampleTime;
        MaterialPropertyBlock originalProperties;
        Renderer boundScreen;
        int boundSlot;

        void OnEnable()
        {
            if (!exteriorCamera || !leftCamera || !rightCamera || !screen || !centerFeed || !leftFeed || !rightFeed) return;
            cameras[0] = exteriorCamera; cameras[1] = leftCamera; cameras[2] = rightCamera;
            var sources = new[] { centerFeed, leftFeed, rightFeed };
            boundScreen = screen; boundSlot = materialSlot;
            originalProperties = new MaterialPropertyBlock();
            boundScreen.GetPropertyBlock(originalProperties, boundSlot);
            for (int i = 0; i < 3; i++)
            {
                authoredFeeds[i] = sources[i];
                var camera = cameras[i];
                originalTargets[i] = camera.targetTexture; originalEnabled[i] = camera.enabled;
                originalDepth[i] = camera.depth; originalEyes[i] = camera.stereoTargetEye;
                cameraData[i] = camera.GetUniversalAdditionalCameraData();
                originalXR[i] = cameraData[i].allowXRRendering;
                activeFeeds[i] = sampling == Sampling.FullResolutionSingleSample ? SingleSampleFeed(i) : sources[i];
                camera.targetTexture = activeFeeds[i];
                cameraData[i].allowXRRendering = false;
                // URP's explicit control replaces the legacy stereoTargetEye setter/warning.
                camera.depth = cockpitCamera ? Mathf.Min(originalDepth[i], cockpitCamera.depth - 1) : originalDepth[i];
                camera.enabled = true;
                FeedCompletedFrames[i] = -1; began[i] = ended[i] = 0;
            }
            // Keep the existing public feed-to-target contract during Play; restore authored references on disable.
            centerFeed = activeFeeds[0]; leftFeed = activeFeeds[1]; rightFeed = activeFeeds[2];
            BindTextures(centerFeed, leftFeed, rightFeed);
            configured = true;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            Application.onBeforeRender += SampleBeforeRender;
        }
        void BindTextures(RenderTexture center, RenderTexture left, RenderTexture right)
        {
            var properties = new MaterialPropertyBlock();
            boundScreen.GetPropertyBlock(properties, boundSlot);
            properties.SetTexture("_CenterFeed", center); properties.SetTexture("_LeftFeed", left); properties.SetTexture("_RightFeed", right);
            boundScreen.SetPropertyBlock(properties, boundSlot);
        }
        RenderTexture SingleSampleFeed(int index)
        {
            if (singleSampleFeeds[index]) return singleSampleFeeds[index];
            var source = authoredFeeds[index];
            var descriptor = source.descriptor;
            descriptor.msaaSamples = 1; descriptor.bindMS = false;
            var texture = new RenderTexture(descriptor)
            {
                name = source.name + " (runtime single sample)", hideFlags = HideFlags.DontSave,
                filterMode = source.filterMode, wrapMode = source.wrapMode
            };
            if (!texture.Create())
            {
                Destroy(texture);
                Debug.LogWarning("Cockpit runtime RT creation failed; using authored texture.", this);
                return source;
            }
            singleSampleFeeds[index] = texture; RuntimeTexturesCreated++;
            return texture;
        }
        // Reuse the same three runtime textures across A/B; only disable destroys them.
        public void SetSampling(Sampling value)
        {
            if (sampling == value) return;
            sampling = value;
            if (!configured) return;
            for (int i = 0; i < 3; i++)
            {
                activeFeeds[i] = value == Sampling.FullResolutionSingleSample ? SingleSampleFeed(i) : authoredFeeds[i];
                cameras[i].targetTexture = activeFeeds[i];
                FeedCompletedFrames[i] = -1;
            }
            centerFeed = activeFeeds[0]; leftFeed = activeFeeds[1]; rightFeed = activeFeeds[2];
            BindTextures(centerFeed, leftFeed, rightFeed);
        }
        void LateUpdate()
        {
            if (!collectTiming || !cockpitCamera) return;
            LatePoseFrame = Time.frameCount; LateHeadRotation = cockpitCamera.transform.rotation;
        }
        void SampleBeforeRender()
        {
            if (!collectTiming || !cockpitCamera) return;
            BeforeRenderSamples++; BeforeRenderPoseFrame = Time.frameCount;
            BeforeRenderHeadRotation = cockpitCamera.transform.rotation;
            headSampleTime = Time.realtimeSinceStartupAsDouble;
        }
        int FeedIndex(Camera camera)
        {
            for (int i = 0; i < 3; i++) if (camera == cameras[i]) return i;
            return -1;
        }
        bool IsRobotCamera(Camera camera) => FeedIndex(camera) >= 0 || camera == inspectionCamera;
        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            int index = FeedIndex(camera);
            if (collectTiming)
            {
                if (index >= 0) began[index] = Time.realtimeSinceStartupAsDouble;
                if (camera == cockpitCamera)
                {
                    CockpitSamples++;
                    RenderHeadRotation = camera.transform.rotation;
                    HeadBeforeRenderToSubmissionMs = BeforeRenderPoseFrame == Time.frameCount
                        ? (Time.realtimeSinceStartupAsDouble - headSampleTime) * 1000 : -1;
                    for (int i = 0; i < 3; i++)
                        if (cameras[i].enabled && FeedCompletedFrames[i] != Time.frameCount) { StaleFeedSamples++; break; }
                }
            }
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
        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            int index = FeedIndex(camera);
            if (collectTiming && index >= 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                FeedCompletedFrames[index] = Time.frameCount; FeedSubmissions[index]++;
                FeedCpuSubmissionMs[index] = (now - began[index]) * 1000;
                FeedIntervalMs[index] = ended[index] > 0 ? (now - ended[index]) * 1000 : 0;
                ended[index] = now;
            }
            if (IsRobotCamera(camera)) RestorePlayer();
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Application.onBeforeRender -= SampleBeforeRender;
            RestorePlayer();
            if (!configured) return;
            if (boundScreen) boundScreen.SetPropertyBlock(originalProperties, boundSlot);
            for (int i = 0; i < 3; i++)
            {
                if (cameras[i])
                {
                    cameras[i].targetTexture = originalTargets[i]; cameras[i].enabled = originalEnabled[i];
                    cameras[i].depth = originalDepth[i];
                    if (cameras[i].stereoTargetEye != originalEyes[i]) cameras[i].stereoTargetEye = originalEyes[i];
                    if (cameraData[i]) cameraData[i].allowXRRendering = originalXR[i];
                }
                if (singleSampleFeeds[i])
                {
                    singleSampleFeeds[i].Release(); Destroy(singleSampleFeeds[i]); singleSampleFeeds[i] = null;
                }
                activeFeeds[i] = null;
            }
            centerFeed = authoredFeeds[0]; leftFeed = authoredFeeds[1]; rightFeed = authoredFeeds[2];
            configured = false;
        }
    }
}
