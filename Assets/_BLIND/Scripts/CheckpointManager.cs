
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// セーブポイント管理。
//
// 仕様:
//   ・赤/青/緑のボタンを押すとその地点がセーブポイントになる
//   ・誰か1人でも死亡判定になったら「全員」が現在のセーブポイントに戻る
//
// checkpoints[0] はスタート地点。以降 index 1,2,3... がボタン1,2,3に対応する。
// currentIndex だけを同期し、後退はしない(既に進んだ地点より前には戻さない)。
public class CheckpointManager : UdonSharpBehaviour
{
    [Header("セーブポイント(0番はスタート地点)")]
    [SerializeField] private Transform[] checkpoints;

    [Header("進行に応じて開閉する扉(赤開く/赤閉じる など)")]
    [SerializeField] private ToggleDoor[] doors;

    [Header("復帰時の演出(任意・ローカル)")]
    [SerializeField] private GameObject deathEffect;
    [SerializeField] private AudioSource deathSound;

    [Header("死亡時の文字（目の前に数秒出して消す）")]
    [Tooltip("BLIND → 死亡演出 → 文字を表示する板を作る で自動で入る")]
    [SerializeField] private GameObject deathHud;
    [SerializeField] private UnityEngine.UI.Text deathText;
    [Tooltip("文字の後ろに敷く黒い幕（暗転）。空なら暗転しない")]
    [SerializeField] private UnityEngine.UI.Image deathBlackout;
    [Tooltip("暗転の濃さ。1で真っ黒")]
    [Range(0f, 1f)] [SerializeField] private float blackoutAlpha = 1f;
    [Tooltip("罠ごとの文字が空の時に出す文字")]
    [TextArea] [SerializeField] private string defaultDeathMessage = "やられた…";
    [Tooltip("罠ごとの効果音が空の時に鳴らす音（空なら上の Death Sound）")]
    [SerializeField] private AudioClip defaultDeathClip;
    [SerializeField] private float messageSeconds = 2.5f;
    [SerializeField] private float fadeSeconds = 0.6f;
    [Tooltip("顔から文字までの距離(m)")]
    [SerializeField] private float hudDistance = 0.7f;

    [UdonSynced] public int currentIndex;

    private float hudTimer;
    private Color hudColor = Color.white;

    void Start()
    {
        RefreshDoors();

        if (deathText != null) hudColor = deathText.color;
        if (deathHud != null) deathHud.SetActive(false);
    }

    // 同期で currentIndex が更新された時(途中参加・他人がボタンを押した時)
    public override void OnDeserialization()
    {
        RefreshDoors();
    }

    private void RefreshDoors()
    {
        if (doors == null)
        {
            return;
        }

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null)
            {
                doors[i].ApplyState();
            }
        }
    }

    // ------------------------------------------------------------
    // セーブポイント登録
    // ------------------------------------------------------------

    // ボタン側から呼ぶ。index はそのボタンに対応するセーブポイント番号。
    public void SetCheckpoint(int index)
    {
        if (index <= currentIndex)
        {
            return;
        }

        if (checkpoints == null || index >= checkpoints.Length)
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        currentIndex = index;
        RequestSerialization();

        RefreshDoors();
    }

    // ボタン側から呼ぶ「順序を問わない」版。
    // 赤→青→緑の順に回るとは限らない(3方向に分岐したマップなので
    // どのボタンから押しても良い)ため、押した瞬間のボタン地点を
    // そのまま復帰地点にする。index の大小は見ない。
    public void SetCheckpointDirect(int index)
    {
        if (checkpoints == null || index < 0 || index >= checkpoints.Length)
        {
            return;
        }

        if (index == currentIndex)
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        currentIndex = index;
        RequestSerialization();

        RefreshDoors();
    }

    // ------------------------------------------------------------
    // 死亡判定 → 全員復帰
    // ------------------------------------------------------------

    // 罠に触れたクライアントがローカルで呼ぶ。
    public void TriggerDeath()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RespawnAll));
    }

    // 全員のクライアントで実行される。自分自身だけをテレポートさせる。
    // 罠ごとの文字・音を持たないギミック(TriggerDeath)はここに来て、標準の文字と音を出す。
    public void RespawnAll()
    {
        RespawnWith("", null, 1f);
    }

    // 罠ごとの文字と効果音で戻す。空なら標準の文字・音になる。
    // ⚠️ 全員のクライアントで呼ぶこと(HazardZone は自分のネットワークイベントから呼ぶ)。
    // volume: 効果音の大きさ(0〜1)。炎の音などは素材そのものが大きく、耳元で鳴らすと驚くので罠ごとに下げられる。
    public void RespawnWith(string message, AudioClip clip, float volume)
    {
        if (!RespawnLocal()) return;
        ShowDeathMessage(message);

        AudioClip c = clip != null ? clip : defaultDeathClip;
        if (c != null)
        {
            PlayAtHead(c, volume);
        }
        else if (deathSound != null)
        {
            deathSound.Play();
        }
    }

    // 効果音はギミック側で鳴らし済みのとき用（クイズの扉・チェスの間違いボタン）。
    public void RespawnWithMessage(string message)
    {
        if (!RespawnLocal()) return;
        ShowDeathMessage(message);
    }

    private bool RespawnLocal()
    {
        Transform point = GetCurrentPoint();
        if (point == null)
        {
            return false;
        }

        if (Networking.LocalPlayer != null)
        {
            Networking.LocalPlayer.TeleportTo(point.position, point.rotation);
        }

        if (deathEffect != null)
        {
            deathEffect.SetActive(true);
        }
        return true;
    }

    // ------------------------------------------------------------
    // 死亡時の文字（ローカル）
    // ------------------------------------------------------------

    // 文字だけを出す。自分の場所へ戻す WrongButton などからも使う。空なら標準の文字。
    public void ShowDeathMessage(string message)
    {
        if (deathText == null || deathHud == null) return;

        string m = (message != null && message.Length > 0) ? message : defaultDeathMessage;
        if (m == null || m.Length == 0) return;

        deathText.text = m;
        hudTimer = messageSeconds + fadeSeconds;
        deathHud.SetActive(true);
        FollowHead();
        SetHudAlpha(1f);
    }

    public override void PostLateUpdate()
    {
        if (hudTimer <= 0f) return;

        hudTimer -= Time.deltaTime;
        if (hudTimer <= 0f)
        {
            if (deathHud != null) deathHud.SetActive(false);
            return;
        }

        FollowHead();
        SetHudAlpha(hudTimer < fadeSeconds ? hudTimer / fadeSeconds : 1f);
    }

    // 顔の前に置き続ける（テレポートした後も読めるように）
    private void FollowHead()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || deathHud == null) return;
        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        deathHud.transform.SetPositionAndRotation(head.position + head.rotation * Vector3.forward * hudDistance, head.rotation);
    }

    // 文字と暗転の幕を一緒に消していく
    private void SetHudAlpha(float a)
    {
        if (deathText != null)
        {
            Color c = hudColor;
            c.a = a;
            deathText.color = c;
        }
        if (deathBlackout != null)
        {
            deathBlackout.color = new Color(0f, 0f, 0f, a * blackoutAlpha);
        }
    }

    // どこにいても同じ音量で聞こえるよう、耳元で鳴らす
    private void PlayAtHead(AudioClip clip, float volume)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        AudioSource.PlayClipAtPoint(clip, local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, volume);
    }

    private Transform GetCurrentPoint()
    {
        if (checkpoints == null || checkpoints.Length == 0)
        {
            return null;
        }

        int index = currentIndex;
        if (index < 0)
        {
            index = 0;
        }
        if (index >= checkpoints.Length)
        {
            index = checkpoints.Length - 1;
        }

        return checkpoints[index];
    }
}
