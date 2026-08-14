
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class PlayerVisionController : UdonSharpBehaviour
{
    [Header("カリングマスク設定")]
    [SerializeField] private LayerMask thermalCullingMask;
    [SerializeField] private LayerMask echoCullingMask;

    [Header("ローカルカメラ参照")]
    [Tooltip("Camera.mainはUdonSharpから呼び出せないため、シーン上のメインカメラをここに割り当ててください。")]
    [SerializeField] private Camera localCamera;

    private bool isThermalRole;

    public void SetRole(bool isThermal)
    {
        isThermalRole = isThermal;
        ApplyVisionMask();
    }

    private void ApplyVisionMask()
    {
        if (Networking.LocalPlayer == null)
        {
            return;
        }

        if (localCamera == null)
        {
            return;
        }

        localCamera.cullingMask = isThermalRole ? thermalCullingMask : echoCullingMask;
    }
}
