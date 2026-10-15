using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AnseongSteel.RobotPlay
{
    // Optional PC observer. Never drives, disables or replaces the HMD camera.
    public sealed class SoloPracticeObserver : MonoBehaviour
    {
        [SerializeField] Transform viewBasis;
        [SerializeField] Renderer[] playerRenderers = new Renderer[0];
        [SerializeField] Renderer[] dummyRenderers = new Renderer[0];
        [SerializeField] Renderer[] cockpitRenderers = new Renderer[0];
        RenderTexture output;
        Camera observer;
        public RenderTexture Output => output;
        public bool Observed => observer && observer.enabled;
        public string LastError { get; private set; }

        public void Configure(Transform basis, Renderer[] player, Renderer[] dummy, Renderer[] cockpit)
        {
            if (Observed) throw new System.InvalidOperationException("Hide PC observation before changing its references.");
            viewBasis = basis;
            playerRenderers = Copy(player); dummyRenderers = Copy(dummy); cockpitRenderers = Copy(cockpit);
        }
        static Renderer[] Copy(Renderer[] values) => values == null ? new Renderer[0] : (Renderer[])values.Clone();
        bool Refuse(string error) { LastError = error; return false; }
        public bool Show()
        {
            if (Observed) return true;
            Release(); // also clean up a camera externally disabled since the previous selection
            LastError = null;
            if (!Application.isPlaying || !isActiveAndEnabled || !viewBasis)
                return Refuse("A configured active practice view is required during Play.");
            if (!TryBounds(playerRenderers, out var player, out int playerLayers) ||
                !TryBounds(dummyRenderers, out var dummy, out int dummyLayers))
                return Refuse("Provide the active full player and dummy renderer references.");
            int mask = playerLayers | dummyLayers;
            // Camera-only filtering: never move originals to another layer or edit renderer flags.
            foreach (var renderer in cockpitRenderers)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    (mask & (1 << renderer.gameObject.layer)) != 0)
                    return Refuse("PC observer model/dummy layers overlap the active cockpit. Practice owner must separate visibility.");
            if ((mask & (1 << 31)) != 0)
                return Refuse("Player/dummy renderers must not use the helmet/calibration HUD layer.");
            player.Encapsulate(dummy);
            float radius = player.extents.magnitude * 1.4f;
            if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius))
                return Refuse("Player/dummy world bounds must be finite and nonzero.");
            output = new RenderTexture(1024, 768, 24)
            { name = "Solo practice PC observation", antiAliasing = 1, hideFlags = HideFlags.DontSave };
            if (!output.Create()) { Release(); return Refuse("PC observation texture creation failed."); }
            var cameraObject = new GameObject("Solo practice PC observer");
            cameraObject.SetActive(false); cameraObject.hideFlags = HideFlags.DontSave;
            observer = cameraObject.AddComponent<Camera>();
            observer.targetTexture = output;
            observer.GetUniversalAdditionalCameraData().allowXRRendering = false;
            observer.cullingMask = mask; observer.aspect = 1024f / 768f; observer.fieldOfView = 60;
            observer.clearFlags = CameraClearFlags.SolidColor; observer.backgroundColor = new Color(.025f,.035f,.05f);
            float verticalHalf = observer.fieldOfView * .5f * Mathf.Deg2Rad;
            float horizontalHalf = Mathf.Atan(Mathf.Tan(verticalHalf) * observer.aspect);
            float distance = radius / Mathf.Sin(Mathf.Min(verticalHalf, horizontalHalf));
            var direction = viewBasis.TransformDirection(new Vector3(1,.3f,1).normalized);
            var up = viewBasis.up;
            observer.transform.SetPositionAndRotation(player.center + direction * distance,
                Quaternion.LookRotation(-direction, up));
            observer.nearClipPlane = Mathf.Max(.01f, distance * .001f);
            observer.farClipPlane = distance + radius * 2;
            cameraObject.SetActive(true);
            return true;
        }
        static bool TryBounds(Renderer[] values, out Bounds bounds, out int layers)
        {
            bounds = new Bounds(); layers = 0; bool found = false;
            foreach (var renderer in values)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
                layers |= 1 << renderer.gameObject.layer;
            }
            return found;
        }
        public void Hide() => Release();
        void Release()
        {
            if (observer)
            {
                observer.enabled = false; observer.targetTexture = null;
                Destroy(observer.gameObject); observer = null;
            }
            if (output) { output.Release(); Destroy(output); output = null; }
        }
        [ContextMenu("Show PC observation (Play only)")]
        void ShowFromInspector() { if (!Show()) Debug.LogWarning(LastError, this); }
        [ContextMenu("Hide PC observation")]
        void HideFromInspector() => Hide();
        void OnDisable() => Hide();
        void OnDestroy() => Hide();
    }
}
