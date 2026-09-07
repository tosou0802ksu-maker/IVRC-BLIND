using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// だるまさんがころんだ（room11）。
//
// 大きなだるまが部屋の奥に立っていて、
//   背を向けている間だけ進める → 振り向いたら止まる
// を繰り返す。振り向いている間に動いた人は**名指しされる**。
//
// ⚠️ この部屋では死なない（`punish` 既定 false）。意図した設計。
//    ゲートボタン(＝復帰地点)を押す前の部屋なので、ここで殺すと
//    まだ稼いでいない進行度まで巻き戻る形になり、テンポだけが悪くなる。
//    緊張の担当は落とし穴部屋。ここは「動いたのが誰か分かる」だけで足りる。
//
// **このギミックの肝は「だるまが、動いた人の方を向く」こと。**
// 誰かが捕まったとき、だるまはその人の方へ首を回してそのまま追い続ける。
// 3人は互いの姿が見えない（役ごとに別の世界を見ている）ので、
// 「誰がやったか」を知る手段がこの向きしかない。
// 向きが自分を指していれば自分だと分かるし、
// 他の2人にも「どっちの方向の人がやったか」だけが伝わる。
//
// 同期の考え方:
//   ・位相(phase)と長さ(phaseLength)はオーナーだけが進めて同期する。
//     全員が自前でタイマーを回すと、数秒でズレて「自分だけ捕まる」ようになる。
//   ・回転そのものは同期しない。各クライアントが phase から毎フレーム計算する。
//     Transform を同期するより軽く、遅延で首がカクつくこともない。
//   ・捕まった人だけがオーナーを取って accusedId を書き、位相を「名指し」に進める。
//     誰の方を向くかは VRCPlayerApi.GetPlayerById で全員が同じ答えを出せる。
//
// ⚠️ 同期は **必ず Manual**。既定の Continuous のままだと OnDeserialization が
//    毎フレーム飛んできて、その中の phaseTimer = 0 が延々と効き続ける。
//    結果、オーナー以外の画面では**だるまが振り向きかけたまま固まる**。
//    位相が変わった瞬間だけ知らせたいので、こちらから RequestSerialization する。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DarumaWatcher : UdonSharpBehaviour
{
    // 位相
    private const int PhaseAway = 0;    // 背を向けている＝動いてよい
    private const int PhaseTurn = 1;    // 振り向いている最中＝止まる準備
    private const int PhaseWatch = 2;   // こちらを見ている＝止まれ
    private const int PhaseAccuse = 3;  // 動いた人を指している

    [Header("回すだるま本体（この Transform ごと回る）")]
    [SerializeField] private Transform daruma;

    [Header("見張るときに向く方向の目印。空なら -Z 方向を向く")]
    [SerializeField] private Transform watchTarget;

    [Header("だるまの顔が向いている軸のズレ(度)。モデルによって直す")]
    [SerializeField] private float faceYawOffset = 0.0f;

    [Header("捕まえたときの死亡判定")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("時間(秒)")]
    // ⚠️ 背を向けている時間＝1回に進める距離。長いと**止まらずに着いてしまう**。
    //    部屋は33mあるが、歩き 2m/s なら 4.5〜9秒で 9〜18m 進めてしまい、
    //    2〜3回止まるだけで渡り切れていた（「間隔が長すぎる」と指摘された）。
    //    1.8〜3.8秒なら1回 3.6〜7.6m ＝ **5〜8回は止まらされる**。
    [Tooltip("背を向けている時間。この範囲でランダム。1回に進める距離を決める。")]
    [SerializeField] private float awayMin = 1.8f;
    [SerializeField] private float awayMax = 3.8f;
    [Tooltip("振り向きにかける時間。短すぎると反応できない。")]
    [SerializeField] private float turnDuration = 0.9f;
    [Tooltip("こちらを見ている時間。この範囲でランダム。")]
    [SerializeField] private float watchMin = 1.6f;
    [SerializeField] private float watchMax = 3.2f;
    [Tooltip("振り向き切ってから判定を始めるまでの猶予。止まる余裕。")]
    [SerializeField] private float graceTime = 1.0f;
    [Tooltip("名指ししてから全員リスポーンするまでの間。")]
    [SerializeField] private float accuseDuration = 1.4f;

    [Header("入室してから判定の対象になるまで(秒)")]
    // ⚠️ これが無いと **扉を入った瞬間に死ぬ**。
    //    見張り中(1周の約3割)に部屋へ入ると、歩いている最中の位置がそのまま
    //    基準として固定され、次の瞬間には 0.3m 動いていて即アウトになる。
    //    しかも復帰地点が扉の手前だと、戻る→入る→即死 の無限ループになり、
    //    **確認も調整も一切できなくなる**（実際そうなった）。
    [SerializeField] private float enterGrace = 2.5f;

    [Header("捕まえたときに全員リスポーンさせるか")]
    // ⚠️ 既定は **切ってある**。この部屋で殺さないのは意図した設計。
    //
    //   ・この部屋はゲートボタン(＝復帰地点)を押す**前**にある。
    //     ここで死ぬと、まだ稼いでいない進行度まで巻き戻る形になり、
    //     失うものが大きすぎる割に得るものが無い。
    //   ・そもそも復帰地点 CP_2_Blue がこの部屋の中にあるので、
    //     死んでも部屋の中に戻るだけで罰として成立していない。
    //   ・落とし穴部屋が「一歩ごとに死ぬ」緊張を担当しているので、
    //     ここまで即死だとテンポが単調になる。
    //
    // 捕まったときの手応えは「だるまが自分の方を向いて指す」ことで足りている。
    // 3人は互いが見えないので、**誰が動いたかが分かること自体**が情報になる。
    [Tooltip("既定は切り。入れると捕まったとき全員が復帰地点へ戻る。")]
    [SerializeField] private bool punish = false;

    [Header("動いたと判定する距離(m)")]
    [Tooltip("VRだと立っているだけで頭が数cm動く。0.25〜0.4 が目安。")]
    [SerializeField] private float moveTolerance = 0.30f;

    [Header("名指し中に首を回す速さ(度/秒)")]
    [SerializeField] private float trackSpeed = 220.0f;

    [Header("演出(任意)")]
    [Tooltip("「だるまさんがころんだ」の声。背を向けた瞬間に鳴らす。")]
    [SerializeField] private AudioSource chantSound;
    [Tooltip("捕まえた瞬間の音。")]
    [SerializeField] private AudioSource caughtSound;

    [Header("「今 振り向いた」を見せる")]
    // ⚠️ 振り向きが見えないと、このギミックはただの理不尽な即死になる。
    //    実測(12m/サーモ)では、顔として読める画素は 135 → 918 まで増えるものの、
    //    **増えるのは振り向きの後半 40% だけ**で、前半はほとんど変化が無い。
    //    エコロケ役に至っては、たまたまパルスを撃っていないと**何も見えない**。
    //    そこで、振り向く瞬間そのものに合図を足す。
    [Tooltip("だるまのサーモ用 Renderer。振り向く瞬間だけ熱を上げる。")]
    [SerializeField] private Renderer thermalBody;
    [Tooltip("だるまのエコロケ用受信機。動いた瞬間に光らせる。")]
    [SerializeField] private EchoReceiver[] echoParts;
    [Tooltip("振り向いている間の熱の倍率。")]
    [SerializeField] private float turnHeat = 1.45f;
    [Tooltip("見張っている間の熱の倍率。背を向けている間は 1.0。")]
    [SerializeField] private float watchHeat = 1.15f;

    private MaterialPropertyBlock heatBlock;
    private float shownHeat = 1.0f;

    // サーモ役・エコロケ役への「今こっちを向いた」の伝達は、
    // ここでは何もしない。だるまの顔の位置に熱い板(T_Face / E_Face)を
    // 貼ってあり、向いている間だけ見えて背を向けると胴体に隠れる。
    // 状態を持たないぶん、同期ズレでも絶対に食い違わない。
    // → BlindGimmickBuilder.BuildDarumaWatcher の「顔の板」を参照。

    [UdonSynced] public int phase;
    [UdonSynced] public float phaseLength;
    [UdonSynced] public int accusedId = -1;

    // ローカル
    private float phaseTimer;
    private float awayYaw;      // 背を向けているときの向き
    private float watchYaw;     // 見張るときの向き
    private float currentYaw;
    private Vector3 frozenPos;
    private bool frozenValid;
    private bool localInside;   // ローカルプレイヤーが部屋の中にいるか
    private float insideSince;  // いつ入ったか
    private bool eligible;      // この回の見張りで判定の対象になるか
    private bool deathSent;

    void Start()
    {
        if (daruma == null)
        {
            daruma = transform;
        }

        // 見張る向き＝プレイヤーが来る方向。目印が無ければ -Z。
        Vector3 look = -daruma.forward;
        if (watchTarget != null)
        {
            Vector3 d = watchTarget.position - daruma.position;
            d.y = 0.0f;
            if (d.sqrMagnitude > 0.0001f)
            {
                look = d.normalized;
            }
        }

        watchYaw = Quaternion.LookRotation(look).eulerAngles.y + faceYawOffset;
        awayYaw = watchYaw + 180.0f;
        currentYaw = awayYaw;
        ApplyYaw(currentYaw);

        if (Networking.IsOwner(gameObject))
        {
            EnterPhase(PhaseAway);
        }
    }

    // ------------------------------------------------------------
    // 部屋の内外。判定はこの部屋にいる人だけが対象。
    // ------------------------------------------------------------
    // ⚠️ これが無いと、別の部屋を普通に歩いているだけの人が
    //    「動いた」と判定されて全員リスポーンになる。
    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player != null && player.isLocal)
        {
            if (!localInside)
            {
                insideSince = Time.timeSinceLevelLoad;
            }
            localInside = true;
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player != null && player.isLocal)
        {
            localInside = false;
        }
    }

    // ------------------------------------------------------------
    // 位相
    // ------------------------------------------------------------

    public override void OnDeserialization()
    {
        // オーナーが位相を変えた。こちらはタイマーを0に戻して同じ演出を始める。
        phaseTimer = 0.0f;
        OnPhaseStarted();
    }

    private void EnterPhase(int next)
    {
        phase = next;
        phaseTimer = 0.0f;

        if (next == PhaseAway)
        {
            phaseLength = Random.Range(awayMin, awayMax);
            accusedId = -1;
        }
        else if (next == PhaseTurn)
        {
            phaseLength = turnDuration;
        }
        else if (next == PhaseWatch)
        {
            phaseLength = Random.Range(watchMin, watchMax);
        }
        else
        {
            phaseLength = accuseDuration;
        }

        RequestSerialization();
        OnPhaseStarted();
    }

    // 位相が変わった瞬間に全員のクライアントで走る（同期・オーナー問わず）
    private void OnPhaseStarted()
    {
        frozenValid = false;
        deathSent = false;

        // ⚠️ 判定の対象かどうかは **見張りが始まった瞬間に決める**。
        //    毎フレーム「今 部屋の中か」で見ると、見張り中に扉を入った人が
        //    歩いている最中に基準を取られて即アウトになる（＝入った瞬間に死ぬ）。
        //    始まった時点で既に入っていて、かつ入ってから enterGrace 秒たった人だけ。
        //    途中から入った人は次の回から参加する。
        if (phase == PhaseWatch)
        {
            eligible = localInside
                    && (Time.timeSinceLevelLoad - insideSince) >= enterGrace;
        }

        if (phase == PhaseAway)
        {
            if (chantSound != null)
            {
                chantSound.Play();
            }
        }
        else if (phase == PhaseAccuse)
        {
            if (caughtSound != null)
            {
                caughtSound.Play();
            }

            // ⚠️ 復帰地点(CP_2_Blue)が**この部屋の中**にあるので、
            //    死んでも部屋の外には出ない＝ゾーンから出入りしないため
            //    OnPlayerTriggerEnter が飛ばず、入室の猶予が働かない。
            //    そのまま次の見張りに参加させると、立ち直る前にまた捕まって
            //    永久に先へ進めなくなる。ここで入室時刻を取り直して、
            //    復帰した人にもう一度 enterGrace を与える。
            insideSince = Time.timeSinceLevelLoad;
        }

        // 動き出した瞬間にエコロケを光らせる。
        //
        // ⚠️ エコロケ役はパルスが当たっている間しか物が見えない。
        //    たまたまパルスを撃っていなければ、**振り向きを丸ごと見逃して死ぬ**。
        //    「だるまが動けば音が出る」＝反響が返る、という理屈にも合う。
        if (phase == PhaseTurn || phase == PhaseAccuse)
        {
            FlashEcho();
        }
    }

    private void FlashEcho()
    {
        if (echoParts == null)
        {
            return;
        }

        for (int i = 0; i < echoParts.Length; i++)
        {
            if (echoParts[i] != null)
            {
                // 即座に点灯 → 0.35秒 保持 → フェードアウト。
                // 振り向き(0.45秒)のあいだ点きっぱなしになる長さにしてある。
                echoParts[i].TriggerGlowWithDelay(0.0f, 0.35f);
            }
        }
    }

    // 振り向いている間だけ熱くする。サーモ役への「今動いた」の合図。
    private void UpdateHeat()
    {
        if (thermalBody == null)
        {
            return;
        }

        float want = 1.0f;
        if (phase == PhaseTurn)
        {
            want = turnHeat;
        }
        else if (phase == PhaseWatch)
        {
            want = watchHeat;
        }
        else if (phase == PhaseAccuse)
        {
            want = turnHeat;
        }

        // 立ち上がりは即、戻りはゆっくり。点いた瞬間が目に留まる。
        shownHeat = want > shownHeat ? want
                                     : Mathf.MoveTowards(shownHeat, want, 1.2f * Time.deltaTime);

        if (heatBlock == null)
        {
            heatBlock = new MaterialPropertyBlock();
        }
        thermalBody.GetPropertyBlock(heatBlock);
        heatBlock.SetFloat("_HeatIntensity", shownHeat);
        thermalBody.SetPropertyBlock(heatBlock);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        phaseTimer += dt;

        UpdateRotation(dt);
        UpdateHeat();
        UpdateCatch();

        // 進行はオーナーだけ。全員が進めると位相が割れる。
        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        if (phaseTimer < phaseLength)
        {
            return;
        }

        if (phase == PhaseAway)
        {
            EnterPhase(PhaseTurn);
        }
        else if (phase == PhaseTurn)
        {
            EnterPhase(PhaseWatch);
        }
        else if (phase == PhaseWatch)
        {
            EnterPhase(PhaseAway);
        }
        else
        {
            // 名指しが終わった。ここで初めて全員リスポーン。
            if (!deathSent)
            {
                deathSent = true;
                if (punish && checkpointManager != null)
                {
                    checkpointManager.TriggerDeath();
                }
            }
            EnterPhase(PhaseAway);

            // リスポーンさせる設定のときだけ、直後は必ず長めに背を向ける。
            // 復帰してすぐ見張りが始まると、立ち直る前にまた捕まって
            // 何度やってもここから先へ進めなくなる。
            // 殺さない設定なら止める理由が無いので、普通の長さのまま回す。
            if (punish)
            {
                phaseLength = awayMax;
                RequestSerialization();
            }
        }
    }

    // ------------------------------------------------------------
    // 首の向き
    // ------------------------------------------------------------
    private void UpdateRotation(float dt)
    {
        if (daruma == null)
        {
            return;
        }

        if (phase == PhaseAway)
        {
            currentYaw = awayYaw;
        }
        else if (phase == PhaseTurn)
        {
            float t = phaseLength > 0.0001f ? Mathf.Clamp01(phaseTimer / phaseLength) : 1.0f;
            // 端を滑らかにして「ぐるっと振り向く」感じを出す
            t = t * t * (3.0f - 2.0f * t);
            currentYaw = Mathf.LerpAngle(awayYaw, watchYaw, t);
        }
        else if (phase == PhaseWatch)
        {
            currentYaw = watchYaw;
        }
        else
        {
            // ★ ここがこのギミックの肝。動いた人の方を向いて、そのまま追い続ける。
            float target = watchYaw;
            VRCPlayerApi accused = VRCPlayerApi.GetPlayerById(accusedId);
            if (Utilities.IsValid(accused))
            {
                Vector3 d = accused.GetPosition() - daruma.position;
                d.y = 0.0f;
                if (d.sqrMagnitude > 0.0001f)
                {
                    target = Quaternion.LookRotation(d.normalized).eulerAngles.y + faceYawOffset;
                }
            }
            currentYaw = Mathf.MoveTowardsAngle(currentYaw, target, trackSpeed * dt);
        }

        ApplyYaw(currentYaw);
    }

    private void ApplyYaw(float yaw)
    {
        if (daruma == null)
        {
            return;
        }

        // 親に回転が入っていても水平に回るよう、ワールド回転として組み直す。
        daruma.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
    }

    // ------------------------------------------------------------
    // 動いたかどうか
    // ------------------------------------------------------------
    private void UpdateCatch()
    {
        if (phase != PhaseWatch)
        {
            return;
        }
        // 見張りが始まった時点で対象だった人だけ。途中から入った人は次の回から。
        if (!eligible || !localInside)
        {
            return;
        }
        // 振り向き切った直後は猶予。ここが無いと反応が間に合わない。
        if (phaseTimer < graceTime)
        {
            return;
        }

        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null)
        {
            return;
        }

        Vector3 pos = lp.GetPosition();

        if (!frozenValid)
        {
            frozenPos = pos;
            frozenValid = true;
            return;
        }

        Vector3 d = pos - frozenPos;
        d.y = 0.0f;   // ジャンプやしゃがみは見逃す。水平移動だけを見る。
        if (d.sqrMagnitude < moveTolerance * moveTolerance)
        {
            return;
        }

        // 捕まった。自分がオーナーを取って「自分が動いた」と書き込む。
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(lp, gameObject);
        }
        accusedId = lp.playerId;
        EnterPhase(PhaseAccuse);
    }
}
