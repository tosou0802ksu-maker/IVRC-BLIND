using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EchoEmitter : UdonSharpBehaviour
{
    [Header("パルス設定")]
    [SerializeField] private float pulseRange = 10f;
    [SerializeField] private float pulseAngle = 45f;
    [SerializeField] private float pulseInterval = 3f;

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

    void Update()
    {
        // スペースキーでエディタテスト発射
        if (Input.GetKeyDown(KeyCode.Space))
        {
            EmitFromEditorCamera();
            return;
        }

        // VRChat上では自動でパルス発射
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

            if (distance > pulseRange) continue;

            float angle = Vector3.Angle(direction, toReceiver.normalized);
            if (angle > pulseAngle) continue;

            receiver.TriggerGlow();
        }
    }
}