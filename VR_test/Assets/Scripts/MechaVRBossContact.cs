using System;
using UnityEngine;

// Animation Rigging first produces the requested pose. Restrict the visible
// pose in LateUpdate, after animation and before rendering. XR input is untouched.
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class MechaVRBossContact : MonoBehaviour
{
    [Serializable]
    public sealed class Arm
    {
        public string label;
        public Transform upperArm;
        public Transform forearm;
        public Transform hand;
        [Tooltip("Enabled trigger BoxColliders used as geometric query volumes. Include the hand/fingers, forearm, upper arm and shoulder armour.")]
        public BoxCollider[] volumes = Array.Empty<BoxCollider>();
        public bool IsBlocked { get; internal set; }
        public string ContactPart { get; internal set; }
        internal readonly Quaternion[] accepted = new Quaternion[3];
        internal readonly Quaternion[] desired = new Quaternion[3];
        internal bool wasBlocked;
        internal float lastHitTime = float.NegativeInfinity;
        internal Vector3[] previousCenters;
        internal float requestedSpeed;
        internal Transform Joint(int i) => i == 0 ? upperArm : i == 1 ? forearm : hand;
        internal bool Valid => upperArm && forearm && hand && volumes != null && volumes.Length > 0;
    }

    public MechaVRBossTarget boss;
    public Arm[] arms = Array.Empty<Arm>();
    [Range(16, 512)] public int maxSubsteps = 192;
    [Range(6, 16)] public int contactRefinements = 10;
    [Min(0f)] public float minimumHitSpeed = 0.2f;
    [Min(0f)] public float hitCooldown = 0.12f;
    public bool BodyBlocked { get; private set; }
    public bool RecoveryFailed { get; private set; }
    public int QueriesLastFrame { get; private set; }
    private bool initialized;
    private Vector3 acceptedPosition;
    private Quaternion acceptedRotation;
    private Vector3 acceptedScale;
    private Vector3 sweepRootFrom, sweepRootTo;
    private Quaternion sweepRotationFrom, sweepRotationTo;

    private struct Contact
    {
        public int arm;
        public BoxCollider volume;
        public Collider surface;
        public Vector3 normal;
        public float depth;
    }

    private void OnEnable() => ResetContactState();
    private void LateUpdate() => ConstrainPose();
    public void ResetContactState() => initialized = false;

    // Public so the Editor can validate the same runtime solver deterministically.
    public void ConstrainPose()
    {
        if (!boss || !boss.isActiveAndEnabled || arms == null || arms.Length == 0) { initialized = false; return; }
        foreach (var arm in arms) if (arm == null || !arm.Valid) { initialized = false; return; }
        QueriesLastFrame = 0;
        BodyBlocked = false;
        RecoveryFailed = false;
        Vector3 requestedPosition = transform.position;
        Quaternion requestedRotation = transform.rotation;
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        foreach (var arm in arms)
        {
            for (int j = 0; j < 3; j++) arm.desired[j] = arm.Joint(j).localRotation;
            bool hasSamples = initialized && arm.previousCenters != null && arm.previousCenters.Length == arm.volumes.Length;
            if (!hasSamples) arm.previousCenters = new Vector3[arm.volumes.Length];
            arm.requestedSpeed = 0f;
            for (int v = 0; v < arm.volumes.Length; v++)
            {
                if (!arm.volumes[v]) continue;
                Vector3 center = arm.volumes[v].transform.TransformPoint(arm.volumes[v].center);
                if (hasSamples) arm.requestedSpeed = Mathf.Max(arm.requestedSpeed, Vector3.Distance(center, arm.previousCenters[v]) / dt);
                arm.previousCenters[v] = center;
            }
        }

        if (!initialized || acceptedScale != transform.lossyScale)
        {
            initialized = true;
            acceptedScale = transform.lossyScale;
            foreach (var arm in arms)
            {
                Array.Copy(arm.desired, arm.accepted, 3);
                arm.wasBlocked = arm.IsBlocked = false;
                foreach (var volume in arm.volumes)
                {
                    if (!volume) continue;
                    volume.isTrigger = true;
                    volume.enabled = true;
                    volume.gameObject.layer = 2; // Ignore Raycast; not a UI/interaction target
                }
            }
            Physics.SyncTransforms();
            RecoverOverlap();
            SaveRoot();
            return;
        }

        // Restore the last accepted full pose before trying body/head movement.
        // A blocked shoulder must not be pushed through boss by the body follower.
        transform.SetPositionAndRotation(acceptedPosition, acceptedRotation);
        foreach (var arm in arms) SetArm(arm, 0f);
        RecoverOverlap();
        SaveRoot();
        Vector3 fromPosition = acceptedPosition;
        Quaternion fromRotation = acceptedRotation;
        float bodyTravel = Vector3.Distance(fromPosition, requestedPosition)
            + Quaternion.Angle(fromRotation, requestedRotation) * Mathf.Deg2Rad * RadiusFrom(transform.position, -1);
        sweepRootFrom = fromPosition;
        sweepRootTo = requestedPosition;
        sweepRotationFrom = fromRotation;
        sweepRotationTo = requestedRotation;
        Contact bodyContact;
        bool bodyHit = Sweep(-1, bodyTravel, out bodyContact);
        BodyBlocked = bodyHit;
        SaveRoot();

        for (int i = 0; i < arms.Length; i++)
        {
            Arm arm = arms[i];
            float travel = 0f;
            float reachBound = Vector3.Distance(arm.upperArm.position, arm.forearm.position)
                + Vector3.Distance(arm.forearm.position, arm.hand.position) + RadiusFrom(arm.hand.position, i);
            for (int j = 0; j < 3; j++)
                travel += Quaternion.Angle(arm.accepted[j], arm.desired[j]) * Mathf.Deg2Rad * reachBound;
            Contact armContact;
            bool armHit = Sweep(i, travel, out armContact);
            arm.IsBlocked = armHit || (bodyHit && bodyContact.arm == i);
            Contact contact = armHit ? armContact : bodyContact;
            arm.ContactPart = arm.IsBlocked && contact.volume ? contact.volume.transform.parent.name : "";
            float speed = arm.requestedSpeed;
            if (arm.IsBlocked && !arm.wasBlocked && speed >= minimumHitSpeed && Time.unscaledTime >= arm.lastHitTime + hitCooldown)
            {
                Vector3 center = contact.volume.transform.TransformPoint(contact.volume.center);
                Physics.SyncTransforms();
                Vector3 point = contact.surface ? contact.surface.ClosestPoint(center) : center;
                boss.RegisterHit(arm.label, point, speed);
                arm.lastHitTime = Time.unscaledTime;
            }
            arm.wasBlocked = arm.IsBlocked;
            for (int j = 0; j < 3; j++) arm.accepted[j] = arm.Joint(j).localRotation;
        }
    }

    private void SaveRoot()
    {
        acceptedPosition = transform.position;
        acceptedRotation = transform.rotation;
    }

    private static void SetArm(Arm arm, float t)
    {
        for (int j = 0; j < 3; j++) arm.Joint(j).localRotation = Quaternion.Slerp(arm.accepted[j], arm.desired[j], t);
    }

    private void ApplySweep(int armIndex, float t)
    {
        if (armIndex < 0)
            transform.SetPositionAndRotation(Vector3.Lerp(sweepRootFrom, sweepRootTo, t), Quaternion.Slerp(sweepRotationFrom, sweepRotationTo, t));
        else SetArm(arms[armIndex], t);
    }

    private bool Sweep(int armIndex, float travel, out Contact contact)
    {
        contact = default;
        if (travel < 0.000001f) { ApplySweep(armIndex, 1f); return Overlap(armIndex, out contact); }
        // Bound corner movement by a fraction of the thinnest contact volume.
        // When a pose jump exceeds the work budget, advance only through the
        // checked portion this frame; never skip the unchecked interval.
        float stepDistance = Mathf.Max(0.0005f, SmallestExtent(armIndex) * 0.4f);
        int required = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(travel / stepDistance, 1000000f)));
        int steps = Mathf.Min(required, Mathf.Max(1, maxSubsteps));
        float safe = 0f;
        for (int k = 1; k <= steps; k++)
        {
            float t = (float)k / required;
            ApplySweep(armIndex, t);
            if (!Overlap(armIndex, out contact)) { safe = t; continue; }
            float blocked = t;
            Contact firstContact = contact;
            for (int n = 0; n < contactRefinements; n++)
            {
                float middle = (safe + blocked) * 0.5f;
                ApplySweep(armIndex, middle);
                if (Overlap(armIndex, out _)) blocked = middle;
                else safe = middle;
            }
            ApplySweep(armIndex, safe);
            contact = firstContact;
            return true;
        }
        return false;
    }

    public bool HasPenetration(out float depth)
    {
        bool hit = Overlap(-1, out Contact contact);
        depth = hit ? contact.depth : 0f;
        return hit;
    }

    private bool Overlap(int armIndex, out Contact deepest)
    {
        deepest = default;
        if (!boss) return false;
        foreach (Collider surface in boss.ContactColliders)
        {
            if (!surface || !surface.enabled || surface.isTrigger || !surface.gameObject.activeInHierarchy) continue;
            for (int i = 0; i < arms.Length; i++)
            {
                if (armIndex >= 0 && i != armIndex) continue;
                foreach (BoxCollider volume in arms[i].volumes)
                {
                    if (!volume) continue;
                    QueriesLastFrame++;
                    if (Physics.ComputePenetration(volume, volume.transform.position, volume.transform.rotation,
                        surface, surface.transform.position, surface.transform.rotation, out Vector3 normal, out float distance)
                        && distance > deepest.depth)
                        deepest = new Contact { arm = i, volume = volume, surface = surface, normal = normal, depth = distance };
                }
            }
        }
        return deepest.volume;
    }

    private void RecoverOverlap()
    {
        // Covers spawn overlap and a boss moving into an already stopped arm.
        // Move only the avatar; never modify the XR camera or controller poses.
        for (int i = 0; i < 16; i++)
        {
            if (!Overlap(-1, out Contact contact)) return;
            transform.position += contact.normal * (contact.depth + 0.0002f);
        }
        RecoveryFailed = Overlap(-1, out _);
    }

    private float SmallestExtent(int armIndex)
    {
        float smallest = float.PositiveInfinity;
        for (int i = 0; i < arms.Length; i++)
        {
            if (armIndex >= 0 && i != armIndex) continue;
            foreach (var volume in arms[i].volumes)
            {
                if (!volume) continue;
                Vector3 size = Vector3.Scale(volume.size, volume.transform.lossyScale);
                smallest = Mathf.Min(smallest, Mathf.Min(Mathf.Abs(size.x), Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.z))) * 0.5f);
            }
        }
        return float.IsInfinity(smallest) ? 0.01f : smallest;
    }

    private float RadiusFrom(Vector3 origin, int armIndex)
    {
        float radius = 0.01f;
        for (int i = 0; i < arms.Length; i++)
        {
            if (armIndex >= 0 && i != armIndex) continue;
            foreach (var volume in arms[i].volumes)
            {
                if (!volume) continue;
                Vector3 center = volume.transform.TransformPoint(volume.center);
                float halfDiagonal = Vector3.Scale(volume.size * 0.5f, volume.transform.lossyScale).magnitude;
                radius = Mathf.Max(radius, Vector3.Distance(origin, center) + halfDiagonal);
            }
        }
        return radius;
    }

    private void OnDrawGizmosSelected()
    {
        if (arms == null) return;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        foreach (var arm in arms)
        {
            if (arm?.volumes == null) continue;
            Gizmos.color = arm.IsBlocked ? Color.red : new Color(0.1f, 1f, 0.65f, 0.8f);
            foreach (var volume in arm.volumes)
            {
                if (!volume) continue;
                Gizmos.matrix = volume.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(volume.center, volume.size);
            }
        }
        Gizmos.matrix = oldMatrix;
    }
}
