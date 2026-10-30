
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;

// タイトル画面。入室した人の目の前に板を出し、後ろでステージの動画を流す。
//
// ・自分の画面だけで動く(同期しない)。押した人だけがゲームを始める。
// ・板は UI レイヤー(5)。PlayerVisionController が全役のカメラに UI レイヤーを足しているので、
//   どの役でも見える。奥行きを無視して手前に描くマテリアルなので、壁が近くても隠れない。
// ・メニューはスティック(デスクトップは W/S)で選び、トリガー(左クリック)で決定する。
//   デスクトップは視界の真ん中しか指せないため、板の上のボタンを指して押す方式にはしていない。
// ・最初は何も選んでいない。何も選ばずに決定を押したときは、まず先頭(スタート)を選ぶ。
// ・選んでいるボタンは、影の位置まで沈み込んで枠の色が変わる。決定するとさらに沈んでから消える。
// ・タイトル中は歩けないようにする(Immobilize)。
// ・動画は VRChat の仕様で URL からしか流せない(YouTube など)。URL が空なら背景は黒のまま。
// ・動画プレイヤーはこのスクリプトと同じ GameObject に付けること。OnVideoStart/OnVideoError は
//   同じ GameObject の Udon にしか届かない。動画が始まるまで(と、エラーのとき)は動画の板を隠して黒にする。
// ・動画の大きさは、デスクトップなら視野ぴったり、VR なら視野が広いので大きくする。
// ・作り直しは メニュー「BLIND/タイトル画面/作り直す」(TitleScreenBuilder)。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TitleScreen : UdonSharpBehaviour
{
    [Header("目の前に出す板")]
    [SerializeField] private GameObject panel;
    [Tooltip("顔から板までの距離(m)")]
    [SerializeField] private float distance = 0.6f;

    [Header("メニューの項目(上から順。0番 = スタート)")]
    [SerializeField] private RectTransform[] buttonFaces;
    [SerializeField] private Image[] buttonFrames;
    [SerializeField] private GameObject[] buttonShadows;

    [Header("ボタンの見た目")]
    [Tooltip("選んでいないときに、ボタンが影より浮いている量(キャンバスの単位=1mm)")]
    [SerializeField] private float raiseHeight = 7f;
    [Tooltip("決定したときに、さらに沈む量")]
    [SerializeField] private float pressDepth = 4f;
    [SerializeField] private Color frameNormal = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField] private Color frameSelected = new Color(1f, 0.85f, 0.35f, 1f);
    [Tooltip("決定してから消えるまでの秒数")]
    [SerializeField] private float confirmDelay = 0.25f;

    [Header("背景の動画")]
    [SerializeField] private BaseVRCVideoPlayer videoPlayer;
    [SerializeField] private VRCUrl videoUrl;
    [Tooltip("動画を映す RawImage。VR かどうかで大きさを変える")]
    [SerializeField] private RectTransform videoRect;
    [Tooltip("デスクトップ: 視野(顔から 0.6m 先で約 1.23m×0.69m)にちょうど収まる大きさ")]
    [SerializeField] private Vector2 desktopVideoSize = new Vector2(1240f, 698f);
    [Tooltip("VR: 視野が広いので、端が見えないよう大きくする")]
    [SerializeField] private Vector2 vrVideoSize = new Vector2(2400f, 1350f);

    [Header("効果音(任意)")]
    [SerializeField] private AudioSource selectSound;
    [SerializeField] private AudioSource confirmSound;

    private bool showing;
    private bool confirming;
    private int selected = -1;
    private bool stickNeutral = true;
    private Vector2[] basePositions;

    void Start()
    {
        int n = buttonFaces != null ? buttonFaces.Length : 0;
        basePositions = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            if (buttonFaces[i] != null) basePositions[i] = buttonFaces[i].anchoredPosition;
        }

        Show();
    }

    public void Show()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || panel == null) return;

        showing = true;
        confirming = false;
        selected = -1;
        stickNeutral = true;
        panel.SetActive(true);
        local.Immobilize(true);
        FollowHead();
        RefreshButtons();

        if (videoRect != null)
        {
            videoRect.sizeDelta = local.IsUserInVR() ? vrVideoSize : desktopVideoSize;
            // 動画が始まるまでは隠す(動画を映す RenderTexture は、最初は灰色のため)
            videoRect.gameObject.SetActive(false);
        }

        if (videoPlayer != null && videoUrl != null && videoUrl.Get().Length > 0)
        {
            videoPlayer.Loop = true;
            videoPlayer.PlayURL(videoUrl);
        }
    }

    public override void PostLateUpdate()
    {
        if (!showing) return;
        FollowHead();
    }

    // スティックの上下(デスクトップは W/S)。一度倒したら、真ん中に戻すまで次へ進まない。
    public override void InputMoveVertical(float value, UdonInputEventArgs args)
    {
        if (!showing || confirming) return;

        if (value > -0.2f && value < 0.2f)
        {
            stickNeutral = true;
            return;
        }
        if (!stickNeutral) return;
        if (value < 0.5f && value > -0.5f) return;
        stickNeutral = false;

        int n = buttonFaces.Length;
        if (n == 0) return;
        if (selected < 0) selected = 0;
        else if (value > 0f) selected = (selected - 1 + n) % n;
        else selected = (selected + 1) % n;

        if (selectSound != null) selectSound.Play();
        RefreshButtons();
    }

    // トリガー(デスクトップは左クリック)で決定
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!showing || confirming || !value) return;

        if (selected < 0)
        {
            selected = 0;
            if (selectSound != null) selectSound.Play();
            RefreshButtons();
            return;
        }

        confirming = true;
        if (confirmSound != null) confirmSound.Play();
        RefreshButtons();
        SendCustomEventDelayedSeconds(nameof(_OnConfirmed), confirmDelay);
    }

    // 決定した項目を実行する。今は「スタート」(0番)だけ。
    public void _OnConfirmed()
    {
        if (selected == 0)
        {
            StartGame();
        }
        else
        {
            confirming = false;
            RefreshButtons();
        }
    }

    private void StartGame()
    {
        showing = false;
        confirming = false;
        if (panel != null) panel.SetActive(false);

        VRCPlayerApi local = Networking.LocalPlayer;
        if (local != null) local.Immobilize(false);

        if (videoPlayer != null) videoPlayer.Stop();
    }

    public override void OnVideoStart()
    {
        if (showing && videoRect != null) videoRect.gameObject.SetActive(true);
    }

    public override void OnVideoError(VideoError videoError)
    {
        // 背景が黒いままになるだけで、タイトルとスタートはそのまま使える
        Debug.LogWarning("[BLIND] タイトルの背景動画を再生できなかった: " + videoError);
    }

    private void FollowHead()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || panel == null) return;
        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        panel.transform.SetPositionAndRotation(head.position + head.rotation * Vector3.forward * distance, head.rotation);
    }

    // 選んでいない = 浮いて影が見える / 選んでいる = 影の位置まで沈む / 決定 = さらに沈む
    private void RefreshButtons()
    {
        if (buttonFaces == null) return;

        for (int i = 0; i < buttonFaces.Length; i++)
        {
            if (buttonFaces[i] == null) continue;

            bool isSelected = i == selected;
            float y = isSelected ? 0f : raiseHeight;
            if (isSelected && confirming) y = -pressDepth;
            buttonFaces[i].anchoredPosition = basePositions[i] + new Vector2(0f, y);

            if (buttonFrames != null && i < buttonFrames.Length && buttonFrames[i] != null)
            {
                buttonFrames[i].color = isSelected ? frameSelected : frameNormal;
            }
            if (buttonShadows != null && i < buttonShadows.Length && buttonShadows[i] != null)
            {
                buttonShadows[i].SetActive(!isSelected);
            }
        }
    }
}
