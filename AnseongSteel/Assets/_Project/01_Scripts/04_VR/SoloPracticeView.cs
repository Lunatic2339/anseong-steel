using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AnseongSteel.PlayerMotion;

namespace AnseongSteel.RobotPlay
{
    // Temporary local visibility only. The practice owner positions the tracking origin;
    // the existing pose owner remains the sole writer of HMD position and rotation.
    public sealed class SoloPracticeView : MonoBehaviour
    {
        Camera hmdCamera;
        Behaviour poseOwner;
        Transform activeRobotRoot;
        [SerializeField] CockpitLiveView relay;
        [Tooltip("Cockpit selector / old view comparison only. Do not include pose, HUD or arm controllers.")]
        [SerializeField] Behaviour[] suspendWhileDirect = new Behaviour[0];
        [Tooltip("Actual active cockpit shell/display renderers and art-mapped local head/chest occluders.")]
        [SerializeField] Renderer[] localOccluders = new Renderer[0];
        [Tooltip("Art-mapped visible arm renderers. No rig names, scale or model version is assumed.")]
        [SerializeField] Renderer[] visibleArmRenderers = new Renderer[0];
        public bool Direct { get; private set; }
        public string LastError { get; private set; }

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<Renderer> arms = new List<Renderer>();
        readonly List<Behaviour> paused = new List<Behaviour>();
        readonly List<Camera> feeds = new List<Camera>();
        bool[] pausedEnabled, feedEnabled;
        sealed class RenderScope
        {
            public Camera camera;
            public bool[] hiddenOff, armsOff, armsEnabled;
            public RenderScope(int hiddenCount, int armCount)
            { hiddenOff = new bool[hiddenCount]; armsOff = new bool[armCount]; armsEnabled = new bool[armCount]; }
        }
        readonly List<RenderScope> renderScopes = new List<RenderScope>();
        int scopeDepth;
        bool relayEnabled, xrEnabled;
        int previousMask;
        RenderTexture previousTarget;
        UniversalAdditionalCameraData cameraData;

        public void Configure(CockpitLiveView relay, Behaviour[] suspend, Renderer[] hide, Renderer[] show)
        {
            if (Direct) throw new System.InvalidOperationException("Exit the direct view before changing its mapping.");
            this.relay = relay;
            suspendWhileDirect = suspend == null ? new Behaviour[0] : (Behaviour[])suspend.Clone();
            localOccluders = hide == null ? new Renderer[0] : (Renderer[])hide.Clone();
            visibleArmRenderers = show == null ? new Renderer[0] : (Renderer[])show.Clone();
        }
        public bool Enter(RobotPilotInput pilot, Transform robotRoot)
        {
            if (Direct)
            {
                if (pilot && pilot.cockpitCamera == hmdCamera && robotRoot == activeRobotRoot) return true;
                return Refuse("Exit the active direct view before changing pilot or root.");
            }
            if (!pilot || !robotRoot) return Refuse("Practice owner must provide the existing pilot and robot root.");
            hmdCamera = pilot.cockpitCamera; poseOwner = pilot;
            LastError = null;
            if (!isActiveAndEnabled || !hmdCamera || !hmdCamera.isActiveAndEnabled || !poseOwner || !poseOwner.isActiveAndEnabled || !relay)
                return Refuse("Active HMD camera, active pose owner and relay reference are required.");
            if (relay.cockpitCamera != hmdCamera)
                return Refuse("Reuse the relay's existing HMD camera so its HUD and calibration panel stay attached.");
            hidden.Clear(); arms.Clear(); paused.Clear(); feeds.Clear();
            AddUnique(hidden, localOccluders); AddUnique(arms, visibleArmRenderers);
            if (relay.screen && !hidden.Contains(relay.screen)) hidden.Add(relay.screen);
            if (arms.Count == 0) return Refuse("Provide the art-mapped arm renderer references before entering.");
            foreach (var renderer in arms)
            {
                if (hidden.Contains(renderer)) return Refuse("One renderer cannot be both a local occluder and a visible arm.");
                if (!renderer.gameObject.activeInHierarchy)
                    return Refuse("Visible arm GameObjects must already be active; this component never activates model roots.");
            }
            foreach (var behaviour in suspendWhileDirect ?? new Behaviour[0])
            {
                if (!behaviour) continue;
                if (behaviour == this || behaviour == hmdCamera || behaviour == poseOwner || behaviour == relay)
                    return Refuse("Cannot suspend this component, the HMD camera, pose owner or relay through the generic list.");
                if (!paused.Contains(behaviour)) paused.Add(behaviour);
            }
            foreach (var camera in new[] { relay.exteriorCamera, relay.leftCamera, relay.rightCamera, relay.inspectionCamera })
            {
                if (!camera) continue;
                if (camera == hmdCamera) return Refuse("The HMD camera cannot be an exterior feed camera.");
                if (!feeds.Contains(camera)) feeds.Add(camera);
            }
            previousMask = hmdCamera.cullingMask; previousTarget = hmdCamera.targetTexture;
            relayEnabled = relay.enabled;
            cameraData = hmdCamera.GetComponent<UniversalAdditionalCameraData>();
            xrEnabled = cameraData && cameraData.allowXRRendering;
            pausedEnabled = new bool[paused.Count]; feedEnabled = new bool[feeds.Count];
            renderScopes.Clear(); scopeDepth = 0;
            renderScopes.Add(new RenderScope(hidden.Count, arms.Count));
            for (int i = 0; i < paused.Count; i++) pausedEnabled[i] = paused[i].enabled;
            for (int i = 0; i < feeds.Count; i++) feedEnabled[i] = feeds[i].enabled;
            foreach (var behaviour in paused) behaviour.enabled = false;
            // Relay.OnDisable restores its authored targets/settings first. Then stop all feeds.
            relay.enabled = false;
            foreach (var camera in feeds) camera.enabled = false;
            int mask = previousMask | (1 << robotRoot.gameObject.layer) | (1 << 31); // existing helmet/calibration HUD
            foreach (var renderer in arms) mask |= 1 << renderer.gameObject.layer;
            hmdCamera.cullingMask = mask;
            hmdCamera.targetTexture = null; // XRLayout requires direct output, not a relay RT
            if (cameraData) cameraData.allowXRRendering = true; // without additional data, installed URP defaults to XR allowed
            activeRobotRoot = robotRoot;
            Direct = true;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            return true;
        }
        static void AddUnique(List<Renderer> target, Renderer[] sources)
        {
            if (sources == null) return;
            foreach (var source in sources) if (source && !target.Contains(source)) target.Add(source);
        }
        bool Refuse(string message) { LastError = message; return false; }
        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!Direct || (camera != hmdCamera && scopeDepth == 0)) return;
            if (scopeDepth == renderScopes.Count) renderScopes.Add(new RenderScope(hidden.Count, arms.Count));
            var state = renderScopes[scopeDepth]; state.camera = camera;
            for (int i = 0; i < hidden.Count; i++) if (hidden[i]) state.hiddenOff[i] = hidden[i].forceRenderingOff;
            for (int i = 0; i < arms.Count; i++) if (arms[i])
            { state.armsOff[i] = arms[i].forceRenderingOff; state.armsEnabled[i] = arms[i].enabled; }
            if (camera == hmdCamera)
            {
                foreach (var renderer in hidden) if (renderer) renderer.forceRenderingOff = true;
                foreach (var renderer in arms) if (renderer) { renderer.forceRenderingOff = false; renderer.enabled = true; }
            }
            else RestoreScope(renderScopes[0]); // another camera sees the pre-HMD flags even when nested
            scopeDepth++;
        }
        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            int index = scopeDepth - 1;
            while (index >= 0 && renderScopes[index].camera != camera) index--;
            if (index < 0) return;
            while (scopeDepth > index) RestoreScope(renderScopes[--scopeDepth]);
        }
        void RestoreScope(RenderScope state)
        {
            for (int i = 0; i < hidden.Count; i++) if (hidden[i]) hidden[i].forceRenderingOff = state.hiddenOff[i];
            for (int i = 0; i < arms.Count; i++) if (arms[i])
            { arms[i].forceRenderingOff = state.armsOff[i]; arms[i].enabled = state.armsEnabled[i]; }
        }
        void RestoreRenderers()
        { while (scopeDepth > 0) RestoreScope(renderScopes[--scopeDepth]); }
        public void Exit()
        {
            if (!Direct) return;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestoreRenderers();
            Direct = false; activeRobotRoot = null;
            if (hmdCamera) { hmdCamera.cullingMask = previousMask; hmdCamera.targetTexture = previousTarget; }
            if (cameraData) cameraData.allowXRRendering = xrEnabled;
            if (relay) relay.enabled = relayEnabled;
            // Re-enable may force all feeds on; put back each exact pre-entry state afterwards.
            for (int i = 0; i < feeds.Count; i++) if (feeds[i]) feeds[i].enabled = feedEnabled[i];
            for (int i = 0; i < paused.Count; i++) if (paused[i]) paused[i].enabled = pausedEnabled[i];
            hidden.Clear(); arms.Clear(); paused.Clear(); feeds.Clear(); renderScopes.Clear();
        }
        void OnDisable() => Exit();
        void OnDestroy() => Exit();
    }
}
