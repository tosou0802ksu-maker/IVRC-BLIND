
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
// 設計:
//   エコロケ  : Echoレイヤー(23)だけを映す。背景は真っ黒。
//               → 視界が完全な暗闇になり、パルスが当たった物の輪郭だけが光って見える。
//   サーモ    : Thermalレイヤー(22)だけを映す。背景は真っ黒。
//               → 暗闇に熱源だけが浮かぶ。
//   過去の人  : Default + Memory + Player を映す。背景は通常(スカイボックス)。
//               → 普通に世界が見える。文字や色はMemoryレイヤーに置く。
//
// つまりエコロケ/サーモには Default レイヤーの物は一切見えない。
// 見せたい物には「Echoレイヤーの輪郭用メッシュ」「Thermalレイヤーの熱用メッシュ」を
// 実体とは別に重ねて配置する必要がある(配置手順.md参照)。
//
// VRChat実機ではシーンに置いたCameraではなくプレイヤーカメラが使われるため、
// VRCCameraSettings.ScreenCamera を書き換える必要がある。
// (シーンのCameraを触っても実機では何も起きない)
// localCamera はClientSim/エディタ確認用のフォールバック。
public class PlayerVisionController : UdonSharpBehaviour
{
    [Header("カリングマスク設定")]
    [Tooltip("エコロケ役。Echo(23)のみにチェックを入れる(Defaultは外す)。")]
    [SerializeField] private LayerMask echoCullingMask;

    [Tooltip("サーモ役。Thermal(22)のみにチェックを入れる(Defaultは外す)。")]
    [SerializeField] private LayerMask thermalCullingMask;

    [Tooltip("過去の人役。Default + Memory(24) + Player + PlayerLocal にチェックを入れる。")]
    [SerializeField] private LayerMask memoryCullingMask;

    [Header("背景を真っ黒にする役割")]
    [Tooltip("オンにするとスカイボックスを描かず単色で塗りつぶす(=暗闇になる)。")]
    [SerializeField] private bool echoBlackout = true;
    [SerializeField] private bool thermalBlackout = true;
    [SerializeField] private bool memoryBlackout = false;

    [Header("暗闇の色(通常は真っ黒)")]
    [SerializeField] private Color blackoutColor = Color.black;

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
        bool blackout = echoBlackout;

        if (currentRole == ViewRole.Thermal)
        {
            mask = thermalCullingMask;
            blackout = thermalBlackout;
        }
        else if (currentRole == ViewRole.Memory)
        {
            mask = memoryCullingMask;
            blackout = memoryBlackout;
        }

        VRCCameraSettings screen = VRCCameraSettings.ScreenCamera;
        if (screen != null)
        {
            screen.CullingMask = mask;
            screen.ClearFlags = blackout ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;

            if (blackout)
            {
                screen.BackgroundColor = blackoutColor;
            }
        }

        if (localCamera != null)
        {
            localCamera.cullingMask = mask;
            localCamera.clearFlags = blackout ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;

            if (blackout)
            {
                localCamera.backgroundColor = blackoutColor;
            }
        }
    }
}
