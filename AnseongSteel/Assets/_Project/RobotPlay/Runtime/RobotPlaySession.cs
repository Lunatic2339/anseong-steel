using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    public struct RobotSnapshot : INetworkSerializable, IEquatable<RobotSnapshot>
    {
        public int bossHealth,robotHealth,combo,next,hits,phase,attackSide;
        public bool both,leftGuard,rightGuard;
        public Vector3 left,right,body;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref bossHealth);s.SerializeValue(ref robotHealth);s.SerializeValue(ref combo);
            s.SerializeValue(ref next);s.SerializeValue(ref hits);s.SerializeValue(ref phase);s.SerializeValue(ref attackSide);
            s.SerializeValue(ref both);s.SerializeValue(ref leftGuard);s.SerializeValue(ref rightGuard);
            s.SerializeValue(ref left);s.SerializeValue(ref right);s.SerializeValue(ref body);
        }
        public bool Equals(RobotSnapshot other) => bossHealth==other.bossHealth&&robotHealth==other.robotHealth&&combo==other.combo&&next==other.next&&hits==other.hits&&phase==other.phase&&attackSide==other.attackSide&&both==other.both&&leftGuard==other.leftGuard&&rightGuard==other.rightGuard&&left==other.left&&right==other.right&&body==other.body;
    }

    [DefaultExecutionOrder(500)]
    public sealed class RobotPlaySession : NetworkBehaviour
    {
        public CombatTuning tuning=new CombatTuning();
        public MechaVRBossContact contact;
        public MechaVRBossTarget target;
        public Transform robotRoot;
        public TextMesh statusText;
        public Renderer targetMarker;
        public bool trainingWindow=true;
        public float telegraphSeconds=2f,recoverySeconds=3.8f,guardBonusSeconds=1.5f;
        public float movementSpeed=.6f;
        public string hostAddress="127.0.0.1";
        public ushort port=7777;
        public RobotCombat Combat { get; private set; }
        public RobotSnapshot State => IsSpawned ? replicated.Value : offline;
        public string LastFeedback { get; private set; }="Ready";
        public event Action<FeedbackKind,PilotSide,float> Feedback;
        readonly NetworkVariable<RobotSnapshot> replicated=new NetworkVariable<RobotSnapshot>();
        RobotSnapshot offline;
        readonly Vector3[] input=new Vector3[2];
        readonly bool[] boost=new bool[2];
        readonly float[] received={-100,-100};
        readonly uint[] sequences=new uint[2];
        readonly Vector3[] accepted=new Vector3[2];
        float phaseEnd,nextPublish;
        Vector2 move;
        int phase,attackSide,attackCount;
        bool both;
        ulong rightClient=ulong.MaxValue;
        public bool NetworkActive => NetworkManager.Singleton && NetworkManager.Singleton.IsListening;
        public bool Authority => !NetworkActive || (IsSpawned&&IsServer);
        public int LocalSide => NetworkActive && !NetworkManager.Singleton.IsHost ? 1 : 0;
        public static Vector3 Neutral(int side) => new Vector3(side==0?-.65f:.65f,1.35f,.45f);
        void Awake()
        {
            Combat=new RobotCombat(tuning);Combat.Feedback+=PublishFeedback;
            if(contact)contact.enabled=false;
            if(target)target.onHit.AddListener(OnContact);
            phaseEnd=Time.time+telegraphSeconds;
        }
        public override void OnNetworkSpawn()
        {
            if(IsServer)
            {
                ResetSimulation();
                NetworkManager.OnClientConnectedCallback+=Connected;
                NetworkManager.OnClientDisconnectCallback+=Disconnected;
                foreach(var id in NetworkManager.ConnectedClientsIds)Connected(id);
            }
        }
        public override void OnNetworkDespawn()
        {
            if(NetworkManager)
            {
                NetworkManager.OnClientConnectedCallback-=Connected;
                NetworkManager.OnClientDisconnectCallback-=Disconnected;
            }
            rightClient=ulong.MaxValue;ResetSimulation();
        }
        void Connected(ulong id) { if(id!=NetworkManager.ServerClientId && rightClient==ulong.MaxValue) { rightClient=id;sequences[1]=0; } }
        void Disconnected(ulong id) { if(id==rightClient) { rightClient=ulong.MaxValue;received[1]=-100;Combat.Invalidate(1); } }
        public void StartNetwork(bool host)
        {
            var manager=NetworkManager.Singleton;if(!manager || manager.IsListening)return;
            var transport=manager.GetComponent<UnityTransport>();transport.SetConnectionData(hostAddress,port,host?"0.0.0.0":null);
            manager.NetworkConfig.ConnectionApproval=true;
            if(host)
            {
                manager.ConnectionApprovalCallback=(request,response)=>
                {response.Approved=manager.ConnectedClientsIds.Count<2;response.CreatePlayerObject=false;response.Pending=false;response.Reason="Two pilot prototype is full";};
                manager.StartHost();
            }
            else manager.StartClient();
        }
        public void StopNetwork() { if(NetworkActive)NetworkManager.Singleton.Shutdown(); }
        public void Submit(int side,Vector3 offset,bool boostEdge,Vector2 movement,uint sequence)
        {
            if(NetworkActive)
            {
                if(!IsSpawned || side!=LocalSide)return;
                SubmitRpc(side,offset,boostEdge,movement,sequence);
            }
            else Accept(side,offset,boostEdge,movement,sequence);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        void SubmitRpc(int side,Vector3 offset,bool boostEdge,Vector2 movement,uint sequence,RpcParams rpc=default)
        {
            ulong sender=rpc.Receive.SenderClientId;
            if(!Authorized(side,sender,NetworkManager.ServerClientId,rightClient))return;
            Accept(side,offset,boostEdge,movement,sequence);
        }
        public static bool Authorized(int side,ulong sender,ulong host,ulong right) => side==0?sender==host:side==1&&right!=ulong.MaxValue&&sender==right;
        void Accept(int side,Vector3 offset,bool edge,Vector2 movement,uint sequence)
        {
            if(side<0||side>1||!RobotCombat.Finite(offset)||offset.magnitude>2.5f||!float.IsFinite(movement.x)||!float.IsFinite(movement.y)||sequence<=sequences[side])return;
            float elapsed=Time.time-received[side];
            if(elapsed<.008f)return;
            if(elapsed<.25f && Vector3.Distance(accepted[side],offset)>Mathf.Max(.15f,elapsed*12f))return;
            sequences[side]=sequence;accepted[side]=offset;input[side]=offset;boost[side]|=edge;received[side]=Time.time;
            if(side==0)move=Vector2.ClampMagnitude(movement,1); // Explicit provisional movement ownership.
        }
        void LateUpdate()
        {
            if(!contact||!robotRoot||!target)return;
            if(Authority)
            {
                float now=Time.time;Combat.Tick(now);
                for(int side=0;side<2;side++)
                {
                    if(now-received[side]>.25f)
                    { if(Combat.arms[side].initialized)Combat.Invalidate(side);input[side]=Vector3.zero;boost[side]=false;if(side==0)move=Vector2.zero; }
                    else if(received[side]>Combat.arms[side].lastSample)Combat.Sample(side,input[side],boost[side],received[side]);
                    boost[side]=false;Pose(side,input[side]);
                }
                if(Combat.RobotHealth>0&&Combat.BossHealth>0)
                {
                    Vector3 p=robotRoot.position+new Vector3(move.x,0,move.y)*movementSpeed*Time.deltaTime;
                    p.x=Mathf.Clamp(p.x,-.7f,.7f);p.z=Mathf.Clamp(p.z,-1f,.15f);robotRoot.position=p;
                    UpdateBoss(now);
                }
                contact.ConstrainPose();
                offline=new RobotSnapshot {bossHealth=Combat.BossHealth,robotHealth=Combat.RobotHealth,combo=Combat.ComboStep,next=Combat.NextSide,hits=Combat.HitCount,phase=phase,attackSide=attackSide,both=both,leftGuard=Combat.arms[0].guarding,rightGuard=Combat.arms[1].guarding,left=input[0],right=input[1],body=robotRoot.position};
                if(IsSpawned&&IsServer&&now>=nextPublish){replicated.Value=offline;nextPublish=now+.033f;}
            }
            else if(IsSpawned)
            {var s=State;robotRoot.position=s.body;Pose(0,s.left);Pose(1,s.right);contact.ConstrainPose();}
            UpdateDisplay();
        }
        void UpdateBoss(float now)
        {
            if(trainingWindow){phase=1;return;}
            if(now<phaseEnd)return;
            if(phase==0)
            {
                bool inRange=robotRoot.position.z>-.55f;
                bool defended=!inRange || Combat.ReceiveAttack(attackSide,both,12);
                phase=1;phaseEnd=now+recoverySeconds+(defended?guardBonusSeconds:0);
            }
            else {phase=0;attackCount++;attackSide=attackCount%2;both=attackCount%3==2;phaseEnd=now+telegraphSeconds;}
        }
        void OnContact()
        {
            if(!Authority)return;
            int side=target.LastArm=="L"?0:target.LastArm=="R"?1:-1;
            if(side<0||!contact.arms[side].ContactPart.Contains("Fist"))return;
            Combat.Hit(side,Time.time,trainingWindow||phase==1);
        }
        public void ResetSimulation()
        {
            if(Combat==null)return;
            Combat.Reset();phase=0;attackCount=0;attackSide=0;both=false;phaseEnd=Time.time+telegraphSeconds;
            for(int i=0;i<2;i++){received[i]=-100;input[i]=Vector3.zero;boost[i]=false;sequences[i]=0;}
            move=Vector2.zero;if(robotRoot)robotRoot.position=Vector3.zero;if(contact)contact.ResetContactState();
        }
        // Two rigid segments. The existing contact solver restricts these requested rotations.
        public void Pose(int side,Vector3 offset)
        {
            var arm=contact.arms[side];Vector3 shoulder=arm.upperArm.position;
            Vector3 goal=robotRoot.TransformPoint(Neutral(side)+offset);
            Vector3 delta=goal-shoulder;float distance=Mathf.Clamp(delta.magnitude,.08f,1.89f);
            Vector3 direction=delta.normalized;if(direction.sqrMagnitude<.5f)direction=Vector3.forward;
            Vector3 bend=Vector3.ProjectOnPlane(robotRoot.TransformDirection(new Vector3(side==0?-1:1,-1,0)),direction).normalized;
            if(bend.sqrMagnitude<.5f)bend=Vector3.up;
            Vector3 elbow=shoulder+direction*(distance*.5f)+bend*Mathf.Sqrt(.95f*.95f-distance*distance*.25f);
            arm.upperArm.rotation=Quaternion.LookRotation(elbow-shoulder,robotRoot.up);
            arm.forearm.rotation=Quaternion.LookRotation(shoulder+direction*distance-elbow,robotRoot.up);
            arm.hand.rotation=robotRoot.rotation;
        }
        void PublishFeedback(FeedbackKind kind,PilotSide side,float strength)
        {
            if(IsSpawned&&IsServer)FeedbackRpc((int)kind,(int)side,strength);
            else ReceiveFeedback(kind,side,strength);
        }
        [Rpc(SendTo.Everyone)] void FeedbackRpc(int kind,int side,float strength) => ReceiveFeedback((FeedbackKind)kind,(PilotSide)side,strength);
        void ReceiveFeedback(FeedbackKind kind,PilotSide side,float strength)
        {LastFeedback=$"{side} {kind} {strength:0}";Feedback?.Invoke(kind,side,strength);}
        void UpdateDisplay()
        {
            var s=State;
            if(statusText)statusText.text=$"ROBOT PLAY / {(NetworkActive?(LocalSide==0?"HOST L":"CLIENT R"):"OFFLINE")}\nBOSS {s.bossHealth} / ROBOT {s.robotHealth}\nCOMBO {s.combo}/2   NEXT {(s.next<0?"-":s.next==0?"LEFT":"RIGHT")}\nGUARD L:{s.leftGuard} R:{s.rightGuard}\n{(s.phase==1?"ATTACK WINDOW":s.both?"INCOMING: BOTH GUARD":s.attackSide==0?"INCOMING: LEFT":"INCOMING: RIGHT")}\n{LastFeedback}";
            if(targetMarker)targetMarker.material.color=s.bossHealth<=0?Color.gray:s.phase==1?new Color(.1f,.8f,.5f):new Color(1,.3f,.12f);
        }
    }
}
