from unity_vehicle_client import UnityVehicleClient

client = UnityVehicleClient(host="127.0.0.1", port=9090)

print("Unity에 연결 시도 중...")
if client.connect():
    print("✅ 연결 성공!")
    client.disconnect()
else:
    print("❌ 연결 실패 — Unity가 Play 상태인지 확인하세요")
