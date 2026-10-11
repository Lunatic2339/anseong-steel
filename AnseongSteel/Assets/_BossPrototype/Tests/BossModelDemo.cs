using System.Collections;
using UnityEngine;

namespace AnseongSteel.Bosses
{
    public sealed class BossModelDemo : MonoBehaviour
    {
        public BossAnimationDriver driver;
        public Transform target;
        public Renderer targetRenderer;
        public bool autoDemo = true;
        public bool showControls = true;
        public float approachDistance = 1.05f;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Coroutine demo;
        private string status = "Ready";
        private int hitCount;
        private float flashUntil;
        private Material targetMaterial;

        private void Start()
        {
            startPosition = driver.transform.position;
            startRotation = driver.transform.rotation;
            targetMaterial = targetRenderer.material;
            driver.attack.ImpactChecked += OnImpact;
            if (autoDemo) StartDemo();
        }

        private void Update()
        {
            if (targetMaterial != null)
                targetMaterial.color = Time.time < flashUntil ? new Color(1f, 0.25f, 0.08f) : new Color(0.15f, 0.75f, 0.85f);
        }

        private void OnImpact(Collider[] candidates)
        {
            bool hit = false;
            foreach (var candidate in candidates)
                if (candidate.transform == target || candidate.transform.IsChildOf(target)) hit = true;
            if (hit) { hitCount++; flashUntil = Time.time + 0.55f; }
            status = hit ? "HIT! Target contact confirmed" : "MISS - move closer or face target";
            Debug.Log($"Boss demo: {status}; target hits={hitCount}", this);
        }

        public void StartDemo()
        {
            ResetDemo();
            demo = StartCoroutine(Demonstrate());
        }

        public void StopDemo()
        {
            if(demo!=null)StopCoroutine(demo);demo=null;driver.ResetMotion(.18f);
        }
        public void ResetDemo()
        {
            if (demo != null) StopCoroutine(demo);
            demo = null;
            driver.ResetMotion();
            driver.GetComponent<BossCombatController>()?.ResetEncounter();
            driver.transform.SetPositionAndRotation(startPosition, startRotation);
            hitCount = 0;
            flashUntil = 0f;
            status = "Ready";
        }

        private IEnumerator Demonstrate()
        {
            status = "Idle";
            yield return new WaitForSeconds(1f);
            FaceTarget();
            yield return new WaitUntil(() => !driver.rotation.IsRotating);
            WalkToTarget(1.05f);
            yield return new WaitUntil(() => !driver.movement.IsMoving);
            yield return new WaitForSeconds(0.4f);
            status = "Punch";
            driver.TryPunch();
            yield return new WaitUntil(() => !driver.IsAttacking);
            yield return new WaitForSeconds(0.8f);
            status = "Demo complete - use buttons to repeat";
            demo = null;
        }

        public void FaceTarget()
        {
            status = "Turn toward target";
            driver.rotation.LookAt(target.position);
        }

        public void WalkToTarget()
        {
            WalkToTarget(approachDistance);
        }

        public void WalkToTarget(float distance)
        {
            var direction = target.position - driver.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            driver.movement.MoveTo(target.position - direction.normalized * Mathf.Max(.1f,distance));
            status = "Walking to target";
        }

        private void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.height / 800f, 0.75f, 1.5f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUILayout.BeginArea(new Rect(18, 18, 290, showControls ? 446 : 150), GUI.skin.box);
            GUILayout.Label("MECHA BOSS / v026");
            GUILayout.Label("Idle / Walk / Punch / Turn L & R");
            GUILayout.Space(6);
            GUILayout.Label(status);
            GUILayout.Label($"Target hits: {hitCount}");
            GUILayout.Space(8);
            if (GUILayout.Button(showControls ? "Hide controls" : "Show controls", GUILayout.Height(26))) showControls = !showControls;
            if (!showControls) { if (GUILayout.Button("Run demo", GUILayout.Height(28))) StartDemo(); GUILayout.EndArea(); GUI.matrix = oldMatrix; return; }
            if (GUILayout.Button("Run demo", GUILayout.Height(30))) StartDemo();
            if (GUILayout.Button("Reset", GUILayout.Height(30))) ResetDemo();
            GUI.enabled = demo == null && !driver.IsAttacking && !driver.IsTurning;
            if (GUILayout.Button("Face target", GUILayout.Height(28))) FaceTarget();
            if (GUILayout.Button("Walk to target", GUILayout.Height(28))) WalkToTarget();
            if (GUILayout.Button("Stop / Idle", GUILayout.Height(28))) driver.ResetMotion();
            if (GUILayout.Button("Punch", GUILayout.Height(28))) { status = "Punch"; driver.TryPunch(); }
            if (GUILayout.Button("Turn left 90", GUILayout.Height(28))) driver.rotation.TurnLeft(90f);
            if (GUILayout.Button("Turn right 90", GUILayout.Height(28))) driver.rotation.TurnRight(90f);
            GUI.enabled = true;
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
        }

        private void OnDestroy()
        {
            if (driver != null && driver.attack != null) driver.attack.ImpactChecked -= OnImpact;
            if (targetMaterial != null) Destroy(targetMaterial);
        }
    }
}


