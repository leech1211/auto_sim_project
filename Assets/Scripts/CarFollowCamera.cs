using UnityEngine;

/// <summary>
/// 카트라이더 스타일 3인칭 추적 카메라
/// 메인 카메라에 붙이고 target에 차량을 연결하면 차량 뒤에서 부드럽게 따라감
/// </summary>
public class CarFollowCamera : MonoBehaviour
{
    [Header("추적 대상")]
    public Transform target;                 // 따라갈 차량

    [Header("위치")]
    public float distance   = 6.0f;          // 차량 뒤쪽 거리 (m)
    public float height     = 2.5f;          // 차량 위쪽 높이 (m)
    public float lookHeight = 1.0f;          // 바라볼 지점 높이 (차량 기준)

    [Header("부드러움 (값이 클수록 빠르게 따라감)")]
    public float positionDamping = 8f;       // 위치 추종 속도
    public float rotationDamping = 4f;       // 회전(요) 추종 속도 — 낮을수록 코너에서 차 옆모습이 보임

    [Header("속도감 (FOV)")]
    public bool  useSpeedFov  = true;
    public float baseFov      = 60f;         // 정지 시 FOV
    public float maxFov       = 75f;         // 최고 속도 시 FOV
    public float maxFovSpeed  = 100f;        // maxFov에 도달하는 속도 (km/h)
    public float fovDamping   = 3f;

    [Header("기타")]
    public float snapDistance = 30f;         // 이 거리 이상 떨어지면 즉시 이동 (리셋/텔레포트 대응)

    private Camera    cam;
    private Rigidbody targetRb;
    private float     currentYaw;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target == null)
        {
            var vehicle = FindObjectOfType<VehicleController>();
            if (vehicle != null) target = vehicle.transform;
        }
        if (target == null)
        {
            Debug.LogWarning("[CarFollowCamera] 추적 대상(target)이 없습니다.");
            return;
        }

        targetRb = target.GetComponent<Rigidbody>();
        SnapToTarget();
    }

    // 차량 물리(FixedUpdate)와 보간이 끝난 뒤 카메라를 갱신
    void LateUpdate()
    {
        if (target == null) return;

        if ((transform.position - target.position).sqrMagnitude > snapDistance * snapDistance)
        {
            SnapToTarget();
            return;
        }

        float dt = Time.deltaTime;

        // 차량의 요(Y축 회전)만 따라감 — 차체 기울기(피치/롤)는 무시해 화면 흔들림 방지
        currentYaw = Mathf.LerpAngle(currentYaw, target.eulerAngles.y, 1f - Mathf.Exp(-rotationDamping * dt));

        Vector3 desiredPos = GetDesiredPosition(currentYaw);
        transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-positionDamping * dt));
        transform.LookAt(target.position + Vector3.up * lookHeight);

        UpdateFov(dt);
    }

    /// <summary>카메라를 차량 뒤 목표 위치로 즉시 이동</summary>
    public void SnapToTarget()
    {
        if (target == null) return;
        currentYaw = target.eulerAngles.y;
        transform.position = GetDesiredPosition(currentYaw);
        transform.LookAt(target.position + Vector3.up * lookHeight);
        if (cam != null && useSpeedFov) cam.fieldOfView = baseFov;
    }

    Vector3 GetDesiredPosition(float yaw)
    {
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        return target.position - yawRot * Vector3.forward * distance + Vector3.up * height;
    }

    void UpdateFov(float dt)
    {
        if (!useSpeedFov || cam == null || targetRb == null) return;

        float speedKmh = targetRb.velocity.magnitude * 3.6f;
        float t        = Mathf.Clamp01(speedKmh / maxFovSpeed);
        float goalFov  = Mathf.Lerp(baseFov, maxFov, t);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, goalFov, 1f - Mathf.Exp(-fovDamping * dt));
    }
}
