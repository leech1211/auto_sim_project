using UnityEngine;
using UnityEditor;

/// <summary>
/// 메뉴: Tools > Setup Free Racing Car
/// Free Racing Car 프리팹에 Rigidbody, VehicleController, VehicleServer를
/// 자동으로 추가하고 WheelCollider / Mesh Transform을 연결합니다.
/// </summary>
public class VehicleSetupEditor : Editor
{
    [MenuItem("Tools/Setup Free Racing Car")]
    static void SetupVehicle()
    {
        // ── 1. Hierarchy에서 Free Racing Car 찾기 ──────────────────
        GameObject car = GameObject.Find("Free Racing Car");
        if (car == null)
        {
            EditorUtility.DisplayDialog("Setup 실패",
                "'Free Racing Car' GameObject를 Hierarchy에서 찾을 수 없습니다.\n" +
                "씬에 배치되어 있는지 확인하세요.", "확인");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(car, "Setup Free Racing Car");

        // ── 2. Rigidbody ───────────────────────────────────────────
        Rigidbody rb = GetOrAdd<Rigidbody>(car);
        rb.mass        = 1500f;
        rb.drag            = 0.05f;
        rb.angularDrag     = 0.05f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        Debug.Log("[Setup] Rigidbody 설정 완료");

        // ── 3. WheelCollider 4개 찾기 ──────────────────────────────
        // 경로: Free Racing Car > Wheels > Colliders > FrontLeftWheel 등
        WheelCollider wcFL = FindChildCollider(car, "Colliders/FrontLeftWheel");
        WheelCollider wcFR = FindChildCollider(car, "Colliders/FrontRightWheel");
        WheelCollider wcRL = FindChildCollider(car, "Colliders/RearLeftWheel");
        WheelCollider wcRR = FindChildCollider(car, "Colliders/RearRightWheel");

        bool collidersOk = wcFL && wcFR && wcRL && wcRR;
        if (!collidersOk)
        {
            // 경로가 다를 경우 이름으로 재탐색
            wcFL = FindWheelColliderByName(car, "FrontLeftWheel");
            wcFR = FindWheelColliderByName(car, "FrontRightWheel");
            wcRL = FindWheelColliderByName(car, "RearLeftWheel");
            wcRR = FindWheelColliderByName(car, "RearRightWheel");
        }

        if (!wcFL || !wcFR || !wcRL || !wcRR)
        {
            EditorUtility.DisplayDialog("Setup 실패",
                "WheelCollider를 가진 바퀴 오브젝트를 찾지 못했습니다.\n" +
                "Colliders 하위에 WheelCollider 컴포넌트가 있는지 확인하세요.", "확인");
            return;
        }

        // WheelCollider 물리 파라미터 적용
        ConfigureWheelCollider(wcFL); ConfigureWheelCollider(wcFR);
        ConfigureWheelCollider(wcRL); ConfigureWheelCollider(wcRR);
        Debug.Log("[Setup] WheelCollider 4개 설정 완료");

        // ── 4. 바퀴 메시 Transform 찾기 ───────────────────────────
        // 경로: Free Racing Car > Wheels > Meshes > FrontLeftWheel 등
        Transform tmFL = FindChildTransform(car, "Meshes/FrontLeftWheel");
        Transform tmFR = FindChildTransform(car, "Meshes/FrontRightWheel");
        Transform tmRL = FindChildTransform(car, "Meshes/RearLeftWheel");
        Transform tmRR = FindChildTransform(car, "Meshes/RearRightWheel");

        // 못 찾으면 이름으로 재탐색 (Colliders 하위 제외)
        if (!tmFL) tmFL = FindMeshTransformByName(car, "FrontLeftWheel",  wcFL.transform);
        if (!tmFR) tmFR = FindMeshTransformByName(car, "FrontRightWheel", wcFR.transform);
        if (!tmRL) tmRL = FindMeshTransformByName(car, "RearLeftWheel",   wcRL.transform);
        if (!tmRR) tmRR = FindMeshTransformByName(car, "RearRightWheel",  wcRR.transform);

        if (!tmFL || !tmFR || !tmRL || !tmRR)
        {
            Debug.LogWarning("[Setup] 일부 바퀴 메시 Transform을 찾지 못했습니다. " +
                             "VehicleController Inspector에서 직접 연결해 주세요.");
        }
        else
        {
            Debug.Log("[Setup] 바퀴 메시 Transform 4개 연결 완료");
        }

        // ── 5. VehicleController ───────────────────────────────────
        VehicleController vc = GetOrAdd<VehicleController>(car);
        vc.frontLeftWheel  = wcFL;
        vc.frontRightWheel = wcFR;
        vc.rearLeftWheel   = wcRL;
        vc.rearRightWheel  = wcRR;
        vc.frontLeftTransform  = tmFL;
        vc.frontRightTransform = tmFR;
        vc.rearLeftTransform   = tmRL;
        vc.rearRightTransform  = tmRR;
        vc.maxMotorTorque  = 1500f;
        vc.maxSteerAngle   = 35f;
        vc.maxBrakeTorque  = 3000f;
        vc.maxSpeed        = 100f;
        vc.centerOfMassY   = -0.5f;
        Debug.Log("[Setup] VehicleController 설정 완료");

        // ── 6. VehicleServer ──────────────────────────────────────
        VehicleServer vs = GetOrAdd<VehicleServer>(car);
        vs.tcpPort   = 9090;
        vs.autoStart = true;
        vs.vehicle   = vc;

        // CameraCapture가 있으면 자동 연결
        CameraCapture cc = car.GetComponentInChildren<CameraCapture>();
        if (cc != null)
        {
            vs.cameraCapture = cc;
            Debug.Log("[Setup] CameraCapture 자동 연결 완료");
        }
        Debug.Log("[Setup] VehicleServer 설정 완료");

        // ── 7. VehicleWebSocketServer ─────────────────────────────
        VehicleWebSocketServer wss = GetOrAdd<VehicleWebSocketServer>(car);
        wss.wsPort    = 9091;
        wss.autoStart = true;
        wss.vehicle   = vc;
        Debug.Log("[Setup] VehicleWebSocketServer 설정 완료");

        // ── 8. 무게중심 시각화용 기즈모 오브젝트 (선택) ────────────
        // CenterOfMass라는 빈 오브젝트가 없으면 생성
        Transform com = car.transform.Find("CenterOfMass");
        if (com == null)
        {
            GameObject comObj = new GameObject("CenterOfMass");
            comObj.transform.SetParent(car.transform);
            comObj.transform.localPosition = new Vector3(0, vc.centerOfMassY, 0);
        }

        // ── 9. 완료 ───────────────────────────────────────────────
        EditorUtility.SetDirty(car);

        string result =
            "✅ Free Racing Car 세팅 완료!\n\n" +
            "연결된 항목:\n" +
            $"  • Rigidbody (질량 1500kg)\n" +
            $"  • WheelCollider x4\n" +
            $"  • 바퀴 메시 Transform x{(tmFL ? 4 : 0)}\n" +
            $"  • VehicleController\n" +
            $"  • VehicleServer (TCP :9090)\n" +
            $"  • VehicleWebSocketServer (WS :9091)\n\n" +
            "▶ Play를 누르면 방향키로 바로 주행 가능합니다!";

        EditorUtility.DisplayDialog("Setup 완료", result, "확인");
        Debug.Log("[Setup] 모든 설정 완료 ✅");

        // Inspector 갱신
        Selection.activeGameObject = car;
    }

    // ── 헬퍼 메서드 ───────────────────────────────────────────────

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp == null) comp = go.AddComponent<T>();
        return comp;
    }

    /// <summary>상대 경로로 WheelCollider 탐색 (Wheels/Colliders/FrontLeftWheel 등)</summary>
    static WheelCollider FindChildCollider(GameObject root, string relativePath)
    {
        // Wheels 하위에서 먼저 찾기
        Transform wheels = root.transform.Find("Wheels");
        if (wheels != null)
        {
            Transform t = wheels.Find(relativePath);
            if (t != null) return t.GetComponent<WheelCollider>();
        }
        // 루트에서 직접
        Transform direct = root.transform.Find(relativePath);
        if (direct != null) return direct.GetComponent<WheelCollider>();
        return null;
    }

    /// <summary>이름으로 WheelCollider를 가진 자식 탐색</summary>
    static WheelCollider FindWheelColliderByName(GameObject root, string name)
    {
        foreach (var wc in root.GetComponentsInChildren<WheelCollider>(true))
            if (wc.gameObject.name == name) return wc;
        return null;
    }

    /// <summary>상대 경로로 Transform 탐색</summary>
    static Transform FindChildTransform(GameObject root, string relativePath)
    {
        Transform wheels = root.transform.Find("Wheels");
        if (wheels != null)
        {
            Transform t = wheels.Find(relativePath);
            if (t != null) return t;
        }
        return root.transform.Find(relativePath);
    }

    /// <summary>이름으로 메시 Transform 탐색 (exclude는 WheelCollider 쪽 Transform)</summary>
    static Transform FindMeshTransformByName(GameObject root, string name, Transform exclude)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name && t != exclude && t.GetComponent<WheelCollider>() == null)
                return t;
        }
        return null;
    }

    /// <summary>WheelCollider 물리 파라미터 기본값 설정</summary>
    static void ConfigureWheelCollider(WheelCollider wc)
    {
        wc.mass   = 20f;
        wc.radius = 0.35f;
        wc.suspensionDistance = 0.2f;

        JointSpring spring = wc.suspensionSpring;
        spring.spring   = 35000f;
        spring.damper   = 4500f;
        spring.targetPosition = 0.5f;
        wc.suspensionSpring = spring;

        WheelFrictionCurve fwd = wc.forwardFriction;
        fwd.stiffness = 1.5f;
        wc.forwardFriction = fwd;

        WheelFrictionCurve side = wc.sidewaysFriction;
        side.stiffness = 2.0f;
        wc.sidewaysFriction = side;
    }
}