using UdonSharp;
using UnityEngine;

/// <summary>
/// 全ての部屋を登録しておき、「今見せたい部屋だけ」ONにして残りをOFFにする管理スクリプト。
/// このスクリプトを付けるオブジェクトは、OFFにされる部屋の中には置かないこと
/// (自分自身がOFFになると動かなくなるため)。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoomVisibilityManager : UdonSharpBehaviour
{
    [Tooltip("制御したい部屋(room1〜room19など)を全て登録")]
    public GameObject[] allRooms;

    [Tooltip("ワールド開始時に表示しておく部屋(スポーン部屋とその隣など)")]
    public GameObject[] initialRooms;

    [Tooltip("オンにすると、切り替え結果をConsoleへログ出力する(動作確認用)")]
    public bool debugLog = true;

    void Start()
    {
        ShowOnly(initialRooms);
    }

    // 渡された部屋だけをONにし、allRoomsのうちそれ以外をOFFにする
    public void ShowOnly(GameObject[] visibleRooms)
    {
        if (visibleRooms == null) return;

        // allRoomsに登録されていない部屋が渡されていないかチェック(登録漏れの検出)
        if (debugLog)
        {
            for (int j = 0; j < visibleRooms.Length; j++)
            {
                if (visibleRooms[j] == null)
                {
                    Debug.LogWarning("[RoomVisibility] visibleRoomsの" + j + "番目が空です");
                    continue;
                }

                bool registered = false;
                for (int i = 0; i < allRooms.Length; i++)
                {
                    if (allRooms[i] == visibleRooms[j])
                    {
                        registered = true;
                        break;
                    }
                }

                if (!registered)
                {
                    Debug.LogWarning("[RoomVisibility] allRoomsに登録されていない部屋です: " + visibleRooms[j].name);
                }
            }
        }

        for (int i = 0; i < allRooms.Length; i++)
        {
            if (allRooms[i] == null) continue;

            bool visible = false;
            for (int j = 0; j < visibleRooms.Length; j++)
            {
                if (allRooms[i] == visibleRooms[j])
                {
                    visible = true;
                    break;
                }
            }

            if (allRooms[i].activeSelf != visible)
            {
                allRooms[i].SetActive(visible);
                if (debugLog)
                {
                    Debug.Log("[RoomVisibility] " + allRooms[i].name + " -> " + (visible ? "表示" : "非表示"));
                }
            }
        }
    }
}
