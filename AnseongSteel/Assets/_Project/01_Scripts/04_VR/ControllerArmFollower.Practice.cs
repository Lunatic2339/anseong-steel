using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    public sealed partial class ControllerArmFollower
    {
        public bool usePracticePoseFrame;
        Quaternion practiceUpperFrame,practiceLowerFrame,practiceGripBody=Quaternion.identity,requestedGripBody=Quaternion.identity;
        Vector3 practiceAxis,practicePole,practiceBack;
        Vector3 practiceLowerAxis,practicePositionPole;
        Matrix4x4 practiceFilterInverse;
        float practiceRoll,practiceWristAlignment,practiceSwivel;
        bool practiceSolved,practiceFilterFrame,practiceGripInitialized,hasTrackedGrip;
        [Range(110,175)] public float practiceMaximumBend=160;
        public bool HasTrackedGripFrame=>hasTrackedGrip;
        public Quaternion RequestedFistWorldFrame {get;private set;}=Quaternion.identity;
        public Quaternion RequestedHandWorldRotation=>RequestedFistWorldFrame*Quaternion.Inverse(Quaternion.LookRotation(handForwardLocal,handUpLocal));
        public Quaternion SolvedFistWorldFrame=>HandFrame;
        public float GripForwardErrorDegrees {get;private set;}
        public float ForearmForwardErrorDegrees {get;private set;}
        public float GripBackErrorDegrees {get;private set;}
        public float GripPoleWeight {get;private set;}
        public float MinimumReachMetres {get;private set;}
        public bool InsideMinimumReach {get;private set;}
        public Vector3 RawPracticeTarget {get;private set;}
        public float GripPositionError=>hand?Vector3.Distance(RawPracticeTarget,hand.position):0;
        public void SetTrackedGripFrame(Quaternion bodyFistFrame)
        {if(ArmReachCalibration.ValidRotation(bodyFistFrame)){requestedGripBody=bodyFistFrame.normalized;hasTrackedGrip=true;}}
        public void ClearTrackedGripFrame(){hasTrackedGrip=false;}
        void ResetPracticeLagFrame(){practiceFilterInverse=robotFrame.worldToLocalMatrix;practiceFilterFrame=true;}

        void InitializePracticeFrames()
        {
            handForwardLocal.Normalize();
            handUpLocal=Vector3.ProjectOnPlane(handUpLocal,handForwardLocal).normalized;
            var a=(forearm.position-upper.position).normalized;
            var b=(hand.position-forearm.position).normalized;
            var normal=Vector3.Cross(a,b);
            if(normal.sqrMagnitude<.001f)normal=Vector3.Cross(robotFrame.TransformDirection(bendDirection),a);
            normal=PracticePerpendicular(a,normal,robotFrame.up);
            practiceUpperFrame=Quaternion.Inverse(Quaternion.LookRotation(a,normal))*upper.rotation;
            practiceLowerFrame=Quaternion.Inverse(Quaternion.LookRotation(b,PracticePerpendicular(b,normal,robotFrame.up)))*forearm.rotation;
            practiceSolved=false;practiceRoll=practiceSwivel=0;practiceGripInitialized=false;hasTrackedGrip=false;
            practiceWristAlignment=punchPose?1:0;ResetPracticeLagFrame();
        }
        static Vector3 PracticePerpendicular(Vector3 axis,Vector3 preferred,Vector3 fallback)
        {
            var p=Vector3.ProjectOnPlane(preferred,axis);
            if(p.sqrMagnitude<.0001f)p=Vector3.ProjectOnPlane(fallback,axis);
            if(p.sqrMagnitude<.0001f)p=Vector3.ProjectOnPlane(Vector3.right,axis);
            if(p.sqrMagnitude<.0001f)p=Vector3.ProjectOnPlane(Vector3.forward,axis);
            return p.normalized;
        }
        void FollowPracticePose(Vector3 delta,Quaternion rotationDelta)
        {
            float dt=Mathf.Max(0,Time.deltaTime),total=upperLength+forearmLength;
            var shoulder=robotFrame.InverseTransformPoint(upper.position);
            float length=total/robotFrame.lossyScale.x;
            float full=ArmReachCalibration.DistanceForBend(upperLength,forearmLength,ArmReachCalibration.FullReachBendDegrees)/total;
            float limit=ArmReachCalibration.DistanceForBend(upperLength,forearmLength,ArmReachCalibration.OverreachBendDegrees)/total;
            var localTarget=PersonalReachActive?personalReach.MapPractice(delta,shoulder,neutralTarget,length,full,limit)
                :MapWorkspace(shoulder,neutralTarget,delta,length,motionScale,transverseScale);
            RequestedTarget=robotFrame.TransformPoint(localTarget);
            RawPracticeTarget=RequestedTarget;
            if(!practiceGripInitialized){practiceGripBody=Quaternion.Inverse(robotFrame.rotation)*HandFrame;practiceGripInitialized=true;}
            var targetGrip=hasTrackedGrip?requestedGripBody:practiceGripBody;
            RequestedFistWorldFrame=robotFrame.rotation*targetGrip;
            // Only the robot's output is limited; no input/guard/reach rotation
            // is subtracted. The frame is body-relative and never head-following.
            practiceGripBody=Quaternion.RotateTowards(practiceGripBody,targetGrip,240*dt);
            var gripWorld=robotFrame.rotation*practiceGripBody;
            var wantedForward=gripWorld*Vector3.forward;
            var wantedBack=gripWorld*Vector3.up;
            // Same latest target drives the visible joints and contact volumes.
            if(targetSpeedLimit>0)
            {
                if(hasFilteredTarget&&practiceFilterFrame)
                {
                    // Carry cached pose/velocity with the body, not a fixed world
                    // point. Translation/turning is not an extra hand movement.
                    filteredTarget=robotFrame.TransformPoint(practiceFilterInverse.MultiplyPoint3x4(filteredTarget));
                    targetVelocity=robotFrame.TransformVector(practiceFilterInverse.MultiplyVector(targetVelocity));
                }
                if(!hasFilteredTarget){filteredTarget=hand.position;hasFilteredTarget=true;}
                filteredTarget=targetSmoothSeconds>0?Vector3.SmoothDamp(filteredTarget,RequestedTarget,ref targetVelocity,targetSmoothSeconds,targetSpeedLimit,dt)
                    :Vector3.MoveTowards(filteredTarget,RequestedTarget,targetSpeedLimit*dt);
                RequestedTarget=filteredTarget;
            }
            ResetPracticeLagFrame();
            var offset=RequestedTarget-upper.position;
            var lastAxis=robotFrame.TransformDirection(practiceAxis);
            var lastPole=robotFrame.TransformDirection(practicePole);
            var lastBack=robotFrame.TransformDirection(practiceBack);
            MinimumReachMetres=Mathf.Max(Mathf.Abs(upperLength-forearmLength)+.005f,ArmReachCalibration.DistanceForBend(upperLength,forearmLength,practiceMaximumBend));
            InsideMinimumReach=offset.magnitude<MinimumReachMetres;
            float innerBand=MinimumReachMetres+total*.04f;
            var axis=offset.sqrMagnitude>.000001f?offset.normalized:(practiceSolved?lastAxis:robotFrame.forward);
            if(practiceSolved)
            {
                // The wrist cannot cross the unequal-length inner sphere. Hold
                // its last ray inside the band, then recover along a bounded arc.
                if(offset.magnitude<innerBand)axis=lastAxis;
                else axis=RotatePracticeDirection(lastAxis,axis,lastPole,180*dt);
            }
            float distance=Mathf.Clamp(offset.magnitude,MinimumReachMetres,total*(1-.00001f));
            var goal=upper.position+axis*distance;
            float extension=Mathf.InverseLerp(.65f,.98f,distance/total);
            float raised=Mathf.Max(extension,elbowLift);
            var preferred=robotFrame.TransformDirection(new Vector3(Mathf.Sign(bendDirection.x),Mathf.Lerp(-.65f,0,raised),Mathf.Lerp(-.1f,0,raised)));
            var transportedPosition=practiceSolved?TransportPracticeVector(lastAxis,axis,robotFrame.TransformDirection(practicePositionPole)):preferred;
            transportedPosition=PracticePerpendicular(axis,transportedPosition,robotFrame.up);
            var positionPole=Vector3.ProjectOnPlane(preferred,axis);
            if(positionPole.sqrMagnitude<.04f)positionPole=transportedPosition;else positionPole.Normalize();
            if(practiceSolved)
            {
                positionPole=RotatePracticeDirection(transportedPosition,positionPole,axis,90*dt);
            }
            float along=(upperLength*upperLength-forearmLength*forearmLength+distance*distance)/(2*distance);
            float height=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
            // E=C+h*p. Forearm=(W-C-h*p)/L2. For a desired forearm direction f,
            // -project(f,axis) is the closest point on this exact elbow circle.
            var orientationPole=-Vector3.ProjectOnPlane(wantedForward,axis);
            GripPoleWeight=hasTrackedGrip?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.05f,.3f,orientationPole.magnitude))
                *Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.10f,height/total))
                *Mathf.SmoothStep(0,1,Mathf.InverseLerp(MinimumReachMetres,MinimumReachMetres+total*.12f,distance)):0;
            float targetSwivel=orientationPole.sqrMagnitude>.0001f?BoundPracticeSwivel(Vector3.SignedAngle(positionPole,orientationPole.normalized,axis),practiceSwivel)*GripPoleWeight:0;
            // A bounded scalar cannot accumulate full turns or jump between
            // opposite elbow branches when the projected grip crosses zero.
            practiceSwivel=Mathf.MoveTowards(practiceSwivel,targetSwivel,180*dt);
            var pole=Quaternion.AngleAxis(practiceSwivel,axis)*positionPole;
            var elbow=upper.position+axis*along+pole*height;
            var plane=Vector3.Cross(pole,axis).normalized;
            var upperDirection=(elbow-upper.position).normalized;
            var lowerDirection=(goal-elbow).normalized;
            // Absolute bind-frame reconstruction replaces accumulated FromTo/roll.
            upper.rotation=Quaternion.LookRotation(upperDirection,plane)*practiceUpperFrame;
            forearm.rotation=Quaternion.LookRotation(lowerDirection,plane)*practiceLowerFrame;
            hand.localRotation=LockedWristRotation;
            var back=Vector3.ProjectOnPlane(robotFrame.up,lowerDirection);
            var carriedBack=practiceSolved?TransportPracticeVector(robotFrame.TransformDirection(practiceLowerAxis),lowerDirection,lastBack):robotFrame.up;
            if(back.sqrMagnitude<.04f)back=PracticePerpendicular(lowerDirection,carriedBack,robotFrame.right);else back.Normalize();
            if(practiceSolved)back=Vector3.RotateTowards(PracticePerpendicular(lowerDirection,carriedBack,back),back,180*Mathf.Deg2Rad*dt,0).normalized;
            float neutralRoll=Vector3.SignedAngle(Vector3.ProjectOnPlane(HandFrame*Vector3.up,lowerDirection),back,lowerDirection);
            forearm.rotation=Quaternion.AngleAxis(neutralRoll,lowerDirection)*forearm.rotation;
            hand.localRotation=LockedWristRotation;
            // Position calibration is NOT an orientation calibration. Preserve
            // grip dorsal/thumbnail direction with the smallest swing needed by
            // the feasible forearm axis, rather than replacing it with body up.
            var desired=TransportPracticeVector(wantedForward,lowerDirection,wantedBack);
            desired=PracticePerpendicular(lowerDirection,desired,back);
            var current=Quaternion.AngleAxis(practiceRoll,lowerDirection)*back;
            float step=Mathf.Clamp(Vector3.SignedAngle(current,desired,lowerDirection),-180*dt,180*dt);
            practiceRoll=Mathf.Clamp(practiceRoll+step,-165,165);
            forearm.rotation=Quaternion.AngleAxis(practiceRoll,lowerDirection)*forearm.rotation;
            hand.localRotation=LockedWristRotation;
            // Grip strengthens wrist/forearm alignment. Outside attack, allow a
            // bounded wrist swing; release does not snap the hand to a new pose.
            practiceWristAlignment=Mathf.MoveTowards(practiceWristAlignment,punchPose?1:0,6*dt);
            float swing=(1-practiceWristAlignment)*60*Mathf.Deg2Rad;
            var handForward=Vector3.RotateTowards(lowerDirection,wantedForward,swing,0).normalized;
            var handBack=PracticePerpendicular(handForward,HandFrame*Vector3.up,back);
            var gripBack=PracticePerpendicular(handForward,TransportPracticeVector(wantedForward,handForward,wantedBack),handBack);
            float wristRoll=Mathf.Clamp(Vector3.SignedAngle(handBack,gripBack,handForward),-30*(1-practiceWristAlignment),30*(1-practiceWristAlignment));
            handBack=Quaternion.AngleAxis(wristRoll,handForward)*handBack;
            hand.rotation=Quaternion.LookRotation(handForward,handBack)*Quaternion.Inverse(Quaternion.LookRotation(handForwardLocal,handUpLocal));
            practiceAxis=robotFrame.InverseTransformDirection(axis);practicePole=robotFrame.InverseTransformDirection(pole);
            practiceBack=robotFrame.InverseTransformDirection(back);practiceLowerAxis=robotFrame.InverseTransformDirection(lowerDirection);
            practicePositionPole=robotFrame.InverseTransformDirection(positionPole);practiceSolved=true;
            GripForwardErrorDegrees=Vector3.Angle(HandFrame*Vector3.forward,RequestedFistWorldFrame*Vector3.forward);
            ForearmForwardErrorDegrees=Vector3.Angle(lowerDirection,RequestedFistWorldFrame*Vector3.forward);
            GripBackErrorDegrees=Vector3.Angle(HandFrame*Vector3.up,RequestedFistWorldFrame*Vector3.up);
            ReachError=Vector3.Distance(RequestedTarget,goal);TargetError=Vector3.Distance(goal,hand.position);
            if(targetMarker)targetMarker.position=RequestedTarget;if(actualMarker)actualMarker.position=hand.position;
        }
        static Vector3 TransportPracticeVector(Vector3 from,Vector3 to,Vector3 value)
        {
            if(Vector3.Dot(from,to)<-.999f)return Quaternion.AngleAxis(180,PracticePerpendicular(from,value,Vector3.up))*value;
            return Quaternion.FromToRotation(from,to)*value;
        }
        static Vector3 RotatePracticeDirection(Vector3 from,Vector3 to,Vector3 turnAxis,float maxDegrees)
        {
            if(Vector3.Dot(from,to)<-.999f)return Quaternion.AngleAxis(Mathf.Min(180,maxDegrees),PracticePerpendicular(from,turnAxis,Vector3.up))*from;
            return Vector3.RotateTowards(from,to,maxDegrees*Mathf.Deg2Rad,0).normalized;
        }
        static float BoundPracticeSwivel(float angle,float previous)
        {
            if(Mathf.Abs(angle)<=105)return angle;
            float positive=Mathf.Abs(Mathf.DeltaAngle(angle,105)),negative=Mathf.Abs(Mathf.DeltaAngle(angle,-105));
            // Equally good opposite endpoints must not alternate from tiny
            // noise around +/-180 degrees.
            if(Mathf.Abs(positive-negative)<20&&Mathf.Abs(previous)>1)return previous>0?105:-105;
            return positive<=negative?105:-105;
        }
        public void ReconcilePracticeAcceptedPose()
        {
            if(!usePracticePoseFrame||!IsReady)return;
            CancelTargetLag();
            var ray=hand.position-upper.position;
            if(ray.sqrMagnitude<.000001f)return;
            var axis=ray.normalized;
            var actualPole=Vector3.ProjectOnPlane(forearm.position-upper.position,axis);
            var positionPole=PracticePerpendicular(axis,robotFrame.TransformDirection(practicePositionPole),actualPole);
            if(actualPole.sqrMagnitude>.000001f)practiceSwivel=Mathf.Clamp(Vector3.SignedAngle(positionPole,actualPole.normalized,axis),-105,105);
            practiceAxis=robotFrame.InverseTransformDirection(axis);
            practicePole=robotFrame.InverseTransformDirection(actualPole.sqrMagnitude>.000001f?actualPole.normalized:positionPole);
            practicePositionPole=robotFrame.InverseTransformDirection(positionPole);
            var lower=(hand.position-forearm.position).normalized;
            var neutral=PracticePerpendicular(lower,robotFrame.up,robotFrame.TransformDirection(practiceBack));
            var previousNeutral=PracticePerpendicular(lower,TransportPracticeVector(robotFrame.TransformDirection(practiceLowerAxis),lower,robotFrame.TransformDirection(practiceBack)),neutral);
            neutral=RotatePracticeDirection(previousNeutral,neutral,lower,180*Mathf.Max(0,Time.deltaTime));
            practiceRoll=Mathf.Clamp(Vector3.SignedAngle(neutral,PracticePerpendicular(lower,HandFrame*Vector3.up,neutral),lower),-165,165);
            practiceLowerAxis=robotFrame.InverseTransformDirection(lower);practiceBack=robotFrame.InverseTransformDirection(neutral);
            practiceGripBody=Quaternion.Inverse(robotFrame.rotation)*HandFrame;practiceGripInitialized=true;
            GripForwardErrorDegrees=Vector3.Angle(HandFrame*Vector3.forward,RequestedFistWorldFrame*Vector3.forward);
            GripBackErrorDegrees=Vector3.Angle(HandFrame*Vector3.up,RequestedFistWorldFrame*Vector3.up);
            ForearmForwardErrorDegrees=Vector3.Angle(lower,RequestedFistWorldFrame*Vector3.forward);
        }
    }
}
