using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 2つの感圧板が両方踏まれている間だけドア1枚を開く。DualPressurePlate から呼ばれる。
///
/// 遊び方: 2人が板を踏み、1人が中に入って様子を見る。これを交代しながら、
/// それぞれの視点で中を理解して会話し、奥のボタンにたどり着く。
///
/// 同期はしない。プレイヤーの出入りは全員のクライアントで起きるので、
/// 各クライアントが自分の画面で判定すれば全員同じ結果になる。
/// （扉の当たり判定も各自の画面のものなので、通る人の画面で開いていれば通れる）
///
/// 1〜2人で遊ぶとき(体験会・審査用)は、板1枚で開く。
/// さらに、板から降りて扉まで歩く間に閉まらないよう、降りてから smallGroupCloseDelay 秒は開けておく。
/// （板と扉は 0.6m しか離れていないが、1人だと「踏みながら通る」ことができないため）
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DualPressurePlateDoor : UdonSharpBehaviour
{
    [Header("ドア（localPosition で指定）")]
    [SerializeField] private Transform targetDoor;
    [SerializeField] private Vector3 doorClosedPos;
    [SerializeField] private Vector3 doorOpenPos;
    [Tooltip("扉が動く速さ(m/秒)。0 なら瞬間移動")]
    [SerializeField] private float moveSpeed = 4f;

    [Header("人数が少ないとき(1〜2人)")]
    [Tooltip("この人数以下なら、板1枚で開く")]
    [SerializeField] private int smallGroupMaxPlayers = 2;
    [Tooltip("板から降りてから閉まるまでの秒数(人数が少ないときだけ)")]
    [SerializeField] private float smallGroupCloseDelay = 3f;

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    [SerializeField] private AudioSource audioSource;

    private bool[] occupied = new bool[2];
    private bool wasOpen;
    private bool moving;
    private float closeTimer;

    void Start()
    {
        if (targetDoor != null) targetDoor.localPosition = doorClosedPos;
    }

    // DualPressurePlate から呼ばれる。その板に1人以上乗っているか。
    public void SetPlateOccupied(int plateId, bool value)
    {
        if (plateId < 0 || plateId > 1) return;

        occupied[plateId] = value;
        Refresh();
    }

    public override void OnPlayerJoined(VRCPlayerApi player) { Refresh(); }
    public override void OnPlayerLeft(VRCPlayerApi player) { Refresh(); }

    private bool IsSmallGroup()
    {
        return VRCPlayerApi.GetPlayerCount() <= smallGroupMaxPlayers;
    }

    private void Refresh()
    {
        bool pressed = IsSmallGroup() ? (occupied[0] || occupied[1]) : (occupied[0] && occupied[1]);

        if (pressed)
        {
            closeTimer = 0f;
            SetOpen(true);
        }
        else if (wasOpen)
        {
            if (IsSmallGroup() && smallGroupCloseDelay > 0f)
            {
                closeTimer = smallGroupCloseDelay;   // 少し待ってから閉める(Update)
            }
            else
            {
                SetOpen(false);
            }
        }
    }

    private void SetOpen(bool isOpen)
    {
        if (isOpen == wasOpen) return;
        wasOpen = isOpen;

        PlaySound(isOpen ? openClip : closeClip);
        moving = true;
    }

    void Update()
    {
        if (closeTimer > 0f)
        {
            closeTimer -= Time.deltaTime;
            if (closeTimer <= 0f)
            {
                closeTimer = 0f;
                SetOpen(false);
            }
        }

        if (!moving || targetDoor == null) return;

        Vector3 goal = wasOpen ? doorOpenPos : doorClosedPos;
        if (moveSpeed <= 0f)
        {
            targetDoor.localPosition = goal;
        }
        else
        {
            targetDoor.localPosition = Vector3.MoveTowards(targetDoor.localPosition, goal, moveSpeed * Time.deltaTime);
        }

        if (targetDoor.localPosition == goal) moving = false;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
