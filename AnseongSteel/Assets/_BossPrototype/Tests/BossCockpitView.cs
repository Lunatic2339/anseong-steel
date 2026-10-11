using UnityEngine;
namespace AnseongSteel.Bosses
{
    // Desktop composition preview only; the production XR rig remains separate.
    public sealed class BossCockpitView : MonoBehaviour
    {
        public Camera viewCamera;
        public Renderer targetRenderer;
        public GameObject windowFrame;
        public Vector3 cockpitPosition;
        public Vector3 cockpitEuler;
        public Vector3 overviewPosition = new Vector3(6.5f,4.2f,5.5f);
        public Vector3 overviewEuler;
        public bool cockpitView = true;
        void Start() { SetCockpitView(cockpitView); }
        public void SetCockpitView(bool enabled)
        {
            cockpitView=enabled;
            viewCamera.transform.SetPositionAndRotation(enabled?cockpitPosition:overviewPosition,Quaternion.Euler(enabled?cockpitEuler:overviewEuler));
            viewCamera.fieldOfView=enabled?65f:48f;
            targetRenderer.enabled=!enabled;
            if(windowFrame) windowFrame.SetActive(enabled);
        }
        void OnGUI()
        {
            float scale=Mathf.Clamp(Screen.height/800f,.75f,1.5f);
            var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            if(GUI.Button(new Rect(Screen.width/scale-198,18,180,32),cockpitView?"View: First person / switch":"View: Overview / switch")) SetCockpitView(!cockpitView);
            GUI.matrix=old;
        }
    }
}

