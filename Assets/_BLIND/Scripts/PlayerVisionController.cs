
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class PlayerVisionController : UdonSharpBehaviour
{
    [Header("カリングマスク設定")]
    [SerializeField] private LayerMask thermalCullingMask;
    [SerializeField] private LayerMask echoCullingMask;

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

        Camera localCamera = Camera.main;
        if (localCamera == null)
        {
            return;
        }

        localCamera.cullingMask = isThermalRole ? thermalCullingMask : echoCullingMask;
    }
}
