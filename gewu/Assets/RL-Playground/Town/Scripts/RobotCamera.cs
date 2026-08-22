using UnityEngine;
using System.Collections;
using System.IO;

public class RobotCamera : MonoBehaviour
{
    [Header("设置")]
    [Tooltip("如果不指定，默认使用 MainCamera")]
    public Camera visionCamera;

    [Tooltip("截图宽度")]
    public int resolutionWidth = 768;   // ★ 512 → 768
    [Tooltip("截图高度")]
    public int resolutionHeight = 768;  // ★ 512 → 768

    void Start()
    {
        if (visionCamera == null)
        {
            visionCamera = GetComponent<Camera>();
            if (visionCamera == null)
            {
                visionCamera = Camera.main;
                if (visionCamera == null)
                {
                    Debug.LogError("[RobotCamera] 未找到任何 Camera 组件！");
                }
            }
        }
    }

    public string CaptureBase64Image()
    {
        if (visionCamera == null) return "";

        RenderTexture rt = new RenderTexture(resolutionWidth, resolutionHeight, 24);
        RenderTexture oldRT = RenderTexture.active;

        visionCamera.targetTexture = rt;

        Texture2D screenShot = new Texture2D(resolutionWidth, resolutionHeight, TextureFormat.RGB24, false);
        visionCamera.Render();

        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, resolutionWidth, resolutionHeight), 0, 0);
        screenShot.Apply();

        visionCamera.targetTexture = null;
        RenderTexture.active = oldRT;
        Destroy(rt);

        // ★ 质量从 75 提高到 90
        byte[] bytes = screenShot.EncodeToJPG(90);
        Destroy(screenShot);

        string base64 = System.Convert.ToBase64String(bytes);

        // ★ 调试日志
        Debug.Log($"<color=yellow>[RobotCamera]</color> Captured {resolutionWidth}x{resolutionHeight}, size: {bytes.Length / 1024}KB");

        return base64;
    }
}
