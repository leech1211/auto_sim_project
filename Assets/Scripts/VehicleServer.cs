using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// TCP 서버 — Python 클라이언트의 JSON 명령을 수신하고 응답 전송
/// 포트: 9090 (기본값, Inspector에서 변경 가능)
/// </summary>
public class VehicleServer : MonoBehaviour
{
    [Header("서버 설정")]
    public int tcpPort  = 9090;
    public bool autoStart = true;

    [Header("참조")]
    public VehicleController vehicle;
    public CameraCapture cameraCapture; // 선택적

    // --- 내부 ---
    private TcpListener tcpListener;
    private Thread       listenThread;
    private readonly List<ClientSession> sessions = new List<ClientSession>();
    private readonly object sessionsLock = new object();

    // Unity 메인 스레드 큐
    private readonly Queue<Action> mainThreadQueue = new Queue<Action>();
    private readonly object        queueLock       = new object();

    void Start()
    {
        Debug.Log("VehicleServer Start() 호출됨");
        if (vehicle == null)
            vehicle = FindObjectOfType<VehicleController>();

        if (autoStart) StartServer();
    }

    void Update()
    {
        // 메인 스레드에서 처리해야 할 작업 실행
        lock (queueLock)
        {
            while (mainThreadQueue.Count > 0)
                mainThreadQueue.Dequeue()?.Invoke();
        }
    }

    public void StartServer()
    {
        Debug.Log("StartServer() 진입");
        tcpListener  = new TcpListener(IPAddress.Any, tcpPort);
        tcpListener.Start();
        listenThread = new Thread(ListenLoop) { IsBackground = true };
        listenThread.Start();
        Debug.Log($"[VehicleServer] TCP 서버 시작 — 포트 {tcpPort}");
    }

    void ListenLoop()
    {
        while (true)
        {
            try
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                Debug.Log($"[VehicleServer] 클라이언트 연결: {client.Client.RemoteEndPoint}");
                var session = new ClientSession(client, this);
                lock (sessionsLock) sessions.Add(session);
                var t = new Thread(session.Run) { IsBackground = true };
                t.Start();
            }
            catch { break; }
        }
    }

    /// <summary>JSON 명령 처리 — 별도 스레드에서 호출됨</summary>
    public string HandleCommand(string json)
    {
        try
        {
            var cmd = JsonUtility.FromJson<Command>(json);
            switch (cmd.type)
            {
                case "control":
                    EnqueueMain(() => vehicle.SetControl(cmd.throttle, cmd.steering, cmd.brake));
                    return OkJson("control accepted");

                case "keyboard_mode":
                    EnqueueMain(() => vehicle.useExternalControl = false);
                    return OkJson("keyboard mode");

                case "get_state":
                    // 메인 스레드에서 읽어야 안전
                    string stateJson = null;
                    var done = new ManualResetEventSlim(false);
                    EnqueueMain(() =>
                    {
                        var s = vehicle.GetState();
                        stateJson = BuildStateJson(s);
                        done.Set();
                    });
                    done.Wait(1000);
                    return stateJson ?? ErrorJson("state timeout");

                case "get_image":
                    if (cameraCapture == null) return ErrorJson("no camera");
                    byte[] imgBytes = null;
                    var doneImg = new ManualResetEventSlim(false);
                    EnqueueMain(() =>
                    {
                        imgBytes = cameraCapture.CaptureJpeg();
                        doneImg.Set();
                    });
                    doneImg.Wait(2000);
                    if (imgBytes == null) return ErrorJson("capture failed");
                    return "{\"ok\":true,\"type\":\"image\",\"data\":\"" +
                           Convert.ToBase64String(imgBytes) + "\"}";

                case "reset":
                    EnqueueMain(() => vehicle.ResetVehicle(
                        new Vector3(cmd.px, cmd.py, cmd.pz),
                        Quaternion.Euler(cmd.rx, cmd.ry, cmd.rz)));
                    return OkJson("reset done");

                case "ping":
                    return OkJson("pong");

                default:
                    return ErrorJson($"unknown command: {cmd.type}");
            }
        }
        catch (Exception e)
        {
            return ErrorJson(e.Message);
        }
    }

    string BuildStateJson(VehicleState s)
    {
        return $"{{\"ok\":true,\"type\":\"state\"," +
               $"\"px\":{s.position.x:F4},\"py\":{s.position.y:F4},\"pz\":{s.position.z:F4}," +
               $"\"rx\":{s.rotation.x:F4},\"ry\":{s.rotation.y:F4},\"rz\":{s.rotation.z:F4}," +
               $"\"vx\":{s.velocity.x:F4},\"vy\":{s.velocity.y:F4},\"vz\":{s.velocity.z:F4}," +
               $"\"speed\":{s.speedKmh:F4}," +
               $"\"throttle\":{s.throttle:F4},\"steering\":{s.steering:F4},\"brake\":{s.brake:F4}," +
               $"\"collision\":{(s.isColliding ? "true" : "false")}," +
               $"\"collision_object\":\"{s.collisionObject}\"}}";
    }

    void EnqueueMain(Action action)
    {
        lock (queueLock) mainThreadQueue.Enqueue(action);
    }

    string OkJson(string msg)    => $"{{\"ok\":true,\"msg\":\"{msg}\"}}";
    string ErrorJson(string msg) => $"{{\"ok\":false,\"error\":\"{msg}\"}}";

    public void RemoveSession(ClientSession s)
    {
        lock (sessionsLock) sessions.Remove(s);
    }

    void OnApplicationQuit()
    {
        tcpListener?.Stop();
        listenThread?.Abort();
    }

    // --- 직렬화용 커맨드 ---
    [Serializable]
    class Command
    {
        public string type;
        public float throttle;
        public float steering;
        public float brake;
        public float px, py, pz;
        public float rx, ry, rz;
    }
}

/// <summary>클라이언트 세션 — 길이-접두사 프로토콜 (4바이트 Little-Endian + JSON)</summary>
public class ClientSession
{
    private readonly TcpClient    client;
    private readonly VehicleServer server;
    private readonly NetworkStream stream;

    public ClientSession(TcpClient client, VehicleServer server)
    {
        this.client = client;
        this.server = server;
        stream = client.GetStream();
    }

    public void Run()
    {
        byte[] lenBuf = new byte[4];
        try
        {
            while (true)
            {
                // 4바이트 길이 읽기
                ReadExact(lenBuf, 4);
                int msgLen = BitConverter.ToInt32(lenBuf, 0);
                if (msgLen <= 0 || msgLen > 1024 * 1024) break; // 최대 1MB

                // JSON 본문 읽기
                byte[] bodyBuf = new byte[msgLen];
                ReadExact(bodyBuf, msgLen);
                string json = Encoding.UTF8.GetString(bodyBuf);

                // 처리 & 응답
                string response = server.HandleCommand(json);
                byte[] respBytes = Encoding.UTF8.GetBytes(response);
                byte[] respLen   = BitConverter.GetBytes(respBytes.Length);

                stream.Write(respLen,   0, 4);
                stream.Write(respBytes, 0, respBytes.Length);
                stream.Flush();
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[ClientSession] 연결 종료: {e.Message}");
        }
        finally
        {
            client.Close();
            server.RemoveSession(this);
        }
    }

    void ReadExact(byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buf, total, count - total);
            if (n == 0) throw new Exception("연결 끊김");
            total += n;
        }
    }
}
