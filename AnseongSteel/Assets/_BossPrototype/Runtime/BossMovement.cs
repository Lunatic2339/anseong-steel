using UnityEngine;

namespace AnseongSteel.Bosses
{
    [DisallowMultipleComponent]
    public class BossMovement : MonoBehaviour
    {
        private enum MovementMode
        {
            None,
            Linear,
            Jump
        }

        // 좌우·상하·목적지 이동 속도: 월드 좌표 단위/초.
        [SerializeField, Min(0.01f)] private float moveSpeed = 1f;

        private MovementMode mode;
        private Vector3 destination;

        private Vector3 jumpStartPosition;
        private float jumpHeight;
        private float jumpDuration;
        private float jumpElapsed;

        // 점프도 진행 중인 이동 요청에 포함합니다.
        public bool IsMoving => mode != MovementMode.None;
        public bool IsJumping => mode == MovementMode.Jump;

        public void MoveLeft(float distance)
        {
            Vector3 direction = -transform.right;
            direction.y = 0f;
            MoveInDirection(direction, distance);
        }

        public void MoveRight(float distance)
        {
            Vector3 direction = transform.right;
            direction.y = 0f;
            MoveInDirection(direction, distance);
        }

        public void MoveUp(float distance)
        {
            Vector3 direction = transform.forward;
            direction.y = 0f;
            MoveInDirection(direction, distance);
        }

        public void MoveDown(float distance)
        {
            Vector3 direction = -transform.forward;
            direction.y = 0f;
            MoveInDirection(direction, distance);
        }

        // 기존 계약 유지: 월드 XZ 목적지로 이동하고 현재 Y는 유지합니다.
        public void MoveTo(Vector3 targetPosition)
        {
            if (!isActiveAndEnabled) return;

            if (!IsFinite(targetPosition))
            {
                Debug.LogWarning("MoveTo: 좌표는 유한한 수여야 합니다.", this);
                return;
            }

            targetPosition.y = transform.position.y;
            BeginMove(targetPosition);
        }

        // height: 시작 높이에서 최고점까지의 거리.
        // duration: 상승과 하강을 합친 전체 시간(게임 시간, 초).
        public void Jump(float height, float duration)
        {
            if (!isActiveAndEnabled) return;

            if (!IsFinite(height) || height <= 0f ||
                !IsFinite(duration) || duration <= 0f)
            {
                Debug.LogWarning("Jump: 높이와 시간은 0보다 큰 유한한 수여야 합니다.", this);
                return;
            }

            // 유효한 새 점프만 기존 요청을 교체합니다.
            StopMove();
            jumpStartPosition = transform.position;
            jumpHeight = height;
            jumpDuration = duration;
            mode = MovementMode.Jump;
        }

        public void StopMove()
        {
            mode = MovementMode.None;
            destination = transform.position;
            jumpStartPosition = transform.position;
            jumpHeight = 0f;
            jumpDuration = 0f;
            jumpElapsed = 0f;
        }

        private void Update()
        {
            if (mode == MovementMode.Linear)
            {
                UpdateLinearMove();
            }
            else if (mode == MovementMode.Jump)
            {
                UpdateJump();
            }
        }

        private void MoveInDirection(Vector3 direction, float distance)
        {
            if (!isActiveAndEnabled) return;

            if (!IsFinite(distance) || distance < 0f)
            {
                Debug.LogWarning("이동 거리는 0 이상의 유한한 수여야 합니다.", this);
                return;
            }

            if (distance == 0f)
            {
                StopMove();
                return;
            }

            if (direction.sqrMagnitude < 0.000001f)
            {
                Debug.LogWarning("이동 방향을 계산할 수 없습니다.", this);
                return;
            }

            BeginMove(transform.position + direction.normalized * distance);
        }

        // 전달받은 목적지의 Y를 덮어쓰지 않는 내부 시작 함수입니다.
        private void BeginMove(Vector3 targetPosition)
        {
            if (!IsFinite(targetPosition))
            {
                Debug.LogWarning("이동 목적지는 유한한 좌표여야 합니다.", this);
                return;
            }

            StopMove();
            destination = targetPosition;

            if (!transform.position.Equals(destination))
            {
                mode = MovementMode.Linear;
            }
        }

        private void UpdateLinearMove()
        {
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
            {
                Debug.LogWarning("Move Speed는 0보다 큰 유한한 수여야 합니다.", this);
                StopMove();
                return;
            }

            Vector3 nextPosition = Vector3.MoveTowards(
                transform.position,
                destination,
                moveSpeed * Time.deltaTime);

            transform.position = nextPosition;

            if (nextPosition.Equals(destination))
            {
                StopMove();
            }
        }

        private void UpdateJump()
        {
            jumpElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(jumpElapsed / jumpDuration);

            // t=0에서 출발, 0.5에서 최고점, 1에서 시작 위치로 복귀합니다.
            float heightOffset = jumpHeight * (4f * t * (1f - t));
            transform.position = jumpStartPosition + Vector3.up * heightOffset;

            if (t >= 1f)
            {
                transform.position = jumpStartPosition;
                StopMove();
            }
        }

        private void OnDisable()
        {
            StopMove();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}