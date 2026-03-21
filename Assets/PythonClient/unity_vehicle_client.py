"""
unity_vehicle_client.py
========================
AirSim 스타일의 Python 클라이언트.
Unity VehicleServer(TCP 9090)에 연결해 차량을 제어합니다.

사용 예시:
    from unity_vehicle_client import UnityVehicleClient
    client = UnityVehicleClient()
    client.connect()
    client.set_control(throttle=0.5, steering=0.0)
    state = client.get_state()
    print(state.position, state.speed_kmh)
"""

import socket
import struct
import json
import base64
import time
from dataclasses import dataclass, field
from typing import Optional
import numpy as np


# ─────────────────────────────────────────────
#  데이터 클래스
# ─────────────────────────────────────────────

@dataclass
class Vector3:
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0

    def __repr__(self):
        return f"({self.x:.3f}, {self.y:.3f}, {self.z:.3f})"

    def to_numpy(self):
        return np.array([self.x, self.y, self.z], dtype=np.float32)


@dataclass
class VehicleState:
    position:         Vector3 = field(default_factory=Vector3)
    rotation:         Vector3 = field(default_factory=Vector3)   # Euler degrees
    velocity:         Vector3 = field(default_factory=Vector3)
    speed_kmh:        float   = 0.0
    throttle:         float   = 0.0
    steering:         float   = 0.0
    brake:            float   = 0.0
    is_colliding:     bool    = False
    collision_object: str     = ""

    @classmethod
    def from_dict(cls, d: dict) -> "VehicleState":
        return cls(
            position   = Vector3(d["px"], d["py"], d["pz"]),
            rotation   = Vector3(d["rx"], d["ry"], d["rz"]),
            velocity   = Vector3(d["vx"], d["vy"], d["vz"]),
            speed_kmh  = d["speed"],
            throttle   = d["throttle"],
            steering   = d["steering"],
            brake      = d["brake"],
            is_colliding     = d["collision"],
            collision_object = d.get("collision_object", ""),
        )


# ─────────────────────────────────────────────
#  클라이언트 클래스
# ─────────────────────────────────────────────

class UnityVehicleClient:
    """
    Unity VehicleServer와 통신하는 클라이언트.
    프로토콜: [4바이트 Little-Endian 길이][UTF-8 JSON 본문]
    """

    def __init__(self, host: str = "127.0.0.1", port: int = 9090, timeout: float = 5.0):
        self.host    = host
        self.port    = port
        self.timeout = timeout
        self._sock: Optional[socket.socket] = None

    # ── 연결 관리 ──────────────────────────────

    def connect(self) -> bool:
        """Unity에 연결. 성공 시 True 반환."""
        try:
            self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self._sock.settimeout(self.timeout)
            self._sock.connect((self.host, self.port))
            resp = self._send({"type": "ping"})
            print(f"[UnityVehicleClient] 연결 성공 → {self.host}:{self.port}  응답: {resp}")
            return True
        except Exception as e:
            print(f"[UnityVehicleClient] 연결 실패: {e}")
            self._sock = None
            return False

    def disconnect(self):
        """연결 종료."""
        if self._sock:
            try:
                self._sock.close()
            except Exception:
                pass
            self._sock = None
            print("[UnityVehicleClient] 연결 종료")

    def __enter__(self):
        self.connect()
        return self

    def __exit__(self, *_):
        self.disconnect()

    # ── 차량 제어 API ───────────────────────────

    def set_control(self, throttle: float = 0.0,
                    steering: float = 0.0,
                    brake: float = 0.0) -> bool:
        """
        차량 제어 입력 전송.
        - throttle : -1.0(후진) ~ 1.0(전진)
        - steering : -1.0(왼쪽) ~ 1.0(오른쪽)
        - brake    :  0.0 ~ 1.0
        """
        resp = self._send({
            "type":     "control",
            "throttle": float(np.clip(throttle, -1, 1)),
            "steering": float(np.clip(steering, -1, 1)),
            "brake":    float(np.clip(brake,     0, 1)),
        })
        return resp.get("ok", False)

    def set_throttle(self, value: float) -> bool:
        """가속/후진만 설정 (나머지는 0)."""
        return self.set_control(throttle=value)

    def set_steering(self, value: float) -> bool:
        """조향만 설정."""
        return self.set_control(steering=value)

    def brake(self, value: float = 1.0) -> bool:
        """제동."""
        return self.set_control(brake=value)

    def stop(self) -> bool:
        """즉시 정지."""
        return self.set_control(throttle=0.0, steering=0.0, brake=1.0)

    def enable_keyboard_mode(self) -> bool:
        """Unity 키보드 제어 모드로 전환."""
        resp = self._send({"type": "keyboard_mode"})
        return resp.get("ok", False)

    # ── 상태 읽기 API ───────────────────────────

    def get_state(self) -> Optional[VehicleState]:
        """차량 전체 상태 반환."""
        resp = self._send({"type": "get_state"})
        if not resp.get("ok"):
            return None
        return VehicleState.from_dict(resp)

    def get_position(self) -> Optional[Vector3]:
        """위치만 반환."""
        state = self.get_state()
        return state.position if state else None

    def get_speed_kmh(self) -> float:
        """현재 속도 (km/h)."""
        state = self.get_state()
        return state.speed_kmh if state else 0.0

    def is_colliding(self) -> bool:
        """충돌 중 여부."""
        state = self.get_state()
        return state.is_colliding if state else False

    # ── 카메라 API ──────────────────────────────

    def get_image(self) -> Optional[np.ndarray]:
        """
        카메라 이미지를 NumPy 배열로 반환 (H, W, 3) BGR.
        OpenCV 필요: pip install opencv-python
        """
        resp = self._send({"type": "get_image"})
        if not resp.get("ok"):
            print(f"[get_image] 오류: {resp.get('error')}")
            return None
        try:
            import cv2
            raw = base64.b64decode(resp["data"])
            arr = np.frombuffer(raw, dtype=np.uint8)
            img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
            return img
        except ImportError:
            print("[get_image] OpenCV 없음: pip install opencv-python")
            return None

    def get_image_pil(self):
        """카메라 이미지를 PIL Image로 반환. pip install pillow"""
        resp = self._send({"type": "get_image"})
        if not resp.get("ok"):
            return None
        try:
            from PIL import Image
            import io
            raw = base64.b64decode(resp["data"])
            return Image.open(io.BytesIO(raw))
        except ImportError:
            print("[get_image_pil] Pillow 없음: pip install pillow")
            return None

    # ── 리셋 API ────────────────────────────────

    def reset_vehicle(self,
                      position: tuple = (0, 0.5, 0),
                      rotation: tuple = (0, 0, 0)) -> bool:
        """차량을 지정 위치/회전으로 리셋."""
        resp = self._send({
            "type": "reset",
            "px": position[0], "py": position[1], "pz": position[2],
            "rx": rotation[0], "ry": rotation[1], "rz": rotation[2],
        })
        return resp.get("ok", False)

    # ── 저수준 통신 ─────────────────────────────

    def _send(self, payload: dict) -> dict:
        if self._sock is None:
            raise ConnectionError("연결되지 않음. connect()를 먼저 호출하세요.")
        body = json.dumps(payload).encode("utf-8")
        header = struct.pack("<I", len(body))
        self._sock.sendall(header + body)
        return self._recv()

    def _recv(self) -> dict:
        raw_len = self._recv_exact(4)
        msg_len = struct.unpack("<I", raw_len)[0]
        raw_body = self._recv_exact(msg_len)
        return json.loads(raw_body.decode("utf-8"))

    def _recv_exact(self, n: int) -> bytes:
        buf = bytearray()
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("서버 연결이 끊겼습니다.")
            buf.extend(chunk)
        return bytes(buf)
