using UnityEngine;

/// <summary>
/// 카메라 이미지를 JPEG 바이트 배열로 캡처
/// Python 클라이언트에게 전송하기 위한 용도
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraCapture : MonoBehaviour
{
    [Header("캡처 설정")]
    public int   captureWidth  = 640;
    public int   captureHeight = 480;
    [Range(1, 100)]
    public int   jpegQuality   = 75;

    private Camera      cam;
    private RenderTexture renderTex;
    private Texture2D     readTex;

    void Awake()
    {
        cam = GetComponent<Camera>();
        InitTextures();
    }

    void InitTextures()
    {
        if (renderTex != null) renderTex.Release();

        renderTex = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        renderTex.Create();
        readTex   = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
    }

    /// <summary>
    /// 현재 프레임을 JPEG로 인코딩하여 반환
    /// ※ 반드시 Unity 메인 스레드에서 호출
    /// </summary>
    public byte[] CaptureJpeg()
    {
        var prevTarget = cam.targetTexture;
        cam.targetTexture = renderTex;
        cam.Render();
        cam.targetTexture = prevTarget;

        var prevActive = RenderTexture.active;
        RenderTexture.active = renderTex;
        readTex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        readTex.Apply();
        RenderTexture.active = prevActive;

        return readTex.EncodeToJPG(jpegQuality);
    }

    void OnDestroy()
    {
        if (renderTex != null) renderTex.Release();
    }
}
