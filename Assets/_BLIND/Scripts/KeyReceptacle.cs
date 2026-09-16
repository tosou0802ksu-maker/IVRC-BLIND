using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 扉の横の「鍵をはめる操作盤」。
//
// room11（だるま部屋）の出口の扉は最初から閉まっていて、
// 棚から持ってきた鍵をこの盤にはめると開く。
//
// ---------------------------------------------------------------
// 一度開いたら二度と閉まらない
// ---------------------------------------------------------------
// unlocked は [UdonSynced] で、**false に戻す処理をどこにも書いていない**。
// だるま部屋は「行って帰ってくる」道順なので、戻ってきたときにまた
// 鍵を探させるとテンポが死ぬ。後から入ってきたプレイヤーにも
// OnDeserialization 経由で開いた状態が届く。
//
// ---------------------------------------------------------------
// 判定するのは「運んでいる本人だけ」
// ---------------------------------------------------------------
// ⚠️ 全員が距離を測ると、同じフレームに3人が所有権を要求して取り合いになる。
//    CarryableItem.IsHeld() は OnPickup/OnDrop 由来なので**掴んだ本人の
//    クライアントでしか true にならない**。これをそのまま門番に使う。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class KeyReceptacle : UdonSharpBehaviour
{
    [Header("受け付ける鍵")]
    [SerializeField] private CarryableItem key;

    [Header("鍵がはまる位置と向き")]
    [SerializeField] private Transform slotAnchor;

    [Tooltip("鍵をこの距離まで近づけるとはまる(m)。腕を伸ばせば届く程度にする。")]
    [SerializeField] private float insertRadius = 0.30f;

    [Header("開く扉")]
    [SerializeField] private Transform doorLeaf;
    [SerializeField] private Vector3 closedLocalPos;
    [SerializeField] private Vector3 openLocalPos;
    [Tooltip("開き切るまでの秒数。")]
    [SerializeField] private float slideSeconds = 1.6f;

    [Header("開いたら差し替える材質（並びは1対1）")]
    [SerializeField] private MeshRenderer[] lampRenderers;
    [SerializeField] private Material[] lampOpenMaterials;

    [Header("開いた瞬間に光らせるエコロケ受信機")]
    [SerializeField] private EchoReceiver[] echoes;

    [Header("開いたら「ころんだ」を終わらせるだるま")]
    // 扉が開いた＝この部屋はクリア。以降だるまは追従するだけになる。
    // 鍵を持って戻る途中でまた止まらされると、待ち時間にしかならない。
    [SerializeField] private DarumaWatcher watcher;

    [Header("音(任意)")]
    [SerializeField] private AudioSource unlockSound;

    [UdonSynced] private bool unlocked;

    private bool applied;       // 解錠の演出を流したか
    private float slide;        // 0=閉 1=開

    void Start()
    {
        if (doorLeaf != null)
        {
            doorLeaf.localPosition = closedLocalPos;
        }

        // 途中参加で「もう開いている」場合は、動かさずに開いた形で置く。
        // 誰も居ない所で扉がひとりでに動くのは事故に見える。
        if (unlocked)
        {
            Apply(true);
        }
    }

    void Update()
    {
        if (unlocked && !applied)
        {
            Apply(false);
        }

        if (applied && slide < 1f && slideSeconds > 0.01f)
        {
            slide = Mathf.Min(1f, slide + Time.deltaTime / slideSeconds);
            if (doorLeaf != null)
            {
                doorLeaf.localPosition = Vector3.Lerp(closedLocalPos, openLocalPos,
                                                      Mathf.SmoothStep(0f, 1f, slide));
            }
            return;
        }

        if (unlocked || key == null || slotAnchor == null)
        {
            return;
        }

        // 運んでいる本人のクライアントでしか true にならない。
        if (!key.IsHeld())
        {
            return;
        }

        Vector3 d = key.transform.position - slotAnchor.position;
        if (d.sqrMagnitude > insertRadius * insertRadius)
        {
            return;
        }

        VRCPlayerApi me = Networking.LocalPlayer;
        if (Utilities.IsValid(me) && !Networking.IsOwner(me, gameObject))
        {
            Networking.SetOwner(me, gameObject);
        }
        unlocked = true;
        RequestSerialization();
        Apply(false);
    }

    private void Apply(bool instant)
    {
        applied = true;
        slide = instant ? 1f : 0f;

        if (instant && doorLeaf != null)
        {
            doorLeaf.localPosition = openLocalPos;
        }

        if (key != null)
        {
            key.Seat(slotAnchor);
        }

        if (watcher != null)
        {
            watcher.SetCleared();
        }

        // 盤の表示灯を「開」に差し替える。過去人・サーモで別の材質を入れている。
        if (lampRenderers != null && lampOpenMaterials != null)
        {
            int n = Mathf.Min(lampRenderers.Length, lampOpenMaterials.Length);
            for (int i = 0; i < n; i++)
            {
                if (lampRenderers[i] != null && lampOpenMaterials[i] != null)
                {
                    lampRenderers[i].sharedMaterial = lampOpenMaterials[i];
                }
            }
        }

        // エコロケ役にも「今開いた」を伝える。音を待たずに一度光らせる。
        if (!instant && echoes != null)
        {
            for (int i = 0; i < echoes.Length; i++)
            {
                if (echoes[i] != null)
                {
                    echoes[i].TriggerGlowWithDelay(0f, 0.5f);
                }
            }
        }

        if (!instant && unlockSound != null)
        {
            unlockSound.Play();
        }
    }
}
