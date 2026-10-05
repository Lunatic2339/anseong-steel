using UnityEngine;
using UnityEngine.InputSystem;

namespace AnseongSteel.PlayerMotion
{
    // Changes the robot-side camera feeding the existing cockpit display.
    // The pilot's physical tracking origin stays fixed when switching views.
    [DefaultExecutionOrder(300)]
    public sealed class CockpitViewPosition : MonoBehaviour
    {
        public enum Mount { Head, Neck, Chest }
        public Camera feedCamera;
        public Transform head, neck, chest;
        public Mount selected;
        public TextMesh status;
        public InputAction cycle = new InputAction("Cycle cockpit view", InputActionType.Button,
            "<XRController>{RightHand}/thumbstickClicked");
        public Transform Current => selected == Mount.Head ? head : selected == Mount.Neck ? neck : chest;
        void OnEnable() { cycle.Enable(); Application.onBeforeRender += BeforeRender; }
        void OnDisable() { cycle.Disable(); Application.onBeforeRender -= BeforeRender; }
        void Update()
        {
            var k = Keyboard.current;
            if (cycle.WasPressedThisFrame() || (k != null && k.vKey.wasPressedThisFrame)) Select((Mount)(((int)selected + 1) % 3));
            if (k != null && k.digit1Key.wasPressedThisFrame) Select(Mount.Head);
            if (k != null && k.digit2Key.wasPressedThisFrame) Select(Mount.Neck);
            if (k != null && k.digit3Key.wasPressedThisFrame) Select(Mount.Chest);
        }
        public void Select(Mount mount) { selected = mount; Apply(); }
        void LateUpdate() { Apply(); }
        [BeforeRenderOrder(300)]
        void BeforeRender() { Apply(); }
        void Apply()
        {
            if (!feedCamera || !Current) return;
            // Robot optical mount and pilot HMD are intentionally independent.
            // Looking around the cockpit never commands the robot's head/cameras.
            feedCamera.transform.SetPositionAndRotation(Current.position, Current.rotation);
            if (status) status.text = $"VIEW: {selected.ToString().ToUpperInvariant()}\nV / right stick click: cycle\n1 Head | 2 Neck | 3 Chest\nBLUE player / RED boss | same model + scale";
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 355, 560, 90), GUI.skin.box);
            GUILayout.Label("Robot camera mount: " + selected);
            GUILayout.BeginHorizontal();
            foreach (Mount mount in System.Enum.GetValues(typeof(Mount)))
                if (GUILayout.Button(mount.ToString())) Select(mount);
            GUILayout.EndHorizontal();
            GUILayout.Label("V / right stick click: cycle | 1 / 2 / 3: select");
            GUILayout.EndArea();
        }
    }
}
