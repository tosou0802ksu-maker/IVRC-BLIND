
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
// 入退室で rank が変わるので、OnPlayerJoined / OnPlayerLeft でも計算し直す。
// (これをしないと、誰かが抜けた後に入ってきた人が、残っている人と同じ役割になる)
//
// シャッフルの入口は2つ。
//   ShuffleForColor(colorId) … セーブポイントのボタン(ColorSignalButton)から。各色の初回だけ。
//   ShuffleRoles()           … ShuffleSequenceManager から(現在シーンにボタンが無く未使用)。
//   手元の操作(下記)         … 1〜2人で遊ぶときだけ。いつでも何度でも切り替えられる。
//
// 1〜2人で遊ぶとき(体験会・審査用):
//   デスクトップは switchKey(既定 Q)、VR は右スティックを上に倒すと視点が切り替わる。
//   1人なら エコロケ→サーモ→過去の人 の順に1つずつ。
//   2人なら +1 か +2 をランダムに選ぶ(2人とも必ず前と違う視点になる)。
//   3人いるときは使えない(視点は3人で1つずつ持つのが本来の遊び方のため)。
public class ViewpointShuffleManager : UdonSharpBehaviour
{
    [Header("このクライアントのローカル視点コントローラ")]
    [SerializeField] private PlayerVisionController localVisionController;

    [Header("デバッグ(1人でのエディタ確認用)")]
    [Tooltip("オンにするとキーを押すたびに エコロケ→サーモ→過去の人 と自分の視点を切り替えられる。" +
             "1人だと役割が固定されてしまうため、テスト時だけオンにする。")]
    [SerializeField] private bool enableDebugRoleSwitch;
    [SerializeField] private KeyCode debugRoleSwitchKey = KeyCode.R;

    [Header("1〜2人で遊ぶときの手元の切り替え")]
    [Tooltip("この人数以下のときだけ、手元の操作で視点を切り替えられる")]
    [SerializeField] private int freeSwitchMaxPlayers = 2;
    [Tooltip("デスクトップのキー。VRChat が使っていないキーにすること(WASD/Space/Shift/C/Z/R/V/T などは不可)")]
    [SerializeField] private KeyCode switchKey = KeyCode.Q;
    [Tooltip("連打しても壊れないよう、次に切り替えられるまでの秒数")]
    [SerializeField] private float switchCooldown = 0.5f;

    [UdonSynced] public int roleOffset;

    // ShuffleForColor で、どの色でシャッフル済みかのビット(赤=1, 青=2, 緑=4)。
    // roleOffset と同じ同期で送るので、後から押した人・途中参加の人も「済み」が分かる。
    [UdonSynced] public int shuffledColorFlags;

    // デバッグ切り替えで上書きした役割。-1 なら未使用(通常の計算に従う)。
    private int debugRole = -1;

    // GetPlayers 用の使い回し配列(呼ぶたびに new しないため)。足りなくなったら作り直す。
    private VRCPlayerApi[] playersBuffer = new VRCPlayerApi[16];

    private float switchCooldownTimer;
    private bool stickNeutral = true;

    void Start()
    {
        ApplyRoleForLocalPlayer();
    }

    void Update()
    {
        if (switchCooldownTimer > 0f) switchCooldownTimer -= Time.deltaTime;

        if (Input.GetKeyDown(switchKey))
        {
            _FreeSwitch();
        }

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

    // VR: 右スティックを上に倒す(一度倒したら、真ん中に戻すまで次は来ない)
    public override void InputLookVertical(float value, VRC.Udon.Common.UdonInputEventArgs args)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || !local.IsUserInVR()) return;

        if (value < 0.3f)
        {
            stickNeutral = true;
            return;
        }
        if (value > 0.8f && stickNeutral)
        {
            stickNeutral = false;
            _FreeSwitch();
        }
    }

    // 1〜2人のときだけ、手元の操作で視点を切り替える。
    // 名前を _ で始めているのは、ネットワークイベントで他人から呼ばれないようにするため
    // (public なのは、エディタでの動作確認で SendCustomEvent から呼べるようにするため)。
    public void _FreeSwitch()
    {
        if (switchCooldownTimer > 0f) return;
        int count = VRCPlayerApi.GetPlayerCount();
        if (count > freeSwitchMaxPlayers) return;

        switchCooldownTimer = switchCooldown;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (count <= 1)
        {
            roleOffset = (roleOffset + 1) % 3;   // 1人: 順番に1つずつ
        }
        else
        {
            AdvanceRoleOffset();                 // 2人: 2人とも必ず変わる
        }
        RequestSerialization();

        ApplyRoleForLocalPlayer();
    }

    // 自分が入室したときは、すでにいる全員(自分を含む)の分だけ呼ばれる。
    // 入ってくる人の playerId は既存の全員より大きいので既存の人の rank は変わらないが、
    // Start の時点で人数がそろっているとは限らないので、ここでも計算し直す。
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        ApplyRoleForLocalPlayer();
    }

    // 抜けた人より playerId が大きい人は rank が1つ繰り上がるので計算し直す。
    // この時点で GetPlayers に抜けた人がまだ含まれるかは公式に書かれていないため、明示的に除く。
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        int leftId = player != null ? player.playerId : -1;
        ApplyRoleForLocalPlayerExcluding(leftId);
    }

    public void ShuffleRoles()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        AdvanceRoleOffset();
        RequestSerialization();

        ApplyRoleForLocalPlayer();
    }

    // セーブポイントのボタン(ColorSignalButton)から呼ばれる。
    // その色で初めて呼ばれたときだけシャッフルする(赤=0, 青=1, 緑=2)。
    // ColorSignalButton の「押した」印は各クライアントごとなので、
    // ここで全体の「済み」を見ないと、同じ色を3人が押すと3回シャッフルされてしまう。
    public void ShuffleForColor(int colorId)
    {
        if (colorId < 0 || colorId > 2)
        {
            return;
        }

        int bit = 1 << colorId;
        if ((shuffledColorFlags & bit) != 0)
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        shuffledColorFlags |= bit;
        AdvanceRoleOffset();
        RequestSerialization();

        ApplyRoleForLocalPlayer();
    }

    private void AdvanceRoleOffset()
    {
        // +1 か +2 をランダムに選ぶ(+0 は選ばない)。
        // 3人の役割の入れ替え方のうち「全員の役割が必ず変わる」のはこの2通りだけ。
        // Random.Range(int, int) は上限を含まないので 1 か 2 が返る。
        roleOffset = (roleOffset + Random.Range(1, 3)) % 3;
    }

    private void ApplyRoleForLocalPlayer()
    {
        ApplyRoleForLocalPlayerExcluding(-1);
    }

    // excludedPlayerId: rank の計算から除く playerId。除かないときは -1。
    private void ApplyRoleForLocalPlayerExcluding(int excludedPlayerId)
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

        int rank = GetLocalPlayerRank(excludedPlayerId);
        if (rank < 0)
        {
            return;
        }

        int roleValue = (rank + roleOffset) % 3;
        localVisionController.SetRole((ViewRole)roleValue);
    }

    // playerIdの昇順で自分が何番目(0始まり)かを返す。excludedPlayerId の人は数えない。
    private int GetLocalPlayerRank(int excludedPlayerId)
    {
        if (Networking.LocalPlayer == null)
        {
            return -1;
        }

        int count = VRCPlayerApi.GetPlayerCount();
        if (playersBuffer == null || playersBuffer.Length < count)
        {
            playersBuffer = new VRCPlayerApi[count * 2];
        }
        VRCPlayerApi.GetPlayers(playersBuffer);

        int localId = Networking.LocalPlayer.playerId;
        int rank = 0;

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = playersBuffer[i];
            if (p == null)
            {
                continue;
            }

            int id = p.playerId;
            if (id == excludedPlayerId)
            {
                continue;
            }

            if (id < localId)
            {
                rank++;
            }
        }

        return rank;
    }
}
