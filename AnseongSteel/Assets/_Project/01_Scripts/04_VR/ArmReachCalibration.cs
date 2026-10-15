using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    public enum ShoulderReference { HeadRelativeEstimate, AuxiliaryControlPoint }

    // User-confirmed control-point workspace, not anatomical bone measurements.
    public sealed class ArmReachCalibration
    {
        public bool HasGuard { get; private set; }
        public bool IsComplete { get; private set; }
        public Vector3 Shoulder { get; private set; }
        public Vector3 Guard { get; private set; }
        public Quaternion GuardRotation { get; private set; }
        public float EffectiveReach { get; private set; }
        public Vector3 ReachPoint { get; private set; }
        public Quaternion ReachRotation { get; private set; }=Quaternion.identity;
        public ShoulderReference Reference { get; private set; }
        public float GuardRatio => IsComplete ? Vector3.Distance(Guard, Shoulder) / EffectiveReach : 0;

        public void Reset()
        {
            HasGuard = IsComplete = false;
            Shoulder = Guard = Vector3.zero;
            GuardRotation = Quaternion.identity;
            EffectiveReach = 0;
            ReachPoint=Vector3.zero;
            ReachRotation=Quaternion.identity;
            Reference = ShoulderReference.HeadRelativeEstimate;
        }

        public bool CaptureGuard(Vector3 shoulder, Vector3 controlPoint, Quaternion rotation,
            ShoulderReference reference, out string reason)
        {
            reason = "";
            if (!PunchTrajectoryRecognizer.Finite(shoulder) || !PunchTrajectoryRecognizer.Finite(controlPoint)
                || !ValidRotation(rotation)) { reason = "A valid tracked pose is required."; return false; }
            float distance = Vector3.Distance(shoulder, controlPoint);
            if (distance < .04f || distance > 1.4f)
            { reason = "Check the shoulder estimate and hold a comfortable bent-arm guard."; return false; }
            Shoulder = shoulder; Guard = controlPoint; GuardRotation = rotation.normalized;
            Reference = reference; HasGuard = true; IsComplete = false; EffectiveReach = 0;ReachPoint=Vector3.zero;ReachRotation=Quaternion.identity;
            return true;
        }

        public bool CaptureReach(Vector3 controlPoint, out string reason)
            =>CaptureReach(controlPoint,GuardRotation,out reason);

        public bool CaptureReach(Vector3 controlPoint,Quaternion rotation,out string reason)
        {
            reason = "";
            if (!HasGuard) { reason = "Capture your comfortable guard first."; return false; }
            if (!PunchTrajectoryRecognizer.Finite(controlPoint)||!ValidRotation(rotation)) { reason = "A valid tracked pose is required."; return false; }
            float reach = Vector3.Distance(controlPoint, Shoulder);
            float guard = Vector3.Distance(Guard, Shoulder);
            if (reach < .2f || reach > 1.4f || reach - guard < .08f || guard / reach > .85f)
            { reason = "Extend comfortably farther than guard; check the shoulder reference if necessary."; return false; }
            EffectiveReach = reach;ReachPoint=controlPoint;ReachRotation=rotation.normalized; IsComplete = true; return true;
        }

        public static bool ValidRotation(Quaternion q)
        {
            float length = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
            return PunchTrajectoryRecognizer.Finite(q) && length > .25f && length < 4;
        }

        // Guard and full reach are radial anchors. Direction stays a continuous
        // shoulder-to-control-point vector, including sideways/backward motion.
        public const float FullReachBendDegrees = 3f;
        public const float OverreachBendDegrees = 2f;
        public static float DistanceForBend(float upper, float lower, float bendDegrees)
            => Mathf.Sqrt(upper*upper+lower*lower+2*upper*lower*Mathf.Cos(bendDegrees*Mathf.Deg2Rad));

        public Vector3 Map(Vector3 delta, Vector3 modelShoulder, Vector3 modelGuard, float modelLength,
            float fullRatio = .9996573f, float limitRatio = .9998477f)
        {
            if (!IsComplete || !PunchTrajectoryRecognizer.Finite(delta) || modelLength <= .01f) return modelGuard;
            Vector3 physicalGuard = Guard - Shoulder, current = physicalGuard + delta;
            if (!float.IsFinite(current.magnitude)) return modelGuard;
            Quaternion directionMap = Quaternion.FromToRotation(physicalGuard, modelGuard - modelShoulder);
            float radius = MapRadius(current.magnitude / EffectiveReach, GuardRatio,
                Vector3.Distance(modelGuard, modelShoulder) / modelLength, fullRatio, limitRatio);
            Vector3 direction = current.sqrMagnitude > 1e-10f ? current.normalized : physicalGuard.normalized;
            return modelShoulder + directionMap * direction * (radius * modelLength);
        }

        public static float MapRadius(float ratio, float guardRatio, float modelGuardRatio,
            float fullRatio = .9996573f, float limitRatio = .9998477f)
        {
            guardRatio = Mathf.Clamp(guardRatio, .02f, .85f);
            modelGuardRatio = Mathf.Clamp(modelGuardRatio, .01f, .9f);
            ratio = Mathf.Max(0, ratio);
            float belowSlope = modelGuardRatio / guardRatio;
            fullRatio = Mathf.Clamp(fullRatio, .91f, .99985f);
            limitRatio = Mathf.Clamp(limitRatio, fullRatio + .00001f, .99995f);
            float aboveSlope = (fullRatio - modelGuardRatio) / (1 - guardRatio);
            float guardSlope = Mathf.Min(belowSlope, aboveSlope);
            float endSlope = Mathf.Min(.2f, aboveSlope);
            if (ratio <= guardRatio)
                return Hermite(ratio / guardRatio, 0, modelGuardRatio, guardRatio * belowSlope, guardRatio * guardSlope);
            if (ratio < 1)
            {
                float width = 1 - guardRatio;
                return Hermite((ratio - guardRatio) / width, modelGuardRatio, fullRatio, width * guardSlope, width * endSlope);
            }
            float tail = limitRatio - fullRatio;
            return fullRatio + tail * (1 - Mathf.Exp(-(ratio - 1) / (tail / endSlope)));
        }

        // Practice has two directional anchors: the user's own guard and saved
        // reach. Full reach means shoulder-height robot-forward, not the bent
        // model guard direction. Guard height remains independently calibrated.
        public float ReachBlend(Vector3 delta)
        {
            if(!IsComplete)return 0;
            float guard=Vector3.Distance(Guard,Shoulder);
            float current=(Guard-Shoulder+delta).magnitude;
            return Mathf.SmoothStep(0,1,Mathf.Clamp01((current-guard)/Mathf.Max(.001f,EffectiveReach-guard)));
        }
        public Vector3 MapPractice(Vector3 delta,Vector3 modelShoulder,Vector3 modelGuard,float modelLength,float fullRatio,float limitRatio)
        {
            if(!IsComplete||!PunchTrajectoryRecognizer.Finite(delta)||modelLength<=.01f)return modelGuard;
            var current=Guard-Shoulder+delta;
            var mapping=PracticeDirectionMap(delta,modelShoulder,modelGuard);
            float radius=MapRadius(current.magnitude/EffectiveReach,GuardRatio,(modelGuard-modelShoulder).magnitude/modelLength,fullRatio,limitRatio);
            return modelShoulder+mapping*(current.sqrMagnitude>1e-10f?current.normalized:(Guard-Shoulder).normalized)*(radius*modelLength);
        }
        public Quaternion PracticeDirectionMap(Vector3 delta,Vector3 modelShoulder,Vector3 modelGuard)
        {
            if(!IsComplete)return Quaternion.identity;
            var guardMap=DirectionFrame(modelGuard-modelShoulder)*Quaternion.Inverse(DirectionFrame(Guard-Shoulder));
            var reachMap=DirectionFrame(Vector3.forward)*Quaternion.Inverse(DirectionFrame(ReachPoint-Shoulder));
            return Quaternion.Slerp(guardMap,reachMap,ReachBlend(delta));
        }
        static Quaternion DirectionFrame(Vector3 direction)
        {
            var forward=direction.normalized;var up=Vector3.ProjectOnPlane(Vector3.up,forward);
            if(up.sqrMagnitude<.001f)up=Vector3.ProjectOnPlane(Vector3.right,forward);
            return Quaternion.LookRotation(forward,up.normalized);
        }

        static float Hermite(float t, float a, float b, float ma, float mb)
        {
            float t2 = t*t, t3 = t2*t;
            return (2*t3 - 3*t2 + 1)*a + (t3 - 2*t2 + t)*ma + (-2*t3 + 3*t2)*b + (t3 - t2)*mb;
        }
    }
}
