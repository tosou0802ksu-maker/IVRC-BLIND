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

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    [SerializeField] private AudioSource audioSource;

    private bool[] occupied = new bool[2];
    private bool wasOpen;
    private bool moving;

    void Start()
    {
        if (targetDoor != null) targetDoor.localPosition = doorClosedPos;
    }

    // DualPressurePlate から呼ばれる。その板に1人以上乗っているか。
    public void SetPlateOccupied(int plateId, bool value)
    {
        if (plateId < 0 || plateId > 1) return;

        occupied[plateId] = value;

        bool isOpen = occupied[0] && occupied[1];
        if (isOpen == wasOpen) return;
        wasOpen = isOpen;

        PlaySound(isOpen ? openClip : closeClip);
        moving = true;
    }

    void Update()
    {
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
