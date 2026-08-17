
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 他プレイヤー1人分の「人型シルエット」プロキシ。
//
// Hips一点だけを追従させる代わりに、主要な関節(頭・胴・肩・肘・腰・膝など)の
// ボーン位置を毎フレーム取得し、間をカプセルで繋いで人型のポーズを再現する。
// 各セグメントの子には
//   ・Echoレイヤーのメッシュ(輪郭だけのシルエット)
//   ・Thermalレイヤーのメッシュ(ThermalHeat.shaderを使った熱表現)
// を重ねて置いておくことで、役割ごとに見え方を切り替える(カリングマスク側で制御)。
//
// カプセルはUnity標準のCapsule(ローカルY軸方向に高さ2)を想定。
// 長さはlocalScale.yだけを書き換えて伸縮させ、太さ(x/z)はプレハブ側で設定した値を維持する。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class HumanoidProxyRig : UdonSharpBehaviour
{
    [Header("胴体・首(カプセル)")]
    [SerializeField] private Transform torso;   // Hips -> Chest
    [SerializeField] private Transform neck;    // Chest -> Head

    [Header("頭(球体)")]
    [SerializeField] private Transform head;    // Head位置に配置するだけ

    [Header("左腕(カプセル)")]
    [SerializeField] private Transform leftUpperArm; // LeftUpperArm -> LeftLowerArm
    [SerializeField] private Transform leftForearm;  // LeftLowerArm -> LeftHand

    [Header("右腕(カプセル)")]
    [SerializeField] private Transform rightUpperArm; // RightUpperArm -> RightLowerArm
    [SerializeField] private Transform rightForearm;  // RightLowerArm -> RightHand

    [Header("左脚(カプセル)")]
    [SerializeField] private Transform leftThigh; // LeftUpperLeg -> LeftLowerLeg
    [SerializeField] private Transform leftShin;   // LeftLowerLeg -> LeftFoot

    [Header("右脚(カプセル)")]
    [SerializeField] private Transform rightThigh; // RightUpperLeg -> RightLowerLeg
    [SerializeField] private Transform rightShin;   // RightLowerLeg -> RightFoot

    [Header("サーマル表現(全セグメントのThermalレイヤー側メッシュ)")]
    [SerializeField] private Renderer[] thermalRenderers;

    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    // 常時同じ熱量(=人間はこのくらい熱い、という固定値)を設定する。
    public void SetHeat(float value)
    {
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        if (thermalRenderers == null) return;

        for (int i = 0; i < thermalRenderers.Length; i++)
        {
            if (thermalRenderers[i] == null) continue;
            thermalRenderers[i].GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_HeatIntensity", value);
            thermalRenderers[i].SetPropertyBlock(propertyBlock);
        }
    }

    // playerのボーン位置を読み、各セグメントの位置・向き・長さを更新する。
    public void UpdatePose(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;

        Vector3 hips = player.GetBonePosition(HumanBodyBones.Hips);
        Vector3 chest = player.GetBonePosition(HumanBodyBones.Chest);
        Vector3 headPos = player.GetBonePosition(HumanBodyBones.Head);

        Vector3 leftUpperArmPos = player.GetBonePosition(HumanBodyBones.LeftUpperArm);
        Vector3 leftLowerArmPos = player.GetBonePosition(HumanBodyBones.LeftLowerArm);
        Vector3 leftHandPos = player.GetBonePosition(HumanBodyBones.LeftHand);

        Vector3 rightUpperArmPos = player.GetBonePosition(HumanBodyBones.RightUpperArm);
        Vector3 rightLowerArmPos = player.GetBonePosition(HumanBodyBones.RightLowerArm);
        Vector3 rightHandPos = player.GetBonePosition(HumanBodyBones.RightHand);

        Vector3 leftUpperLegPos = player.GetBonePosition(HumanBodyBones.LeftUpperLeg);
        Vector3 leftLowerLegPos = player.GetBonePosition(HumanBodyBones.LeftLowerLeg);
        Vector3 leftFootPos = player.GetBonePosition(HumanBodyBones.LeftFoot);

        Vector3 rightUpperLegPos = player.GetBonePosition(HumanBodyBones.RightUpperLeg);
        Vector3 rightLowerLegPos = player.GetBonePosition(HumanBodyBones.RightLowerLeg);
        Vector3 rightFootPos = player.GetBonePosition(HumanBodyBones.RightFoot);

        PositionSegment(torso, hips, chest);
        PositionSegment(neck, chest, headPos);

        if (head != null) head.position = headPos;

        PositionSegment(leftUpperArm, leftUpperArmPos, leftLowerArmPos);
        PositionSegment(leftForearm, leftLowerArmPos, leftHandPos);

        PositionSegment(rightUpperArm, rightUpperArmPos, rightLowerArmPos);
        PositionSegment(rightForearm, rightLowerArmPos, rightHandPos);

        PositionSegment(leftThigh, leftUpperLegPos, leftLowerLegPos);
        PositionSegment(leftShin, leftLowerLegPos, leftFootPos);

        PositionSegment(rightThigh, rightUpperLegPos, rightLowerLegPos);
        PositionSegment(rightShin, rightLowerLegPos, rightFootPos);
    }

    // start-end間にカプセルを合わせる。Unity標準カプセルはローカルY方向に高さ2。
    private void PositionSegment(Transform segment, Vector3 start, Vector3 end)
    {
        if (segment == null) return;

        Vector3 diff = end - start;
        float length = diff.magnitude;
        if (length < 0.001f)
        {
            segment.gameObject.SetActive(false);
            return;
        }

        segment.gameObject.SetActive(true);
        segment.position = (start + end) * 0.5f;
        segment.rotation = Quaternion.FromToRotation(Vector3.up, diff.normalized);

        Vector3 scale = segment.localScale;
        scale.y = length * 0.5f;
        segment.localScale = scale;
    }
}
