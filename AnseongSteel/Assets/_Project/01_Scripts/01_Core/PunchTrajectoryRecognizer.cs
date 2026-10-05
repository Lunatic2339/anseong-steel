using System;
using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    public enum ObservedPunch { Unknown, Jab, Hook }
    public enum MotionPhase { WaitingForNeutral, Ready, Moving, Recovering, TrackingLost }

    [Serializable]
    public sealed class TrajectoryTuning
    {
        [Tooltip("All distances/speeds are real controller metres, BEFORE robot scaling.")]
        public float startSpeed = .75f;
        public float minimumForward = .18f;
        public float minimumHookSweep = .22f;
        public float minimumHookOutset = .10f;
        public float minimumHookArcRatio = 1.02f;
        public float maximumJabSideRatio = .55f;
        public float maximumVerticalRatio = .7f;
        public float neutralRadius = .12f;
        public float neutralDwell = .15f;
        public float stopSpeed = .20f;
        public float stopDwell = .10f;
        public float maximumStrokeSeconds = 1.25f;
        public float maximumSampleGap = .15f;
        public float maximumTrackingSpeed = 15f;
    }

    [Serializable]
    public struct PunchObservation
    {
        public ObservedPunch kind;
        public float quality, forward, inward, arcRatio, straightness, speed, duration, wristTurn;
        public string reason;
    }

    // Read-only observer of a pose stream. Never supplies poses or animation commands.
    // The quality value is a heuristic score, not a statistical probability.
    public sealed class PunchTrajectoryRecognizer
    {
        readonly TrajectoryTuning tuning;
        readonly Vector3[] points = new Vector3[256];
        readonly float inwardSign;
        Vector3 neutral, previous;
        Quaternion initialRotation;
        float previousTime, began, quietSince = -1, homeSince = -1;
        float peakSpeed, maxForward;
        int count, peakForwardIndex, returnStart = -1;
        bool hasPrevious;
        public MotionPhase Phase { get; private set; } = MotionPhase.TrackingLost;
        public PunchObservation Candidate { get; private set; }
        public PunchObservation LastCompleted { get; private set; }
        public int CompletedCount { get; private set; }
        public event Action<PunchObservation> Completed;

        public PunchTrajectoryRecognizer(bool left, TrajectoryTuning settings)
        { inwardSign = left ? 1 : -1; tuning = settings; }

        public void Calibrate(Vector3 reference)
        {
            neutral = reference; hasPrevious = false; count = 0; homeSince = quietSince = -1;
            Candidate = default; LastCompleted = default; CompletedCount = 0;
            Phase = MotionPhase.WaitingForNeutral;
        }

        public void LoseTracking()
        {
            hasPrevious = false; count = 0; homeSince = quietSince = -1;
            Candidate = new PunchObservation { reason = "Tracking interrupted: recalibrate" };
            Phase = MotionPhase.TrackingLost;
        }

        // A render stall invalidates a stroke's velocity history, not its calibrated
        // pose origin. Keep completed observations and require neutral before rearming.
        public void DiscardInterruptedStroke()
        {
            hasPrevious = false; count = 0; homeSince = quietSince = -1;
            Candidate = new PunchObservation { reason = "Sample gap: return to ready pose" };
            Phase = MotionPhase.WaitingForNeutral;
        }

        public void Sample(Vector3 position, Quaternion rotation, float now)
        {
            if (Phase == MotionPhase.TrackingLost) return;
            if (!Finite(position) || !Finite(rotation) || !float.IsFinite(now)) { LoseTracking(); return; }
            if (!hasPrevious) { previous = position; previousTime = now; hasPrevious = true; return; }
            float dt = now - previousTime;
            if (dt <= 0) return; // duplicate/out-of-order samples do not advance state
            if (dt > tuning.maximumSampleGap) { LoseTracking(); return; }
            Vector3 velocity = (position - previous) / dt;
            if (velocity.magnitude > tuning.maximumTrackingSpeed) { LoseTracking(); return; }
            Vector3 offset = position - neutral;
            if (Phase == MotionPhase.WaitingForNeutral || Phase == MotionPhase.Recovering)
            {
                if (offset.magnitude <= tuning.neutralRadius && velocity.magnitude <= tuning.stopSpeed)
                {
                    if (homeSince < 0) homeSince = now;
                    if (now - homeSince >= tuning.neutralDwell) Phase = MotionPhase.Ready;
                }
                else homeSince = -1;
            }
            else if (Phase == MotionPhase.Ready)
            {
                // Sideways preparation is allowed; raising/lowering a guard and rearward pulls are not attacks.
                bool departure = velocity.z > .25f || (-velocity.x * inwardSign > .35f && offset.z > .015f);
                if (velocity.magnitude >= tuning.startSpeed && offset.magnitude > .035f && departure)
                {
                    count = 0; points[count++] = previous; points[count++] = position;
                    began = previousTime; peakSpeed = velocity.magnitude;
                    initialRotation = rotation; maxForward = offset.z; peakForwardIndex = 1;
                    quietSince = -1; returnStart = -1; Phase = MotionPhase.Moving;
                }
            }
            else if (Phase == MotionPhase.Moving)
            {
                peakSpeed = Mathf.Max(peakSpeed, velocity.magnitude);
                if (count == points.Length) { Finish(now, rotation, "Sample capacity exceeded"); }
                else
                {
                    points[count++] = position;
                    if (offset.z >= maxForward) { maxForward = offset.z; peakForwardIndex = count - 1; }
                    bool retracting = velocity.z < -.30f && -velocity.z > Mathf.Abs(velocity.x) * .8f
                        && Vector3.Dot(velocity, -offset) > .02f;
                    if (retracting && returnStart < 0) returnStart = count - 2;
                    if (!retracting) returnStart = -1;
                    Candidate = Evaluate(count - 1, now, rotation);
                    if (velocity.magnitude < tuning.stopSpeed)
                    {
                        if (quietSince < 0) quietSince = now;
                        if (now - quietSince >= tuning.stopDwell) Finish(now, rotation);
                    }
                    else quietSince = -1;
                    if (Phase == MotionPhase.Moving && returnStart >= 0 && maxForward - offset.z > .065f)
                        Finish(now, rotation, null, returnStart);
                    if (Phase == MotionPhase.Moving && now - began > tuning.maximumStrokeSeconds)
                        Finish(now, rotation, "Motion too long: no discrete punch");
                }
            }
            previous = position; previousTime = now;
        }

        // Future authoritative hit logic may finalize at actual contact. A candidate alone never deals damage.
        public bool ConfirmContact(float now, Quaternion rotation)
        {
            if (Phase != MotionPhase.Moving) return false;
            Finish(now, rotation); return true;
        }

        void Finish(float now, Quaternion rotation, string rejected = null, int last = -1)
        {
            var result = Evaluate(last < 0 ? count - 1 : last, now, rotation);
            if (rejected != null) { result.kind = ObservedPunch.Unknown; result.quality = 0; result.reason = rejected; }
            LastCompleted = result; Candidate = result; CompletedCount++;
            Phase = MotionPhase.Recovering; homeSince = quietSince = -1;
            Completed?.Invoke(result);
        }

        PunchObservation Evaluate(int last, float now, Quaternion rotation)
        {
            var result = new PunchObservation { kind = ObservedPunch.Unknown, reason = "Need more trajectory", speed = peakSpeed,
                duration = now - began, wristTurn = Quaternion.Angle(initialRotation, rotation) };
            if (last < 2 || result.duration < .06f) return result;
            float minX = 0, maxX = 0, minY = 0, maxY = 0, minInward = 0;
            int outer = 0, inner = 0; float maximumInward = float.NegativeInfinity;
            float path = 0;
            int jabEnd = Mathf.Min(last, peakForwardIndex);
            for (int i = 0; i <= last; i++)
            {
                var p = points[i] - neutral;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                result.forward = Mathf.Max(result.forward, p.z);
                float x = p.x * inwardSign;
                if (x < minInward) { minInward = x; outer = i; maximumInward = x; inner = i; }
                if (i >= outer && x >= maximumInward) { maximumInward = x; inner = i; }
                if (i > 0 && i <= jabEnd) path += Vector3.Distance(points[i], points[i - 1]);
            }
            result.inward = Mathf.Max(0, maximumInward - minInward);
            float arcLength = 0;
            for (int i = outer + 1; i <= inner; i++) arcLength += Vector3.Distance(points[i], points[i - 1]);
            float chord = Vector3.Distance(points[outer], points[inner]);
            result.arcRatio = chord > .01f ? arcLength / chord : 1;
            result.straightness = path > .01f ? Vector3.Distance(points[0], points[jabEnd]) / path : 0;
            if (result.forward < tuning.minimumForward) { result.reason = "Insufficient forward reach"; return result; }
            if (peakSpeed < tuning.startSpeed) { result.reason = "Too slow"; return result; }
            if (maxY - minY > result.forward * tuning.maximumVerticalRatio)
            { result.reason = "Vertical/guard motion: not jab or hook"; return result; }
            bool hook = -minInward >= tuning.minimumHookOutset && result.inward >= tuning.minimumHookSweep
                && result.arcRatio >= tuning.minimumHookArcRatio;
            bool jab = maxX - minX <= result.forward * tuning.maximumJabSideRatio && result.straightness >= .86f
                && -minInward < tuning.minimumHookOutset;
            if (hook && !jab)
            {
                result.kind = ObservedPunch.Hook;
                result.quality = Mathf.Clamp01(.65f + .25f * (result.inward / tuning.minimumHookSweep - 1) + (result.arcRatio - tuning.minimumHookArcRatio));
                result.reason = "Outward preparation + curved inward sweep";
            }
            else if (jab && !hook)
            {
                result.kind = ObservedPunch.Jab;
                result.quality = Mathf.Clamp01(result.straightness * (1 - .3f * ((maxX - minX) / result.forward)));
                result.reason = "Straight forward trajectory";
            }
            else result.reason = "Ambiguous diagonal/swipe: no confirmed punch";
            return result;
        }

        public static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        public static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w)
            && Quaternion.Dot(q, q) > .5f && Quaternion.Dot(q, q) < 1.5f;
    }
}
