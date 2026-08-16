
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 「2つ踏むと開くドア」。
//
// 3人のうち2人が感圧板に乗っている間だけ扉が開く。
// 残りの1人が先へ進むことになるので、
// 板を踏んでいる2人が見えているものを口頭で渡す必要が出る。
//
// 板の上にいる人数はPressurePlate側でローカル集計しており全員一致するため、
// この扉も同期なしで全クライアントが同じ開閉状態になる。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PressurePlateDoor : UdonSharpBehaviour
{
    [Header("この扉に対応する感圧板")]
    [SerializeField] private PressurePlate[] plates;

    [Header("開くのに必要な枚数")]
    [SerializeField] private int requiredCount = 2;

    [Header("扉の実体(閉じている時に表示・当たり判定を持つもの)")]
    [SerializeField] private GameObject doorBody;

    [Header("一度開いたら開きっぱなしにする")]
    [Tooltip("オフにすると板から降りた瞬間に閉まる。")]
    [SerializeField] private bool stayOpen;

    [Header("開閉音(任意)")]
    [SerializeField] private AudioSource moveSound;

    private bool isOpen;
    private bool initialized;

    void Start()
    {
        RefreshDoor();
    }

    // PressurePlate から呼ばれる
    public void RefreshDoor()
    {
        if (stayOpen && isOpen)
        {
            return;
        }

        int pressed = 0;
        if (plates != null)
        {
            for (int i = 0; i < plates.Length; i++)
            {
                if (plates[i] != null && plates[i].IsPressed())
                {
                    pressed++;
                }
            }
        }

        bool shouldOpen = pressed >= requiredCount;

        if (initialized && shouldOpen == isOpen)
        {
            return;
        }

        if (doorBody != null)
        {
            doorBody.SetActive(!shouldOpen);
        }

        if (initialized && moveSound != null)
        {
            moveSound.Play();
        }

        isOpen = shouldOpen;
        initialized = true;
    }
}
