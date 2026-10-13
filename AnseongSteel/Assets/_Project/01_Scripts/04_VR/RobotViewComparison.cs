using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    [DefaultExecutionOrder(400)]
    public sealed class RobotViewComparison : MonoBehaviour
    {
        public ArmMotionInput input;
        public Camera directCamera { get; private set; }
        public bool Direct { get; private set; }
        CockpitLiveView feed;
        CockpitViewPosition mounts;
        AudioListener cockpitListener, directListener;
        string originalTag;

        void Start() => EnsureCreated();
        void EnsureCreated()
        {
            if (directCamera || !input) return;
            feed = FindFirstObjectByType<CockpitLiveView>(); mounts = FindFirstObjectByType<CockpitViewPosition>();
            directCamera = new GameObject("Temporary direct robot XR view").AddComponent<Camera>();
            directCamera.transform.SetParent(transform, false);
            directCamera.nearClipPlane = .03f; directCamera.farClipPlane = 40;
            directCamera.fieldOfView = 75; directCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            directCamera.clearFlags = CameraClearFlags.SolidColor; directCamera.backgroundColor = new Color(.025f, .045f, .07f);
            directCamera.enabled = false;
            directListener = directCamera.gameObject.AddComponent<AudioListener>(); directListener.enabled = false;
            cockpitListener = input.cockpitCamera.GetComponent<AudioListener>(); originalTag = input.cockpitCamera.tag;
            if (feed) feed.inspectionCamera = directCamera;
        }
        public void Toggle() => SetDirect(!Direct);
        public void SetDirect(bool direct)
        {
            EnsureCreated(); if (!directCamera || !feed || !mounts) return;
            Direct = direct;
            input.cockpitCamera.enabled = !direct; directCamera.enabled = direct;
            input.cockpitCamera.tag = direct ? "Untagged" : originalTag;
            directCamera.tag = direct ? "MainCamera" : "Untagged";
            if (cockpitListener) cockpitListener.enabled = !direct;
            directListener.enabled = direct;
            // The panorama is out of view in direct mode. Resume its three feeds on return.
            feed.exteriorCamera.enabled = feed.leftCamera.enabled = feed.rightCamera.enabled = !direct;
            Apply();
        }
        [BeforeRenderOrder(100)]
        public void Apply()
        {
            if (!Direct || !directCamera || !mounts || !input.cockpitCamera) return;
            var eye = input.Avatar ? input.Avatar.neutralEye : Vector3.up * 1.65f;
            var anchor = mounts.Current;
            // A temporary inspection camera only; never rotate the robot or its optical mount.
            directCamera.transform.SetPositionAndRotation(anchor.position + anchor.rotation * (input.cockpitCamera.transform.localPosition - eye),
                anchor.rotation * input.cockpitCamera.transform.localRotation);
        }
        void OnEnable() => Application.onBeforeRender += Apply;
        void LateUpdate() => Apply();
        void OnDisable()
        {
            Application.onBeforeRender -= Apply;
            if (directCamera && input && input.cockpitCamera && feed && mounts) SetDirect(false);
        }
        void OnDestroy()
        {
            if (feed && feed.inspectionCamera == directCamera) feed.inspectionCamera = null;
            if (directCamera) Destroy(directCamera.gameObject);
        }
    }
}
