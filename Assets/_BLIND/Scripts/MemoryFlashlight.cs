using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// 過去の人（Memory）にだけ懐中電灯を持たせる。
//
// なぜ必要か:
//   過去の人の視界は「普通に世界が見える」役で、3役のうちいちばん情報が多い。
//   部屋を暗くして懐中電灯の円の中しか見えないようにすると、
//   「見えているが、見たい所しか見られない」という制約が生まれ、
//   エコロケ役・サーモ役に「どこを照らせばいいか」を訊く理由ができる。
//
// 何をするか:
//   ・自分の役が過去の人のときだけ、手（VR）か頭（デスクトップ）に懐中電灯を追従させる。
//   ・役がシャッフルで替わったら、次のフレームで自動的に消える／点く。
//
// 設計上の注意:
//   ・**同期しない。各クライアントがローカルに表示する。**
//     他の2役は Default レイヤーを見ていない（サーモ22 / エコロケ23 だけ）ので、
//     過去の人の懐中電灯を同期して他人の画面に出しても誰にも見えない。
//     同期すると帯域を食うだけで何も得がない。
//   ・役は SetRole で「押し込まれる」が、ここでは毎フレーム GetRole を**読みに行く**。
//     シャッフル側にこのスクリプトへの配線を足さずに済み、
//     シャッフルの実装が変わっても取りこぼさない。int 比較1回なので負荷は無視できる。
//   ・追従は PostLateUpdate で行う。Update で読むと、VRChat がその後で
//     手の位置を更新するため、懐中電灯が1フレーム遅れて手から浮いて見える。
//   ・懐中電灯は**手に付ける**（VR もデスクトップも）。
//     ただしデスクトップの「手」はアバターの腕が体の横に垂れているだけで、
//     どこも指していない。手の向きをそのまま使うと光が床や壁を勝手に照らす。
//     デスクトップだけ「位置は手、向きは視線」に分ける。
//     腰の高さから視線の先を照らす形になり、懐中電灯を低く構えた見え方になる。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MemoryFlashlight : UdonSharpBehaviour
{
    [Header("役の判定元")]
    [SerializeField] private PlayerVisionController visionController;

    [Header("懐中電灯（モデルと光をまとめた親）")]
    [Tooltip("過去の人でないときはこれごと非表示にする。")]
    [SerializeField] private GameObject body;

    [Header("VR: 右手からのずらし")]
    [Tooltip("手首の位置から、握った懐中電灯の中心までのずれ(手のローカル座標, m)。")]
    [SerializeField] private Vector3 vrOffset = new Vector3(0.0f, -0.02f, 0.08f);
    [Tooltip("手の向きに対する懐中電灯の向き。手の甲側が上、指先が前の想定。")]
    [SerializeField] private Vector3 vrEuler = new Vector3(0f, 0f, 0f);

    [Header("デスクトップ: 手の位置からのずらし（向きは視線）")]
    [Tooltip("手の位置から、視線の向きで見たずれ(m)。少し前に出すと体に埋まらない。")]
    [SerializeField] private Vector3 desktopOffset = new Vector3(0.0f, 0.05f, 0.15f);
    [SerializeField] private Vector3 desktopEuler = new Vector3(0f, 0f, 0f);

    private VRCPlayerApi local;
    private bool shown;
    private bool inVR;

    void Start()
    {
        local = Networking.LocalPlayer;
        if (local != null) inVR = local.IsUserInVR();
        SetShown(false);
    }

    public override void PostLateUpdate()
    {
        bool want = visionController != null
                 && visionController.GetRole() == ViewRole.Memory
                 && local != null;

        if (want != shown) SetShown(want);
        if (!shown) return;

        if (inVR)
        {
            var hand = local.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            Quaternion r = hand.rotation;
            body.transform.SetPositionAndRotation(hand.position + r * vrOffset,
                                                  r * Quaternion.Euler(vrEuler));
        }
        else
        {
            // 位置は手、向きは視線
            var hand = local.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            var head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Quaternion r = head.rotation;
            body.transform.SetPositionAndRotation(hand.position + r * desktopOffset,
                                                  r * Quaternion.Euler(desktopEuler));
        }
    }

    private void SetShown(bool on)
    {
        shown = on;
        if (body != null) body.SetActive(on);
    }
}
