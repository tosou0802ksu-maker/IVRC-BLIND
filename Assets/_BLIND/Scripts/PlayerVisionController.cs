
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

    [Header("過去の人から「現在」を隠す")]
    [Tooltip("オンにすると NowOnly(25) レイヤーの物が過去の人には見えなくなる。" +
             "過去の人は「かつてそこにあった部屋」しか見えず、現在の障害物は" +
             "エコロケ役とサーモ役に教えてもらうしかなくなる。" +
             "実機テストで A/B を切り替えて体験を比べられるようにここをbool にしてある。")]
    [SerializeField] private bool memoryHidesPresent = true;

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

    // 「現在だけに存在する物」を置くレイヤー。過去の人には見えない。
    public const int LayerNowOnly = 25;

    private ViewRole currentRole;

    // スプリンクラー等で一時的に視界を完全に奪う演出用。
    // 0より大きい間は役割に関係なくカリングマスクを0(何も見えない)にする。
    private float forcedBlackoutTimer;

    void Start()
    {
        ApplyVisionMask();
    }

    void Update()
    {
        if (forcedBlackoutTimer <= 0f)
        {
            return;
        }

        forcedBlackoutTimer -= Time.deltaTime;
        if (forcedBlackoutTimer <= 0f)
        {
            forcedBlackoutTimer = 0f;
            ApplyVisionMask();
        }
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

    // スプリンクラーなど「エコロケ・サーモの視界を一時的に奪う」ギミック用。
    // 過去の人(Memory)には効果がない(仕様上、視界を奪う対象ではないため)。
    public void TriggerForcedBlackout(float duration)
    {
        if (currentRole == ViewRole.Memory)
        {
            return;
        }

        if (duration > forcedBlackoutTimer)
        {
            forcedBlackoutTimer = duration;
        }

        ApplyVisionMask();
    }

    private void ApplyVisionMask()
    {
        // ここを LayerMask 型のまま扱わないこと。
        // LayerMask への代入は int -> LayerMask の暗黙変換を呼ぶが、
        // この演算子は Udon に公開されておらず
        // 「Method is not exposed to Udon」でU#のコンパイルが丸ごと落ちる。
        // (落ちるとシーンを開いても中身が空で読み込まれるので原因が分かりにくい)
        // LayerMask -> int の向きだけは公開されているので、最初に int にして以降は int で通す。
        int mask = echoCullingMask;
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

            // NowOnly(25) は「今そこにある物」。過去の人の視界からは必ず落とす。
            // インスペクタで誤ってチェックが入っても効かないよう、ここで確実に落としている。
            // 見えないだけでコライダーは残るので、過去の人はぶつかる。
            // それでいい ―― ぶつかる前に他の2人が教えられるかどうかがこのゲーム。
            if (memoryHidesPresent)
            {
                mask = mask & ~(1 << LayerNowOnly);
            }
        }

        bool forcedBlackout = forcedBlackoutTimer > 0f;
        if (forcedBlackout)
        {
            mask = 0;
            blackout = true;
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
