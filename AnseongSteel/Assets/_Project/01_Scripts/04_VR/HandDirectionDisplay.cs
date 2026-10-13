using UnityEngine;
using UnityEngine.Rendering;

namespace AnseongSteel.PlayerMotion
{
    [DefaultExecutionOrder(350)]
    public sealed class HandDirectionDisplay : MonoBehaviour
    {
        public ArmMotionInput input;
        public LineRenderer[] controllers, pilotHands, robotHands;
        Material cyan, yellow;

        void EnsureCreated()
        {
            if (controllers != null) return;
            cyan = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.cyan };
            yellow = new Material(cyan) { color = new Color(1, .8f, .05f) };
            controllers = new LineRenderer[2]; pilotHands = new LineRenderer[2]; robotHands = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                controllers[i] = Make(side + " raw controller +Z", cyan, .008f);
                pilotHands[i] = Make(side + " pilot hand +Z", yellow, .007f);
                robotHands[i] = Make(side + " robot hand +Z", yellow, .02f);
            }
        }
        LineRenderer Make(string name, Material material, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>(); line.transform.SetParent(transform, false);
            line.useWorldSpace = true; line.positionCount = 7; line.widthMultiplier = width;
            line.sharedMaterial = material; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.numCapVertices = 3; line.enabled = false; return line;
        }
        static void Arrow(LineRenderer line, Vector3 p, Quaternion rotation, float length)
        {
            var forward = rotation * Vector3.forward; var right = rotation * Vector3.right; var up = rotation * Vector3.up;
            var end = p + forward * length; float wing = length * .16f;
            line.enabled = true;
            line.SetPosition(0, p); line.SetPosition(1, end);
            line.SetPosition(2, end - forward * wing + right * wing * .6f); line.SetPosition(3, end);
            line.SetPosition(4, end - forward * wing - right * wing * .6f); line.SetPosition(5, end);
            line.SetPosition(6, end + up * wing); // Short +Y tick makes wrist roll visible.
        }
        public void Refresh()
        {
            if (!input) return;
            EnsureCreated();
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                controllers[i].enabled = pilotHands[i].enabled = robotHands[i].enabled = false;
                if (!input.isActiveAndEnabled || !input.showHandDirections) continue;
                if (input.ControllerInCockpit(left, out var p, out var q)) Arrow(controllers[i], p, q, .22f);
                if (input.Avatar)
                {
                    var rig = input.Avatar.rig; var arm = left ? rig.left : rig.right;
                    Arrow(pilotHands[i], arm.hand.position, rig.HandFrame(arm), .18f);
                }
                var robot = left ? input.leftArm : input.rightArm;
                if (robot && robot.hand)
                {
                    Arrow(robotHands[i], robot.hand.position, robot.HandFrame, .5f);
                }
            }
        }
        void LateUpdate() => Refresh();
        void OnDisable()
        {
            if (controllers == null) return;
            foreach (var lines in new[] { controllers, pilotHands, robotHands }) foreach (var line in lines) if (line) line.enabled = false;
        }
        void OnDestroy()
        {
            if (controllers != null) foreach (var lines in new[] { controllers, pilotHands, robotHands }) foreach (var line in lines) if (line) Destroy(line.gameObject);
            if (cyan) Destroy(cyan); if (yellow) Destroy(yellow);
        }
    }
}
