using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EchoEmitter : UdonSharpBehaviour
{
    [Header("役割判定")]
    [Tooltip("エコロケ役の時だけパルスを発するためのローカル視点コントローラ参照。")]
    [SerializeField] private PlayerVisionController localVisionController;

    [Header("パルス設定")]
    [SerializeField] private float pulseRange = 12f;
    [SerializeField] private float pulseAngle = 55f;

    // 自動パルスの間隔。「点灯している総時間」は
    //   distance * delayPerMeter (届くまで) + distance * delayPerMeter (保持) + glowDuration (減衰)
    // なので、間隔をこれより短くすると前のパルスが消える前に次が来て光りっぱなしになる。
    // 現行値(12m地点)で 0.54 + 0.54 + 0.6 = 1.68秒。間隔2.2秒に対して約0.5秒の暗闇が残る。
    [SerializeField] private float pulseInterval = 2.2f;

    [Header("遅延設定")]
    [SerializeField] private float delayPerMeter = 0.045f;

    [Header("手動パルス")]
    [Tooltip("トリガー(デスクトップでは左クリック)で任意のタイミングでパルスを撃てるようにする。" +
             "自動パルスを待つ時間がそのままストレスになるため、" +
             "「見たい時に見る」操作を与えて待ち時間の体感を消すのが狙い。")]
    [SerializeField] private bool allowManualPulse = true;

    [Tooltip("手動パルスの最短間隔。連打で光りっぱなしになるのを防ぐ。" +
             "暗闇と一瞬の安心感のコントラストが体験の核なので、ここは必ず1秒以上残す。")]
    [SerializeField] private float manualCooldown = 1.1f;

    [Header("遮蔽判定(12:00以降の検証用。既定はオフ)")]
    [Tooltip("オンにすると壁の向こうの輪郭が光らなくなる。" +
             "コライダーが無い面は一切光らなくなるため、実機で確認するまでオフのままにすること。")]
    [SerializeField] private bool occlusionCheck = false;
    // LayerMask 型にしないこと。int -> LayerMask の暗黙変換は Udon に公開されておらず、
    // 初期化子(= ~0)だけでU#のコンパイルが落ちる。-1 は「全レイヤー」の意味。
    [SerializeField] private int occluderMask = -1;

    [Header("エディタテスト用")]
    [SerializeField] private Transform editorCamera;

    [Header("受信オブジェクト一覧")]
    [SerializeField] private EchoReceiver[] receivers;

    [Header("狙っている方向を示す目印(任意)")]
    [Tooltip("VRChatではアバターの手が実物のコントローラの代わりに表示されるだけで、" +
             "どちらに向けてエコーを撃っているかが分かりにくいので、" +
             "コントローラのトラッキング位置に追従させる目印(矢印等)を置く場合はここに設定する。")]
    [SerializeField] private Transform rightHandIndicator;
    [SerializeField] private Transform leftHandIndicator;

    private float timer = 0f;
    private float manualCooldownTimer = 0f;
    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        SetIndicatorsActive(false);
    }

    private bool IsEchoRole()
    {
        // 参照未設定の場合は従来通り誰でも発動できるようにしておく(エディタ単体テスト等)
        if (localVisionController == null) return true;
        return localVisionController.GetRole() == ViewRole.Echo;
    }

    void Update()
    {
        if (!IsEchoRole())
        {
            SetIndicatorsActive(false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("Tキー検知");
            EmitFromEditorCamera();
            return;
        }

        if (localPlayer == null) return;

        // 狙っている方向が分かるよう、パルスの発射有無に関わらず毎フレーム追従させる
        UpdateIndicators();

        if (manualCooldownTimer > 0f) manualCooldownTimer -= Time.deltaTime;

        timer += Time.deltaTime;
        if (timer >= pulseInterval)
        {
            timer = 0f;
            EmitFromBothHands();
        }
    }

    // トリガー(デスクトップでは左クリック)で撃つ手動パルス。
    //
    // 自動パルスだけだと、プレイヤーは「次の光が来るまで何もできずに待つ」状態になる。
    // 暗闇そのものは体験の核だが、"自分では何もできない"待ち時間は
    // 恐怖ではなく手持ち無沙汰になってしまう。撃つ操作を渡すと同じ暗闇が
    // 「自分で選んで踏み込んでいる暗闇」に変わる。
    //
    // クールダウンを1秒以上残してあるのは、連打で光りっぱなしにさせないため。
    // 光が途切れない状態はエコロケ役にとって一番退屈な状態で、
    // かつ「形を伝える」役割の価値そのものを消してしまう。
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!value) return;
        if (!allowManualPulse) return;
        if (!IsEchoRole()) return;
        if (localPlayer == null) return;
        if (manualCooldownTimer > 0f) return;

        manualCooldownTimer = manualCooldown;
        // 自動パルスのタイマーも戻す。手で撃った直後に自動パルスが重なると
        // 二重に光って「撃った手応え」が分からなくなるため。
        timer = 0f;
        EmitFromBothHands();
    }

    private void SetIndicatorsActive(bool active)
    {
        if (rightHandIndicator != null) rightHandIndicator.gameObject.SetActive(active);
        if (leftHandIndicator != null) leftHandIndicator.gameObject.SetActive(active);
    }

    private void UpdateIndicators()
    {
        if (rightHandIndicator != null)
        {
            rightHandIndicator.gameObject.SetActive(true);
            VRCPlayerApi.TrackingData rightTracking =
                localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            rightHandIndicator.SetPositionAndRotation(rightTracking.position, rightTracking.rotation);
        }

        if (leftHandIndicator != null)
        {
            leftHandIndicator.gameObject.SetActive(true);
            VRCPlayerApi.TrackingData leftTracking =
                localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
            leftHandIndicator.SetPositionAndRotation(leftTracking.position, leftTracking.rotation);
        }
    }

    private void EmitFromEditorCamera()
    {
        // localPlayerがいればHeadの骨から位置と向きを取得
        if (localPlayer != null)
        {
            Vector3 headPos = localPlayer.GetBonePosition(HumanBodyBones.Head);
            Quaternion headRot = localPlayer.GetBoneRotation(HumanBodyBones.Head);
            EmitPulse(headPos, headRot * Vector3.forward);
            return;
        }

        // localPlayerがnullならeditorCameraを使う
        if (editorCamera == null) return;
        EmitPulse(editorCamera.position, editorCamera.forward);
    }

    private void EmitFromBothHands()
    {
        // アバターの手ボーン(IK推定)ではなく、実機のコントローラそのものの
        // トラッキング位置・向きを使う。フルボディトラッキング無しでも
        // 実際に持っているコントローラの位置から発生するようにするため。
        VRCPlayerApi.TrackingData rightTracking =
            localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        EmitPulse(rightTracking.position, rightTracking.rotation * Vector3.forward);

        VRCPlayerApi.TrackingData leftTracking =
            localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
        EmitPulse(leftTracking.position, leftTracking.rotation * Vector3.forward);
    }

    private void EmitPulse(Vector3 origin, Vector3 direction)
    {
        if (receivers == null || receivers.Length == 0) return;

        for (int i = 0; i < receivers.Length; i++)
        {
            EchoReceiver receiver = receivers[i];
            if (receiver == null) continue;

            Vector3 toReceiver = receiver.transform.position - origin;
            float distance = toReceiver.magnitude;
            float angle = Vector3.Angle(direction, toReceiver.normalized);

            if (distance > pulseRange) continue;
            if (angle > pulseAngle) continue;

            // 壁の向こうの輪郭まで光ると、エコロケ役は立っているだけで
            // 隣の部屋の間取りまで読めてしまう。サーモ側で距離フェードを入れて
            // 「その場の熱しか読めない」ようにしたのと同じ理由で、本来はここも塞ぐべき。
            // ただしコライダーの無い面は完全に光らなくなるので、実機で確認するまで既定はオフ。
            if (occlusionCheck && distance > 0.01f)
            {
                if (Physics.Raycast(origin, toReceiver.normalized, distance - 0.05f,
                                    occluderMask, QueryTriggerInteraction.Ignore)) continue;
            }

            float startDelay = distance * delayPerMeter;
            float fadeStartDelay = distance * delayPerMeter;
            receiver.TriggerGlowWithDelay(startDelay, fadeStartDelay);
        }
    }
}