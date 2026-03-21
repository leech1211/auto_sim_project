/*
 * VehicleWebSocketServer.cs
 * ─────────────────────────────────────────────────────────────
 * WebSocket 기반 서버 (TCP 대안)
 * 
 * 의존성: NativeWebSocket 패키지 필요
 *   Unity Package Manager → Add package from git URL:
 *   https://github.com/endel/NativeWebSocket.git#upm
 * 
 * 포트: 9091 (기본값)
 * ─────────────────────────────────────────────────────────────
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

#if NATIVE_WEBSOCKET
using NativeWebSocket;
#endif

/// <summary>
/// HTTP/WebSocket 핸드셰이크를 직접 구현한 경량 WS 서버.
/// NativeWebSocket 패키지가 없는 환경에서도 동작합니다.
/// (클라이언트는 python/unity_vehicle_ws_client.py 참고)
/// </summary>
public class VehicleWebSocketServer : MonoBehaviour
{
    [Header("WebSocket 서버 설정")]
    public int  wsPort    = 9091;
    public bool autoStart = true;

    [Header("참조")]
    public VehicleController vehicle;
    public CameraCapture     cameraCapture;

    // 내부 — TCP 수준에서 WS 프로토콜 직접 구현
    private System.Net.Sockets.TcpListener listener;
    private Thread listenThread;
    private readonly List<WsClientSession> sessions = new List<WsClientSession>();
    private readonly object sessionsLock = new object();
    private readonly Queue<Action> mainQueue = new Queue<Action>();
    private readonly object queueLock = new object();

    void Start()
    {
        if (vehicle == null) vehicle = FindObjectOfType<VehicleController>();
        if (autoStart) StartWsServer();
    }

    void Update()
    {
        lock (queueLock)
            while (mainQueue.Count > 0) mainQueue.Dequeue()?.Invoke();
    }

    public void StartWsServer()
    {
        listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, wsPort);
        listener.Start();
        listenThread = new Thread(AcceptLoop) { IsBackground = true };
        listenThread.Start();
        Debug.Log($"[VehicleWsServer] WebSocket 서버 시작 — ws://localhost:{wsPort}");
    }

    void AcceptLoop()
    {
        while (true)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                var sess = new WsClientSession(client, this);
                lock (sessionsLock) sessions.Add(sess);
                new Thread(sess.Run) { IsBackground = true }.Start();
            }
            catch { break; }
        }
    }

    // VehicleServer 와 동일한 명령 처리 위임
    public string HandleCommand(string json) =>
        GetComponent<VehicleServer>()?.HandleCommand(json)
        ?? $"{{\"ok\":false,\"error\":\"VehicleServer 없음\"}}";

    public void EnqueueMain(Action a) { lock (queueLock) mainQueue.Enqueue(a); }
    public void RemoveSession(WsClientSession s) { lock (sessionsLock) sessions.Remove(s); }

    void OnApplicationQuit()
    {
        listener?.Stop();
        listenThread?.Abort();
    }
}


/// <summary>
/// RFC 6455 WebSocket 핸드셰이크 + 프레임 파싱/생성
/// </summary>
public class WsClientSession
{
    private readonly System.Net.Sockets.TcpClient client;
    private readonly VehicleWebSocketServer server;
    private System.Net.Sockets.NetworkStream stream;

    public WsClientSession(System.Net.Sockets.TcpClient c, VehicleWebSocketServer s)
    {
        client = c;
        server = s;
    }

    public void Run()
    {
        try
        {
            stream = client.GetStream();
            if (!Handshake()) return;

            while (true)
            {
                string msg = ReadFrame();
                if (msg == null) break;
                string resp = server.HandleCommand(msg);
                SendFrame(resp);
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[WsSession] 종료: {e.Message}");
        }
        finally
        {
            client.Close();
            server.RemoveSession(this);
        }
    }

    // ── RFC 6455 핸드셰이크 ──────────────────────
    bool Handshake()
    {
        byte[] buf = new byte[4096];
        int n = stream.Read(buf, 0, buf.Length);
        string req = Encoding.UTF8.GetString(buf, 0, n);

        string key = "";
        foreach (var line in req.Split('\n'))
            if (line.StartsWith("Sec-WebSocket-Key:"))
                key = line.Split(':')[1].Trim();

        if (string.IsNullOrEmpty(key)) return false;

        string accept = Convert.ToBase64String(
            System.Security.Cryptography.SHA1.Create().ComputeHash(
                Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

        string resp =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n";

        byte[] b = Encoding.UTF8.GetBytes(resp);
        stream.Write(b, 0, b.Length);
        return true;
    }

    // ── WebSocket 프레임 읽기 ────────────────────
    string ReadFrame()
    {
        int b0 = stream.ReadByte();
        int b1 = stream.ReadByte();
        if (b0 < 0 || b1 < 0) return null;

        bool masked  = (b1 & 0x80) != 0;
        int  payLen  = b1 & 0x7F;

        if (payLen == 126)
        {
            var ext = ReadExact(2);
            payLen = (ext[0] << 8) | ext[1];
        }
        else if (payLen == 127)
        {
            var ext = ReadExact(8);
            payLen = (int)BitConverter.ToInt64(ext, 0);
        }

        byte[] mask    = masked ? ReadExact(4) : null;
        byte[] payload = ReadExact(payLen);

        if (masked)
            for (int i = 0; i < payLen; i++)
                payload[i] ^= mask[i % 4];

        int opcode = b0 & 0x0F;
        if (opcode == 8) return null; // close
        return Encoding.UTF8.GetString(payload);
    }

    // ── WebSocket フレーム 송신 ─────────────────
    void SendFrame(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        int    len     = payload.Length;

        var frame = new System.IO.MemoryStream();
        frame.WriteByte(0x81); // FIN + text opcode

        if (len < 126)
            frame.WriteByte((byte)len);
        else if (len < 65536)
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(len >> 8));
            frame.WriteByte((byte)(len & 0xFF));
        }
        else
        {
            frame.WriteByte(127);
            var lb = BitConverter.GetBytes((long)len);
            if (BitConverter.IsLittleEndian) Array.Reverse(lb);
            frame.Write(lb, 0, 8);
        }

        frame.Write(payload, 0, len);
        byte[] data = frame.ToArray();
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    byte[] ReadExact(int n)
    {
        var buf = new byte[n];
        int total = 0;
        while (total < n)
        {
            int r = stream.Read(buf, total, n - total);
            if (r == 0) throw new Exception("연결 끊김");
            total += r;
        }
        return buf;
    }
}
