from unity_vehicle_client import UnityVehicleClient

client = UnityVehicleClient(host="127.0.0.1", port=9090)

print("Unity에 연결 시도 중...")
if not client.connect():
    print("❌ 연결 실패 — Unity가 Play 상태인지 확인하세요")
    exit(1)

print("연결 성공!")

state = client.get_state()
if state:
    print(f"[위치]     x={state.position.x:.3f}  y={state.position.y:.3f}  z={state.position.z:.3f}")
    print(f"[회전]     x={state.rotation.x:.1f}°  y={state.rotation.y:.1f}°  z={state.rotation.z:.1f}°")
    print(f"[속도벡터] vx={state.velocity.x:.3f}  vy={state.velocity.y:.3f}  vz={state.velocity.z:.3f}")
    print(f"[속도]     {state.speed_kmh:.2f} km/h")
    print(f"[제어]     throttle={state.throttle:.2f}  steering={state.steering:.2f}  brake={state.brake:.2f}")
    print(f"[충돌]     {state.is_colliding}  (대상: {state.collision_object or '없음'})")
else:
    print("❌ 상태 수신 실패")

client.disconnect()
