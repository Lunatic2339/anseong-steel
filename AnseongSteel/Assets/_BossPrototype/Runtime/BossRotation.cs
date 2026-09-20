using UnityEngine;

namespace AnseongSteel.Bosses
{
    [DisallowMultipleComponent]
    public class BossRotation : MonoBehaviour
    {
        // 월드 Y축 기준 회전 속도: 도/초.
        [SerializeField, Min(0.01f)] private float rotationSpeed = 90f;

        // +값은 오른쪽, -값은 왼쪽으로 남은 회전 각도입니다.
        private float remainingAngle;

        public bool IsRotating { get; private set; }

        public void TurnLeft(float angle)
        {
            TurnRelative(angle, -1f);
        }

        public void TurnRight(float angle)
        {
            TurnRelative(angle, 1f);
        }

        // 월드 Y축의 절대 각도를 목표로, 가장 짧은 방향으로 회전합니다.
        public void RotateTo(float targetYRotation)
        {
            if (!isActiveAndEnabled) return;

            if (!IsFinite(targetYRotation))
            {
                Debug.LogWarning("RotateTo: 목표 각도는 유한한 수여야 합니다.", this);
                return;
            }

            float currentYRotation = transform.eulerAngles.y;
            float shortestAngle = Mathf.DeltaAngle(currentYRotation, targetYRotation);

            BeginRotation(shortestAngle);
        }

        // 호출 시점의 targetPosition을 바라보도록 회전합니다.
        // Y 높이는 무시하고 XZ 평면에서 방향을 계산합니다.
        public void LookAt(Vector3 targetPosition)
        {
            if (!isActiveAndEnabled) return;

            if (!IsFinite(targetPosition))
            {
                Debug.LogWarning("LookAt: 목표 좌표는 유한한 수여야 합니다.", this);
                return;
            }

            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.000001f)
            {
                Debug.LogWarning("LookAt: 보스와 목표의 XZ 위치가 같아 바라볼 방향을 계산할 수 없습니다.", this);
                return;
            }

            Quaternion lookRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            RotateTo(lookRotation.eulerAngles.y);
        }

        public void StopRotate()
        {
            IsRotating = false;
            remainingAngle = 0f;
        }

        private void Update()
        {
            if (!IsRotating) return;

            if (!IsPositiveFinite(rotationSpeed))
            {
                Debug.LogWarning("Rotation Speed는 0보다 큰 유한한 수여야 합니다.", this);
                StopRotate();
                return;
            }

            float maxStep = rotationSpeed * Time.deltaTime;
            float step = Mathf.Min(Mathf.Abs(remainingAngle), maxStep)
                * Mathf.Sign(remainingAngle);

            // 월드 Y축만 회전합니다.
            transform.Rotate(0f, step, 0f, Space.World);
            remainingAngle -= step;

            if (Mathf.Abs(remainingAngle) <= 0.0001f)
            {
                StopRotate();
            }
        }

        private void TurnRelative(float angle, float directionSign)
        {
            if (!isActiveAndEnabled) return;

            if (!IsFinite(angle) || angle < 0f)
            {
                Debug.LogWarning("회전 각도는 0 이상의 유한한 수여야 합니다.", this);
                return;
            }

            if (angle == 0f)
            {
                StopRotate();
                return;
            }

            BeginRotation(angle * directionSign);
        }

        private void BeginRotation(float signedAngle)
        {
            // 유효한 새 회전 명령은 이전 회전 명령을 교체합니다.
            StopRotate();

            if (Mathf.Abs(signedAngle) <= 0.0001f)
            {
                return;
            }

            remainingAngle = signedAngle;
            IsRotating = true;
        }

        private void OnDisable()
        {
            StopRotate();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && IsFinite(value);
        }
    }
}
