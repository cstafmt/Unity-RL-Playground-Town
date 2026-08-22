using UnityEngine;

public class Billboard : MonoBehaviour
{
    public Camera mainCamera;

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void LateUpdate()
    {
        if (mainCamera != null)
        {
            // 让物体始终朝向摄像机
            transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                             mainCamera.transform.rotation * Vector3.up);
        }
    }
}
