
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public enum ViewRole
{
    Echo,
    Thermal,
    Memory
}

public class PlayerVisionController : UdonSharpBehaviour
{
    [Header("カリングマスク設定")]
    [SerializeField] private LayerMask echoCullingMask;
    [SerializeField] private LayerMask thermalCullingMask;
    [SerializeField] private LayerMask memoryCullingMask;

    [Header("ローカルカメラ参照")]
    [Tooltip("Camera.mainはUdonSharpから呼び出せないため、シーン上のメインカメラをここに割り当ててください。")]
    [SerializeField] private Camera localCamera;

    private ViewRole currentRole;

    public void SetRole(ViewRole role)
    {
        currentRole = role;
        ApplyVisionMask();
    }

    public ViewRole GetRole()
    {
        return currentRole;
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

        if (currentRole == ViewRole.Thermal)
        {
            localCamera.cullingMask = thermalCullingMask;
        }
        else if (currentRole == ViewRole.Memory)
        {
            localCamera.cullingMask = memoryCullingMask;
        }
        else
        {
            localCamera.cullingMask = echoCullingMask;
        }
    }
}
