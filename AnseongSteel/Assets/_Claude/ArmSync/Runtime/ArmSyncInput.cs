using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace AnseongSteel.ArmSync
{
    public struct TrackedPose
    {
        public bool tracked;
        public Vector3 position;
        public Quaternion rotation;
    }

    // Reads the headset and both controllers in tracking space, plus the test buttons.
    // When no headset is connected (desktop Play mode) a keyboard simulation stands in,
    // so the scene can be checked without a Quest.
    [DefaultExecutionOrder(-50)]
    public sealed class ArmSyncInput : MonoBehaviour
    {
        public enum SimulationMode { Auto, Always, Never }
        public SimulationMode simulation = SimulationMode.Auto;
        public float simulatedHandSpeed = .5f;
        public float simulatedTurnSpeed = 90f;

        public TrackedPose Head { get; private set; }
        public TrackedPose Left { get; private set; }
        public TrackedPose Right { get; private set; }
        public bool Simulating { get; private set; }
        public TrackedPose Hand(int side) => side == 0 ? Left : Right;

        public InputAction Recenter { get; private set; }
        public InputAction ToggleHud { get; private set; }
        public InputAction ToggleRecording { get; private set; }
        public InputAction ToggleMarkers { get; private set; }
        public InputAction ToggleMode { get; private set; }
        public InputAction ToggleMonitor { get; private set; }
        public InputAction ArmLengthUp { get; private set; }
        public InputAction ArmLengthDown { get; private set; }

        readonly InputAction[] positions = new InputAction[3];
        readonly InputAction[] rotations = new InputAction[3];
        readonly InputAction[] states = new InputAction[3];
        InputAction rightStick;
        bool stickWasUp, stickWasDown;
        public bool ArmLengthUpPressed { get; private set; }
        public bool ArmLengthDownPressed { get; private set; }

        Vector2 simLook;
        readonly Vector3[] simHand = { new Vector3(-.18f, -.28f, .32f), new Vector3(.18f, -.28f, .32f) };
        readonly Vector3[] simHandEuler = new Vector3[2];

        void Awake()
        {
            string[] device = { "<XRHMD>", "<XRController>{LeftHand}", "<XRController>{RightHand}" };
            for (int i = 0; i < 3; i++)
            {
                positions[i] = new InputAction("pos" + i, InputActionType.Value, device[i] + (i == 0 ? "/centerEyePosition" : "/devicePosition"), expectedControlType: "Vector3");
                rotations[i] = new InputAction("rot" + i, InputActionType.Value, device[i] + (i == 0 ? "/centerEyeRotation" : "/deviceRotation"), expectedControlType: "Quaternion");
                states[i] = new InputAction("state" + i, InputActionType.Value, device[i] + "/trackingState", expectedControlType: "Integer");
            }
            Recenter = Button("Recenter", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/c");
            ToggleHud = Button("HUD", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/h");
            ToggleRecording = Button("Record", "<XRController>{RightHand}/primaryButton", "<Keyboard>/r");
            ToggleMarkers = Button("Markers", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/g");
            ToggleMode = Button("Mode", "<XRController>{RightHand}/thumbstickClicked", "<Keyboard>/m");
            ToggleMonitor = Button("Monitor", "<XRController>{LeftHand}/thumbstickClicked", "<Keyboard>/v");
            ArmLengthUp = Button("ArmUp", null, "<Keyboard>/equals");
            ArmLengthDown = Button("ArmDown", null, "<Keyboard>/minus");
            rightStick = new InputAction("RightStick", InputActionType.Value, "<XRController>{RightHand}/thumbstick", expectedControlType: "Vector2");
        }

        static InputAction Button(string name, string xr, string key)
        {
            var action = new InputAction(name, InputActionType.Button);
            if (xr != null) action.AddBinding(xr);
            action.AddBinding(key);
            return action;
        }

        InputAction[] All => new[] { positions[0], positions[1], positions[2], rotations[0], rotations[1], rotations[2], states[0], states[1], states[2],
            Recenter, ToggleHud, ToggleRecording, ToggleMarkers, ToggleMode, ToggleMonitor, ArmLengthUp, ArmLengthDown, rightStick };
        void OnEnable() { foreach (var a in All) a.Enable(); }
        void OnDisable() { foreach (var a in All) a.Disable(); }

        // Called by ArmSyncRig in Update and again just before rendering.
        public void Poll(bool frameUpdate)
        {
            Simulating = simulation == SimulationMode.Always
                || (simulation == SimulationMode.Auto && InputSystem.GetDevice<XRHMD>() == null);
            if (Simulating)
            {
                if (frameUpdate) Simulate();
                return;
            }
            Head = Read(0); Left = Read(1); Right = Read(2);
            if (!frameUpdate) return;
            var stick = rightStick.ReadValue<Vector2>();
            bool up = stick.y > .7f, down = stick.y < -.7f;
            ArmLengthUpPressed = (up && !stickWasUp) || ArmLengthUp.WasPressedThisFrame();
            ArmLengthDownPressed = (down && !stickWasDown) || ArmLengthDown.WasPressedThisFrame();
            stickWasUp = up; stickWasDown = down;
        }

        // Position, rotation and tracking flags must come from the same device.
        TrackedPose Read(int i)
        {
            foreach (var control in positions[i].controls)
            {
                if (!(control is Vector3Control p) || !p.device.added || !p.device.enabled) continue;
                QuaternionControl r = null; IntegerControl s = null;
                foreach (var c in rotations[i].controls) if (c.device == p.device && c is QuaternionControl q) { r = q; break; }
                foreach (var c in states[i].controls) if (c.device == p.device && c is IntegerControl t) { s = t; break; }
                if (r == null) continue;
                // InputTrackingState: Position = 1, Rotation = 2.
                if (s != null && (s.ReadValue() & 3) != 3) continue;
                var pos = p.ReadValue(); var rot = r.ReadValue();
                if (!Finite(pos) || !Finite(rot) || Mathf.Abs(Quaternion.Dot(rot, rot) - 1) > .1f) continue;
                return new TrackedPose { tracked = true, position = pos, rotation = rot.normalized };
            }
            return default;
        }

        void Simulate()
        {
            var k = Keyboard.current; var m = Mouse.current; float dt = Time.unscaledDeltaTime;
            ArmLengthUpPressed = ArmLengthUp.WasPressedThisFrame();
            ArmLengthDownPressed = ArmLengthDown.WasPressedThisFrame();
            if (m != null && m.rightButton.isPressed)
            {
                var d = m.delta.ReadValue() * .15f;
                simLook.x += d.x; simLook.y = Mathf.Clamp(simLook.y - d.y, -80, 80);
            }
            var yaw = Quaternion.Euler(0, simLook.x, 0);
            Head = new TrackedPose { tracked = true, position = Vector3.up * 1.2f, rotation = yaw * Quaternion.Euler(simLook.y, 0, 0) };
            if (k != null)
            {
                bool rotate = k.leftShiftKey.isPressed;
                SimHand(0, k.dKey.isPressed, k.aKey.isPressed, k.eKey.isPressed, k.qKey.isPressed, k.wKey.isPressed, k.sKey.isPressed, rotate, dt);
                SimHand(1, k.lKey.isPressed, k.jKey.isPressed, k.oKey.isPressed, k.uKey.isPressed, k.iKey.isPressed, k.kKey.isPressed, rotate, dt);
            }
            Left = new TrackedPose { tracked = true, position = Head.position + yaw * simHand[0], rotation = yaw * Quaternion.Euler(simHandEuler[0]) };
            Right = new TrackedPose { tracked = true, position = Head.position + yaw * simHand[1], rotation = yaw * Quaternion.Euler(simHandEuler[1]) };
        }

        void SimHand(int i, bool xp, bool xn, bool yp, bool yn, bool zp, bool zn, bool rotate, float dt)
        {
            var v = new Vector3((xp ? 1 : 0) - (xn ? 1 : 0), (yp ? 1 : 0) - (yn ? 1 : 0), (zp ? 1 : 0) - (zn ? 1 : 0));
            if (rotate) simHandEuler[i] += new Vector3(-v.z, v.x, -v.y) * simulatedTurnSpeed * dt;
            else simHand[i] += v * simulatedHandSpeed * dt;
        }

        public static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        public static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w);
    }
}
