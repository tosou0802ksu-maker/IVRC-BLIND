using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 「決まった物を全部ここへ運んでくると扉が開く」台。
//
// room6（プール部屋）で使う。熱を持ったアヒルを3体、台の受け皿に置くと
// room7 へ抜ける扉が開く。
//
// KeyReceptacle（鍵1本）を複数個に広げたもの。違いは次の2点。
//   ・物が複数あるので、どれが置かれたかを [UdonSynced] のビットで持つ
//   ・置く場所は**物ごとに決め打ち**（items[i] は必ず slots[i] に座る）
//
// ⚠️ 「一番近い空いている受け皿に入れる」にしてはいけない。
//    誰がどの順で置いたかはクライアントごとに数フレームずれるので、
//    **同じアヒルが人によって違う皿に乗って見える**。
//    物と皿を1対1に固定すれば、どの順に置いても全員同じ絵になる。
//    プレイヤーは台に近づけるだけでよく、どの皿かを考える必要は無い。
//
// ---------------------------------------------------------------
// 一度そろえたら二度と戻らない
// ---------------------------------------------------------------
// placedMask を 0 に戻す処理はどこにも書いていない。
// クリア後に扉が閉まると、戻ってきた3人がもう一度アヒルを探すことになる。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ItemAltar : UdonSharpBehaviour
{
    [Header("運んでくる物と、それぞれの置き場所（並びは1対1）")]
    [SerializeField] private CarryableItem[] items;
    [SerializeField] private Transform[] slots;

    [Header("台の中心。ここへ近づけると置いたことになる")]
    [SerializeField] private Transform altarCenter;
    [Tooltip("この距離まで近づけると受け皿に座る(m)。皿を狙わせない。")]
    [SerializeField] private float snapRadius = 1.20f;

    [Header("開く扉")]
    [SerializeField] private Transform doorLeaf;
    [SerializeField] private Vector3 closedLocalPos;
    [SerializeField] private Vector3 openLocalPos;
    [SerializeField] private float slideSeconds = 1.8f;

    [Header("受け皿の表示（置かれたら差し替える。並びは slots と1対1）")]
    [SerializeField] private MeshRenderer[] slotMarksDefault;
    [SerializeField] private MeshRenderer[] slotMarksThermal;
    [SerializeField] private Material filledDefault;
    [SerializeField] private Material filledThermal;

    [Header("動いた瞬間に光らせるエコロケ受信機")]
    [SerializeField] private EchoReceiver[] echoes;

    [Header("音(任意)")]
    [SerializeField] private AudioSource placeSound;
    [SerializeField] private AudioSource openSound;

    [UdonSynced] private int placedMask;

    private int shownMask;
    private bool opened;
    private float slide;

    void Start()
    {
        if (doorLeaf != null)
        {
            doorLeaf.localPosition = closedLocalPos;
        }

        // 途中参加で既にそろっていたら、動かさずに開いた形で置く。
        if (placedMask != 0)
        {
            Apply(true);
        }
    }

    void Update()
    {
        if (shownMask != placedMask)
        {
            Apply(false);
        }

        if (opened && slide < 1f && slideSeconds > 0.01f)
        {
            slide = Mathf.Min(1f, slide + Time.deltaTime / slideSeconds);
            if (doorLeaf != null)
            {
                doorLeaf.localPosition = Vector3.Lerp(closedLocalPos, openLocalPos,
                                                      Mathf.SmoothStep(0f, 1f, slide));
            }
        }

        if (items == null || altarCenter == null)
        {
            return;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if ((placedMask & (1 << i)) != 0) continue;
            var it = items[i];
            if (it == null) continue;

            // ⚠️ 判定するのは**運んでいる本人のクライアントだけ**。
            //    IsHeld() は OnPickup/OnDrop 由来なので掴んだ本人しか true にならない。
            //    全員が測ると同じフレームに3人が所有権を取り合う。
            if (!it.IsHeld()) continue;

            Vector3 d = it.transform.position - altarCenter.position;
            if (d.sqrMagnitude > snapRadius * snapRadius) continue;

            VRCPlayerApi me = Networking.LocalPlayer;
            if (Utilities.IsValid(me) && !Networking.IsOwner(me, gameObject))
            {
                Networking.SetOwner(me, gameObject);
            }
            placedMask |= (1 << i);
            RequestSerialization();
            Apply(false);
            return;                         // 1フレームに1体で十分
        }
    }

    private void Apply(bool instant)
    {
        int full = (1 << items.Length) - 1;
        bool wasOpen = opened;

        for (int i = 0; i < items.Length; i++)
        {
            bool on = (placedMask & (1 << i)) != 0;
            if (on == ((shownMask & (1 << i)) != 0) && shownMask != 0) continue;
            if (!on) continue;

            if (items[i] != null && slots != null && i < slots.Length)
            {
                items[i].Seat(slots[i]);
            }
            if (slotMarksDefault != null && i < slotMarksDefault.Length
                && slotMarksDefault[i] != null && filledDefault != null)
            {
                slotMarksDefault[i].sharedMaterial = filledDefault;
            }
            if (slotMarksThermal != null && i < slotMarksThermal.Length
                && slotMarksThermal[i] != null && filledThermal != null)
            {
                slotMarksThermal[i].sharedMaterial = filledThermal;
            }
        }

        shownMask = placedMask;

        if (!instant && placeSound != null && placedMask != full)
        {
            placeSound.Play();
        }

        // エコロケ役にも「今1体入った」を伝える。パルス待ちだと見逃す。
        if (!instant && echoes != null)
        {
            for (int i = 0; i < echoes.Length; i++)
            {
                if (echoes[i] != null) echoes[i].TriggerGlowWithDelay(0f, 0.45f);
            }
        }

        if (placedMask != full)
        {
            return;
        }

        opened = true;
        if (instant)
        {
            slide = 1f;
            if (doorLeaf != null) doorLeaf.localPosition = openLocalPos;
        }
        else if (!wasOpen)
        {
            slide = 0f;
            if (openSound != null) openSound.Play();
        }
    }
}
