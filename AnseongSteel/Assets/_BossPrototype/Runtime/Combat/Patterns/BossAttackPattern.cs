using UnityEngine;
namespace AnseongSteel.Bosses
{
    // Each component is one independently callable attack. No scheduling or chaining lives here.
    public abstract class BossAttackPattern : MonoBehaviour
    {
        public abstract BossAttackKind Kind {get;}
        public bool TryExecute()=>GetComponent<BossCombatController>().TryAttack(Kind);
        public virtual void Begin() { }
        public abstract void Tick(BossCombatController context,float progress);
    }
}
