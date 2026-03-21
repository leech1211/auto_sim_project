using UnityEngine;

/// <summary>
/// WheelCollider 기반 차량 제어 스크립트
/// 키보드 방향키 + Python 외부 제어 모두 지원
/// </summary>
public class VehicleController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;

    [Header("Wheel Transforms (시각적 메시)")]
    public Transform frontLeftTransform;
    public Transform frontRightTransform;
    public Transform rearLeftTransform;
    public Transform rearRightTransform;

    [Header("차량 설정")]
    public float maxMotorTorque = 1500f;   // 최대 구동 토크 (Nm)
    public float maxSteerAngle = 35f;       // 최대 조향각 (도)
    public float maxBrakeTorque = 3000f;    // 최대 제동 토크 (Nm)
    public float maxSpeed = 100f;           // 최대 속도 (km/h)

    [Header("차량 안정성")]
    public float centerOfMassY = -0.5f;    // 무게중심 낮추기 (전복 방지)

    // --- 외부에서 주입 가능한 제어 입력 (Python이 여기에 씀) ---
    [HideInInspector] public float externalThrottle = 0f;   // -1 ~ 1
    [HideInInspector] public float externalSteering = 0f;  // -1 ~ 1
    [HideInInspector] public float externalBrake = 0f;     // 0 ~ 1
    [HideInInspector] public bool useExternalControl = false;

    // --- 충돌 상태 ---
    [HideInInspector] public bool isColliding = false;
    [HideInInspector] public string lastCollisionObject = "";
    [HideInInspector] public float lastCollisionTime = -999f;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            // 무게중심을 낮춰 안정성 향상
            rb.centerOfMass = new Vector3(0, centerOfMassY, 0);
        }
    }

    void FixedUpdate()
    {
        float throttle, steering, brake;

        if (useExternalControl)
        {
            // Python 제어 모드
            throttle = externalThrottle;
            steering = externalSteering;
            brake    = externalBrake;
        }
        else
        {
            // 키보드 제어 모드
            throttle = Input.GetAxis("Vertical");    // ↑↓
            steering = Input.GetAxis("Horizontal");  // ←→
            brake    = Input.GetKey(KeyCode.Space) ? 1f : 0f;
        }

        ApplyControl(throttle, steering, brake);
        UpdateWheelMeshes();
    }

    void ApplyControl(float throttle, float steering, float brake)
    {
        float currentSpeedKmh = rb != null ? rb.velocity.magnitude * 3.6f : 0f;

        // 속도 제한
        float motorTorque = 0f;
        if (currentSpeedKmh < maxSpeed)
            motorTorque = throttle * maxMotorTorque;

        float steerAngle = steering * maxSteerAngle;
        float brakeTorque = brake * maxBrakeTorque;

        // 앞바퀴 조향
        frontLeftWheel.steerAngle  = steerAngle;
        frontRightWheel.steerAngle = steerAngle;

        // 뒷바퀴 구동 (RWD)
        rearLeftWheel.motorTorque  = motorTorque;
        rearRightWheel.motorTorque = motorTorque;

        // 제동 (4륜)
        frontLeftWheel.brakeTorque  = brakeTorque;
        frontRightWheel.brakeTorque = brakeTorque;
        rearLeftWheel.brakeTorque   = brakeTorque;
        rearRightWheel.brakeTorque  = brakeTorque;
    }

    /// <summary>WheelCollider 위치/회전을 시각적 메시에 동기화</summary>
    void UpdateWheelMeshes()
    {
        UpdateWheelPose(frontLeftWheel,  frontLeftTransform);
        UpdateWheelPose(frontRightWheel, frontRightTransform);
        UpdateWheelPose(rearLeftWheel,   rearLeftTransform);
        UpdateWheelPose(rearRightWheel,  rearRightTransform);
    }

    void UpdateWheelPose(WheelCollider col, Transform t)
    {
        if (t == null) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        t.position = pos;
        t.rotation = rot;
    }

    // --- 공개 API (VehicleServer가 호출) ---

    public Vector3 GetPosition()    => transform.position;
    public Vector3 GetRotation()    => transform.eulerAngles;
    public float   GetSpeedKmh()    => rb != null ? rb.velocity.magnitude * 3.6f : 0f;
    public Vector3 GetVelocity()    => rb != null ? rb.velocity : Vector3.zero;

    public VehicleState GetState()
    {
        return new VehicleState
        {
            position   = GetPosition(),
            rotation   = GetRotation(),
            velocity   = GetVelocity(),
            speedKmh   = GetSpeedKmh(),
            throttle   = externalThrottle,
            steering   = externalSteering,
            brake      = externalBrake,
            isColliding = isColliding,
            collisionObject = lastCollisionObject
        };
    }

    public void SetControl(float throttle, float steering, float brake)
    {
        externalThrottle = Mathf.Clamp(throttle, -1f, 1f);
        externalSteering = Mathf.Clamp(steering, -1f, 1f);
        externalBrake    = Mathf.Clamp(brake,     0f, 1f);
        useExternalControl = true;
    }

    public void ResetVehicle(Vector3 position, Quaternion rotation)
    {
        if (rb != null)
        {
            rb.velocity        = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.position = position;
        transform.rotation = rotation;
    }

    // --- 충돌 감지 ---
    void OnCollisionEnter(Collision col)
    {
        isColliding         = true;
        lastCollisionObject = col.gameObject.name;
        lastCollisionTime   = Time.time;
    }

    void OnCollisionExit(Collision col)
    {
        isColliding = false;
    }
}

/// <summary>직렬화용 차량 상태 구조체</summary>
[System.Serializable]
public class VehicleState
{
    public Vector3 position;
    public Vector3 rotation;
    public Vector3 velocity;
    public float   speedKmh;
    public float   throttle;
    public float   steering;
    public float   brake;
    public bool    isColliding;
    public string  collisionObject;
}
