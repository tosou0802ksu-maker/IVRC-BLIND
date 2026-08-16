using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EchoEmitter : UdonSharpBehaviour
{
    [Header("役割判定")]
    [Tooltip("エコロケ役の時だけパルスを発するためのローカル視点コントローラ参照。")]
    [SerializeField] private PlayerVisionController localVisionController;

    [Header("パルス設定")]
    [SerializeField] private float pulseRange = 10f;
    [SerializeField] private float pulseAngle = 45f;
    [SerializeField] private float pulseInterval = 3f;

    [Header("遅延設定")]
    [SerializeField] private float delayPerMeter = 0.1f;

    [Header("エディタテスト用")]
    [SerializeField] private Transform editorCamera;

    [Header("受信オブジェクト一覧")]
    [SerializeField] private EchoReceiver[] receivers;

    private float timer = 0f;
    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }

    private bool IsEchoRole()
    {
        // 参照未設定の場合は従来通り誰でも発動できるようにしておく(エディタ単体テスト等)
        if (localVisionController == null) return true;
        return localVisionController.GetRole() == ViewRole.Echo;
    }

    void Update()
    {
        if (!IsEchoRole()) return;

        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("Tキー検知");
            EmitFromEditorCamera();
            return;
        }

        if (localPlayer == null) return;
        timer += Time.deltaTime;
        if (timer >= pulseInterval)
        {
            timer = 0f;
            EmitFromBothHands();
        }
    }

    private void EmitFromEditorCamera()
    {
        // localPlayerがいればHeadの骨から位置と向きを取得
        if (localPlayer != null)
        {
            Vector3 headPos = localPlayer.GetBonePosition(HumanBodyBones.Head);
            Quaternion headRot = localPlayer.GetBoneRotation(HumanBodyBones.Head);
            EmitPulse(headPos, headRot * Vector3.forward);
            return;
        }

        // localPlayerがnullならeditorCameraを使う
        if (editorCamera == null) return;
        EmitPulse(editorCamera.position, editorCamera.forward);
    }

    private void EmitFromBothHands()
    {
        Vector3 rightPos = localPlayer.GetBonePosition(HumanBodyBones.RightHand);
        Quaternion rightRot = localPlayer.GetBoneRotation(HumanBodyBones.RightHand);
        EmitPulse(rightPos, rightRot * Vector3.forward);

        Vector3 leftPos = localPlayer.GetBonePosition(HumanBodyBones.LeftHand);
        Quaternion leftRot = localPlayer.GetBoneRotation(HumanBodyBones.LeftHand);
        EmitPulse(leftPos, leftRot * Vector3.forward);
    }

    private void EmitPulse(Vector3 origin, Vector3 direction)
    {
        if (receivers == null || receivers.Length == 0) return;

        for (int i = 0; i < receivers.Length; i++)
        {
            EchoReceiver receiver = receivers[i];
            if (receiver == null) continue;

            Vector3 toReceiver = receiver.transform.position - origin;
            float distance = toReceiver.magnitude;
            float angle = Vector3.Angle(direction, toReceiver.normalized);

            if (distance > pulseRange) continue;
            if (angle > pulseAngle) continue;

            float startDelay = distance * delayPerMeter;
            float fadeStartDelay = distance * delayPerMeter;
            receiver.TriggerGlowWithDelay(startDelay, fadeStartDelay);
        }
    }
}