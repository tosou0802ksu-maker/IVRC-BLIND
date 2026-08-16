
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Rendering;
using VRC.Udon;

public enum ViewRole
{
    Echo,
    Thermal,
    Memory
}

// 3視点(エコロケ/サーモ/過去の人)の「見えるもの」をカリングマスクで切り替える。
//
// VRChat実機ではシーンに置いたCameraではなくプレイヤーカメラが使われるため、
// VRCCameraSettings.ScreenCamera.CullingMask を書き換える必要がある。
// (シーンのCameraを触っても実機では何も起きない)
// localCamera はClientSim/エディタ確認用のフォールバック。
public class PlayerVisionController : UdonSharpBehaviour
{
    [Header("カリングマスク設定")]
    [SerializeField] private LayerMask echoCullingMask;
    [SerializeField] private LayerMask thermalCullingMask;
    [SerializeField] private LayerMask memoryCullingMask;

    [Header("エディタ確認用のフォールバック(任意)")]
    [Tooltip("実機ではVRCCameraSettingsを使うため未設定でも構わない。")]
    [SerializeField] private Camera localCamera;

    private ViewRole currentRole;

    void Start()
    {
        ApplyVisionMask();
    }

    // VRChat側がカメラ設定を作り直した時に呼ばれる。
    // ここで再適用しないとマスクが元に戻ってしまう。
    public override void OnVRCCameraSettingsChanged(VRCCameraSettings cameraSettings)
    {
        ApplyVisionMask();
    }

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
        LayerMask mask = echoCullingMask;

        if (currentRole == ViewRole.Thermal)
        {
            mask = thermalCullingMask;
        }
        else if (currentRole == ViewRole.Memory)
        {
            mask = memoryCullingMask;
        }

        VRCCameraSettings screen = VRCCameraSettings.ScreenCamera;
        if (screen != null)
        {
            screen.CullingMask = mask;
        }

        if (localCamera != null)
        {
            localCamera.cullingMask = mask;
        }
    }
}
