# 🚗 Unity Vehicle — Python Control

AirSim을 대체하는 **Unity + Python** 차량 제어 환경입니다.
Unity의 WheelCollider 물리 기반 차량을 키보드 또는 Python 스크립트로 제어할 수 있습니다.

---

## 📌 프로젝트 배경

기존에 **AirSim** 환경에서 차량 제어 및 센서 데이터 수집 작업을 진행하던 중
성능 문제(버벅임)로 인해 **Unity 기반 환경으로 마이그레이션**한 프로젝트입니다.

AirSim에서 사용하던 Python API와 유사한 인터페이스를 유지하면서
Unity의 가벼운 렌더링과 물리 엔진을 활용합니다.

---


## 🗺️ 로드맵

### Phase 1 — 기반 환경 구축 ✅
- [x] Unity 프로젝트 생성 및 Free Racing Car 에셋 적용
- [x] WheelCollider 물리 세팅 (서스펜션, 마찰, 토크)
- [x] 키보드 제어 (VehicleController)
- [x] TCP / WebSocket 서버 구현 (VehicleServer, VehicleWebSocketServer)
- [x] Python 클라이언트 라이브러리 구현 (AirSim 스타일 API)
- [x] 에디터 자동 세팅 도구 (Tools > Setup Free Racing Car)

### Phase 2 — Python 연동 테스트 🔄
- [ ] Python ping 연결 확인
- [ ] 차량 상태 실시간 읽기 (위치, 속도, 회전)
- [ ] Python으로 주행 제어 (조향/가속/브레이크)
- [ ] 카메라 이미지 수신 및 OpenCV 처리

### Phase 3 — 환경 확장 📋
- [ ] 주행 맵 / 트랙 제작
- [ ] 장애물 배치 및 충돌 시나리오
- [ ] 다양한 카메라 뷰 (전방, 조감, FPV)
- [ ] Lidar / Depth 센서 시뮬레이션


---

## 🏗️ 프로젝트 구조

```
MyUnityProject/
├── Assets/
│   └── Scripts/
│       ├── VehicleController.cs        # WheelCollider 차량 물리 + 제어
│       ├── VehicleServer.cs            # TCP 서버 (포트 9090)
│       ├── VehicleWebSocketServer.cs   # WebSocket 서버 (포트 9091)
│       ├── CameraCapture.cs            # 카메라 JPEG 캡처
│       └── Editor/
│           ├── VehicleSetupEditor.cs   # 자동 세팅 (Tools > Setup)
│           └── WheelColliderFixer.cs   # WheelCollider 자동 수정
└── PythonClient/                       # Unity 프로젝트 루트 외부
    ├── unity_vehicle_client.py         # TCP 클라이언트
    ├── unity_vehicle_ws_client.py      # WebSocket 클라이언트
    ├── test_ping.py                    # 연결 테스트
    └── examples.py                     # 사용 예시 모음
```

---

## 🚀 시작하기

### Unity 세팅

1. Unity 2021.3 LTS 이상으로 프로젝트 생성
2. `Assets/Scripts/`에 C# 스크립트 4개 복사
3. `Assets/Scripts/Editor/`에 에디터 스크립트 복사
4. 상단 메뉴 → **Tools → Setup Free Racing Car** 실행
5. ▶ Play → Console에서 서버 시작 메시지 확인

```
[VehicleServer] TCP 서버 시작 — 포트 9090
```





## ⌨️ 키보드 제어

| 키 | 동작 |
|----|------|
| ↑ / W | 전진 |
| ↓ / S | 후진 |
| ← / A | 좌회전 |
| → / D | 우회전 |
| Space | 브레이크 |

> Python이 `set_control()`을 호출하면 자동으로 외부 제어 모드로 전환됩니다.
> `enable_keyboard_mode()`로 키보드 제어로 복귀 가능합니다.

---

## 🔌 통신 프로토콜

**TCP (기본 권장)** — 포트 9090
```
[4바이트 Little-Endian 길이][UTF-8 JSON 본문]
```

**WebSocket** — 포트 9091
```
ws://localhost:9091
```

### 명령 예시
```json
{"type": "control", "throttle": 0.5, "steering": -0.3, "brake": 0.0}
{"type": "get_state"}
{"type": "get_image"}
{"type": "reset", "px": 0, "py": 0.5, "pz": 0, "rx": 0, "ry": 0, "rz": 0}
{"type": "ping"}
```

---

## 🛠️ 개발 환경

- **Unity** 2021.3 LTS+
- **Python** 3.8+
- **에셋** ARCADE - FREE Racing Car
- **IDE** VS Code

---

## 📝 참고

- AirSim Python API와 유사한 인터페이스로 설계되어 기존 코드 재활용 가능
- TCP / WebSocket 둘 다 지원하므로 환경에 맞게 선택 가능
- 키보드 ↔ Python 제어를 런타임 중 자유롭게 전환 가능
- 개발 블로그 : https://blog.naver.com/cacu1211
