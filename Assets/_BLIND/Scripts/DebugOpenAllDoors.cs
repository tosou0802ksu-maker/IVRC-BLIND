using UdonSharp;
using UnityEngine;

// 【デバッグ専用】ギミックの扉を全部消して、どの部屋へも素通りできるようにする。
//
// ⚠️ 提出・本番アップロードの前に、このスクリプトが付いた
//    「=== SYSTEM === / DEBUG_OpenAllDoors」を削除（または非アクティブに）すること。
//
// なぜ毎秒消し直すのか:
//   扉はそれぞれのギミック（色ボタン・クイズ・鍵・アヒルの祭壇など）が
//   起動時や同期を受け取ったときに「閉じた位置」「表示」に戻す。
//   さらに RoomVisibilityManager が部屋ごと出し入れするので、
//   部屋が出てきたタイミングで扉の Start が走り直すこともある。
//   一度消すだけだと後から閉じ直されるので、1秒おきに消し直す。
//
// 同期しない。各自の画面で消えるだけで、ギミックの状態は変えない。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DebugOpenAllDoors : UdonSharpBehaviour
{
    [Header("消す扉（BLIND/デバッグ から自動で入る）")]
    [SerializeField] private GameObject[] doors;

    [SerializeField] private float interval = 1.0f;

    private float timer;

    void Start()
    {
        HideAll();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < interval) return;
        timer = 0f;
        HideAll();
    }

    private void HideAll()
    {
        if (doors == null) return;
        for (int i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            if (d != null && d.activeSelf) d.SetActive(false);
        }
    }
}
