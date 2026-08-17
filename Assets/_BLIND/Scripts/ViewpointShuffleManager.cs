
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 3人のプレイヤーの視点役割(エコロケ/サーモ/過去の人)をシャッフルする。
//
// 各プレイヤーの役割は「playerIdの昇順で自分が何番目か(rank)」と
// 同期された roleOffset から、各クライアントがローカルで
//   role = (rank + roleOffset) % 3
// として計算する。役割の実体を同期する必要はなく、roleOffset 1個の
// 同期だけで全員が矛盾なく同じ役割分担にたどり着ける。
//
// ShuffleRoles() は ShuffleSequenceManager からのみ呼ばれる想定。
public class ViewpointShuffleManager : UdonSharpBehaviour
{
    [Header("このクライアントのローカル視点コントローラ")]
    [SerializeField] private PlayerVisionController localVisionController;

    [Header("デバッグ(1人でのエディタ確認用)")]
    [Tooltip("オンにするとキーを押すたびに エコロケ→サーモ→過去の人 と自分の視点を切り替えられる。" +
             "1人だと役割が固定されてしまうため、テスト時だけオンにする。")]
    [SerializeField] private bool enableDebugRoleSwitch;
    [SerializeField] private KeyCode debugRoleSwitchKey = KeyCode.R;

    [UdonSynced] public int roleOffset;

    // デバッグ切り替えで上書きした役割。-1 なら未使用(通常の計算に従う)。
    private int debugRole = -1;

    void Start()
    {
        ApplyRoleForLocalPlayer();
    }

    void Update()
    {
        if (!enableDebugRoleSwitch)
        {
            return;
        }

        if (Input.GetKeyDown(debugRoleSwitchKey))
        {
            debugRole = (debugRole + 1) % 3;

            if (localVisionController != null)
            {
                localVisionController.SetRole((ViewRole)debugRole);
                Debug.Log("[BLIND] デバッグ視点切り替え -> " + (ViewRole)debugRole);
            }
        }
    }

    public override void OnDeserialization()
    {
        ApplyRoleForLocalPlayer();
    }

    public void ShuffleRoles()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        roleOffset = (roleOffset + 1) % 3;
        RequestSerialization();

        ApplyRoleForLocalPlayer();
    }

    private void ApplyRoleForLocalPlayer()
    {
        if (localVisionController == null)
        {
            return;
        }

        // デバッグで手動切り替え中は、同期由来の再計算で上書きしない
        if (enableDebugRoleSwitch && debugRole >= 0)
        {
            localVisionController.SetRole((ViewRole)debugRole);
            return;
        }

        int rank = GetLocalPlayerRank();
        if (rank < 0)
        {
            return;
        }

        int roleValue = (rank + roleOffset) % 3;
        localVisionController.SetRole((ViewRole)roleValue);
    }

    // playerIdの昇順で自分が何番目(0始まり)かを返す。
    private int GetLocalPlayerRank()
    {
        if (Networking.LocalPlayer == null)
        {
            return -1;
        }

        VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(players);

        int localId = Networking.LocalPlayer.playerId;
        int rank = 0;

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null)
            {
                continue;
            }

            if (players[i].playerId < localId)
            {
                rank++;
            }
        }

        return rank;
    }
}
