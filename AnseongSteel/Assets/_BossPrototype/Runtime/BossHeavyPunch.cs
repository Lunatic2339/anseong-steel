using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnseongSteel.Bosses
{
    [DisallowMultipleComponent]
    public class BossHeavyPunch : MonoBehaviour
    {
        public enum AttackState
        {
            Idle,
            Windup,
            Recovery
        }

        [SerializeField] private Transform hitPoint;
        [SerializeField, Min(0.01f)] private float hitRadius = 0.75f;
        [SerializeField, Min(0.01f)] private float windupSeconds = 1.5f;
        [SerializeField, Min(0.01f)] private float recoverySeconds = 0.8f;
        [SerializeField] private LayerMask targetLayers = ~0;

        // 진행 중인 공격의 설정입니다. Inspector 변경은 다음 공격부터 적용합니다.
        private Transform activeHitPoint;
        private float activeRadius;
        private float impactTime;
        private float finishTime;
        private int activeLayers;
        private float elapsed;

        public AttackState State { get; private set; }
        public bool IsAttacking => State != AttackState.Idle;

        // 타격 순간에 한 번 전달합니다. 빈 배열이면 빗나간 것입니다.
        // Collider 목록일 뿐이며, 데미지나 방어 성공을 확정하지 않습니다.
        public event Action<Collider[]> ImpactChecked;

        // true: 새 공격 시작. false: 비활성화, 공격 중, 또는 설정 오류로 거절.
        public bool TryHeavyPunch()
        {
            if (!isActiveAndEnabled || IsAttacking) return false;

            if (hitPoint == null || !hitPoint.IsChildOf(transform))
            {
                Debug.LogWarning("Hit Point에 보스 루트 아래의 Transform을 연결하세요.", this);
                return false;
            }

            if (!IsPositiveFinite(hitRadius) ||
                !IsPositiveFinite(windupSeconds) ||
                !IsPositiveFinite(recoverySeconds) ||
                !IsPositiveFinite(windupSeconds + recoverySeconds))
            {
                Debug.LogWarning("공격 반경과 시간은 0보다 큰 유한한 수여야 합니다.", this);
                return false;
            }

            activeHitPoint = hitPoint;
            activeRadius = hitRadius;
            impactTime = windupSeconds;
            finishTime = windupSeconds + recoverySeconds;
            activeLayers = targetLayers.value;
            elapsed = 0f;
            State = AttackState.Windup;
            return true;
        }

        public void CancelAttack()
        {
            State = AttackState.Idle;
            elapsed = 0f;
            activeHitPoint = null;
        }

        private void Update()
        {
            if (!IsAttacking) return;

            if (activeHitPoint == null)
            {
                CancelAttack();
                return;
            }

            elapsed += Time.deltaTime;

            if (State == AttackState.Windup && elapsed >= impactTime)
            {
                // 먼저 상태를 바꿔 같은 공격에서 판정이 반복되지 않게 합니다.
                State = AttackState.Recovery;
                Collider[] hits = CollectHitCandidates();
                ImpactChecked?.Invoke(hits);

                // 알림을 받은 쪽이 취소하거나 새 공격을 시작해도
                // 이 프레임에서 그 새 요청까지 종료하지 않습니다.
                return;
            }

            if (State == AttackState.Recovery && elapsed >= finishTime)
            {
                CancelAttack();
            }
        }

        private Collider[] CollectHitCandidates()
        {
            // Transform으로 움직인 Collider의 현재 위치를 물리 조회에 반영합니다.
            Physics.SyncTransforms();

            Collider[] overlaps = Physics.OverlapSphere(
                activeHitPoint.position,
                activeRadius,
                activeLayers,
                QueryTriggerInteraction.Ignore);

            List<Collider> candidates = new List<Collider>();

            foreach (Collider candidate in overlaps)
            {
                // 보스 자신과 자식 오브젝트는 공격 대상에서 제외합니다.
                if (!candidate.transform.IsChildOf(transform))
                {
                    candidates.Add(candidate);
                }
            }

            return candidates.ToArray();
        }

        private void OnDisable()
        {
            CancelAttack();
        }

        private void OnDrawGizmosSelected()
        {
            Transform point = IsAttacking ? activeHitPoint : hitPoint;
            float radius = IsAttacking ? activeRadius : hitRadius;
            if (point == null || !IsPositiveFinite(radius)) return;

            // 범위 미리보기입니다. 구가 보인다고 계속 타격하는 것은 아닙니다.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(point.position, radius);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
