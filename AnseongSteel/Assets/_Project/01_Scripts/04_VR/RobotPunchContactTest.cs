using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Input;

namespace AnseongSteel.PlayerMotion
{
    [DefaultExecutionOrder(375)]
    public sealed class RobotPunchContactTest : MonoBehaviour
    {
        public ArmMotionInput input;
        public bool visible = true;
        public int HitCount { get; private set; }
        public float LastHitTime { get; private set; } = -10;
        public Transform[] Pads { get; private set; }
        readonly Vector3[] origins = new Vector3[2], previous = new Vector3[2];
        readonly bool[] sampled = new bool[2], armed = new bool[2];
        readonly float[] struck = { -10, -10 };
        readonly int[] hits = new int[2];
        readonly Renderer[] faces = new Renderer[2];
        readonly TextMesh[] labels = new TextMesh[2];
        readonly Material[] materials = new Material[2];
        static readonly Vector3 Size = new Vector3(.75f, .85f, .22f);
        static readonly Color ReadyColor = new Color(1, .34f, .06f);
        AudioSource audioSource;
        AudioClip thump;
        int priorSide = -1;

        void Start() => EnsureCreated();
        void EnsureCreated()
        {
            if (Pads != null || !input) return;
            Pads = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var arm = i == 0 ? input.leftArm : input.rightArm;
                var pad = new GameObject(i == 0 ? "Left reach contact pad" : "Right reach contact pad").transform;
                pad.SetParent(arm.robotFrame, false); Pads[i] = pad;
                // Position the contact face just inside the robot's full forward reach.
                float length = Vector3.Distance(arm.upper.position, arm.forearm.position) + Vector3.Distance(arm.forearm.position, arm.hand.position);
                var shoulder = arm.robotFrame.InverseTransformPoint(arm.upper.position);
                var n = arm.neutralTarget - shoulder;
                float advance = -n.z + Mathf.Sqrt(Mathf.Max(0, n.z * n.z - n.sqrMagnitude + length * length * .96f * .96f));
                origins[i] = arm.neutralTarget + Vector3.forward * (Mathf.Max(.15f, advance) + .25f);
                pad.localPosition = origins[i];
                materials[i] = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = ReadyColor };
                var face = GameObject.CreatePrimitive(PrimitiveType.Cube); face.name = "Contact surface"; face.transform.SetParent(pad, false);
                face.transform.localScale = Size; Destroy(face.GetComponent<Collider>());
                faces[i] = face.GetComponent<Renderer>(); faces[i].sharedMaterial = materials[i];
                var label = new GameObject("Contact count").AddComponent<TextMesh>(); label.transform.SetParent(pad, false);
                label.transform.localPosition = new Vector3(0, .6f, -.13f); label.anchor = TextAnchor.MiddleCenter;
                label.fontSize = 48; label.characterSize = .03f; label.color = Color.white; labels[i] = label;
                armed[i] = true;
            }
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false;
            // The robot world is separated from the cockpit in this test scene; use headset audio.
            audioSource.spatialBlend = 0; audioSource.volume = .35f;
            const int rate = 44100; var samples = new float[5292]; var random = new System.Random(7);
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                samples[i] = Mathf.Sin(2 * Mathf.PI * (105 * t - 170 * t * t)) * Mathf.Exp(-t * 38) * .7f
                    + ((float)random.NextDouble() * 2 - 1) * Mathf.Exp(-t * 180) * .23f;
            }
            thump = AudioClip.Create("Short contact thump", samples.Length, 1, rate, false); thump.SetData(samples, 0);
        }
        public static bool SweptContact(Vector3 from, Vector3 to, Vector3 halfSize, float radius)
        {
            var box = new Bounds(Vector3.zero, (halfSize + Vector3.one * radius) * 2);
            if (box.Contains(from)) return false;
            var delta = to - from; float distance = delta.magnitude;
            return distance > .0001f && box.IntersectRay(new Ray(from, delta / distance), out float entry) && entry <= distance;
        }
        public Vector3 ContactCenter(int side) => Pads[side].parent.TransformPoint(origins[side]);
        public Vector3 FistCenter(ControllerArmFollower arm) => arm.hand.position + arm.HandFrame * Vector3.forward * .3f;
        void LateUpdate()
        {
            EnsureCreated(); if (Pads == null) return;
            int selected = input.leftPilot ? 0 : 1;
            if (priorSide != selected) { sampled[0] = sampled[1] = false; priorSide = selected; }
            float now = Time.unscaledTime;
            for (int i = 0; i < 2; i++)
            {
                Pads[i].gameObject.SetActive(visible);
                float elapsed = now - struck[i];
                float kick = elapsed < .32f ? Mathf.Sin(Mathf.Clamp01(elapsed / .32f) * Mathf.PI) * .20f : 0;
                Pads[i].localPosition = origins[i] + Vector3.forward * kick;
                materials[i].color = Color.Lerp(Color.white, i == selected ? ReadyColor : Color.gray, Mathf.Clamp01(elapsed / .25f));
                labels[i].text = (i == 0 ? "LEFT" : "RIGHT") + "\nHIT " + hits[i];
                if (!visible || !input.Calibrated || !input.TrackingValid || i != selected) { sampled[i] = false; continue; }
                var arm = i == 0 ? input.leftArm : input.rightArm;
                // Test against the neutral pad location so recoil cannot manufacture another hit.
                var p = arm.robotFrame.InverseTransformPoint(FistCenter(arm)) - origins[i];
                var box = new Bounds(Vector3.zero, Size + Vector3.one * .26f);
                if (!sampled[i]) { previous[i] = p; sampled[i] = true; armed[i] = !box.Contains(p); continue; }
                float speed = Vector3.Distance(p, previous[i]) / Mathf.Max(.001f, Time.unscaledDeltaTime);
                if (!armed[i] && now - struck[i] > .3f && !new Bounds(Vector3.zero, Size + Vector3.one * .4f).Contains(p)) armed[i] = true;
                if (armed[i] && speed > .4f && Time.unscaledDeltaTime < .15f && SweptContact(previous[i], p, Size * .5f, .13f))
                {
                    armed[i] = false; struck[i] = LastHitTime = now; HitCount++; hits[i]++;
                    audioSource.panStereo = i == 0 ? -.2f : .2f; audioSource.PlayOneShot(thump);
                    var device = InputDevices.GetDeviceAtXRNode(i == 0 ? XRNode.LeftHand : XRNode.RightHand);
                    if (device.isValid) OpenXRInput.SendHapticImpulse(device, .4f, 0, .055f);
                }
                previous[i] = p;
            }
        }
        void OnDisable() { if (Pads != null) foreach (var pad in Pads) if (pad) pad.gameObject.SetActive(false); }
        void OnDestroy()
        {
            if (Pads != null) foreach (var pad in Pads) if (pad) Destroy(pad.gameObject);
            foreach (var material in materials) if (material) Destroy(material);
            if (thump) Destroy(thump); if (audioSource) Destroy(audioSource);
        }
    }
}
