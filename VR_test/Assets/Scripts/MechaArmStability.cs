using UnityEngine;

public static class MechaArmStability
{
    public sealed class PoleState
    {
        public bool initialized;
        public Vector3 axisLocal, directionLocal;
        public bool hasRotation;
        public Quaternion rotationLocal;
    }

    // C1-continuous at the onset; leaves enough bend for Unity's Hint threshold.
    public static float SoftDistance(float distance, float length)
    {
        if (length <= 0.00001f) return distance;
        float onset = length * .94f, limit = length * .99f;
        if (distance <= onset) return distance;
        return onset + (limit - onset) *
            (1f - Mathf.Exp(-(distance - onset) / (limit - onset)));
    }

    public static Vector3 StableHint(Vector3 shoulder, Vector3 target,
        Vector3 elbow, Vector3 restHint, Quaternion frame, float length,
        float rawReachRatio, bool tracked, float blend, PoleState state,
        Quaternion? handRotation = null)
    {
        Quaternion inverse = Quaternion.Inverse(frame);
        Vector3 axis = inverse * (target - shoulder);
        if (axis.sqrMagnitude < 1e-10f) axis = state.initialized ? state.axisLocal : Vector3.forward;
        axis.Normalize();
        Vector3 candidate = Vector3.ProjectOnPlane(inverse * (elbow - shoulder), axis);
        Vector3 fallback = Vector3.ProjectOnPlane(inverse * (restHint - shoulder), axis);
        if (fallback.sqrMagnitude < 1e-10f) fallback = Vector3.Cross(axis, Vector3.up);
        if (fallback.sqrMagnitude < 1e-10f) fallback = Vector3.Cross(axis, Vector3.right);
        Vector3 direction = fallback.normalized;
        if (state.initialized)
        {
            direction = Quaternion.FromToRotation(state.axisLocal, axis) * state.directionLocal;
            direction = Vector3.ProjectOnPlane(direction, axis).normalized;
            if (direction.sqrMagnitude < .5f) direction = fallback.normalized;
        }
        float extension = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.94f, .98f, rawReachRatio));
        Quaternion rotation = handRotation.HasValue ? inverse * handRotation.Value : Quaternion.identity;
        if (state.initialized && state.hasRotation && handRotation.HasValue)
        {
            // Remove the swing already applied by parallel transport. Only the
            // remaining roll about the shoulder-to-hand axis should turn the pole.
            Quaternion swing = Quaternion.FromToRotation(state.axisLocal, axis);
            Quaternion delta = rotation * Quaternion.Inverse(swing * state.rotationLocal);
            Vector3 v = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            float norm = Mathf.Sqrt(v.sqrMagnitude + delta.w * delta.w);
            if (norm > .00001f)
            {
                Quaternion twist = new Quaternion(v.x / norm, v.y / norm, v.z / norm, delta.w / norm);
                direction = Quaternion.Slerp(Quaternion.identity, twist, extension) * direction;
            }
        }
        state.hasRotation = handRotation.HasValue;
        state.rotationLocal = rotation;
        float confidence = tracked ? 1f - extension : 0f;
        if (candidate.magnitude > length * .035f && confidence > 0f)
            direction = Vector3.Slerp(direction, candidate.normalized,
                Mathf.Clamp01(blend) * confidence).normalized;
        state.initialized = true; state.axisLocal = axis; state.directionLocal = direction;
        return shoulder + frame * (axis * (length * .5f) + direction * (length * .35f));
    }
}
