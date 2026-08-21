
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 他プレイヤー1人分の「木製マネキン」プロキシ。
//
// マネキンのarmature(骨格)は元々SkinnedMeshRendererで見た目が構成されているため、
// HumanoidProxyRig(カプセル版)のように「2点間に伸縮させる」方式は使えない。
// 代わりに、各ボーンを「実プレイヤーの対応する2点(例:肩->肘)が向いている方向」に
// 回転させるだけにする(長さはモデル本来の骨格のまま、伸縮なし)。
// 位置はTransform階層の親子関係で自動的に伝播するので、
// 根本(armature)のワールド座標をHipsに合わせるだけでよい。
//
// 対応できないボーン(頭・手首・足首の先端の細かい向き)は
// あえて回転させず、親の回転をそのまま受け継ぐだけにしている。
// (アバターごとにボーン軸の向きの慣習が違うため、末端だけ直接コピーすると
//  ねじれて見えるリスクが高く、今回はそこまでやらない判断)
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MannequinProxyRig : UdonSharpBehaviour
{
    [Header("マネキンのarmatureルート(hip.l/hip.rとspineの親)")]
    [SerializeField] private Transform armature;

    [Header("背骨(3本、Hips->Chestの向きを均等に割り当てる)")]
    [SerializeField] private Transform spine;
    [SerializeField] private Transform belly;
    [SerializeField] private Transform torso;

    [Header("首(Chest->Headの向き)")]
    [SerializeField] private Transform neck;

    [Header("左腕")]
    [SerializeField] private Transform leftArm;      // LeftUpperArm -> LeftLowerArm
    [SerializeField] private Transform leftForearm;  // LeftLowerArm -> LeftHand

    [Header("右腕")]
    [SerializeField] private Transform rightArm;
    [SerializeField] private Transform rightForearm;

    [Header("左脚")]
    [SerializeField] private Transform leftHip;  // LeftUpperLeg -> LeftLowerLeg
    [SerializeField] private Transform leftLeg;  // 同上(2本目、同じ向き)
    [SerializeField] private Transform leftCalf; // LeftLowerLeg -> LeftFoot

    [Header("右脚")]
    [SerializeField] private Transform rightHip;
    [SerializeField] private Transform rightLeg;
    [SerializeField] private Transform rightCalf;

    [Header("サーマル表現(全パーツのThermalレイヤー側メッシュ)")]
    [SerializeField] private Renderer[] thermalRenderers;

    // 各ボーンの「静止姿勢でのワールド方向」と「静止姿勢でのワールド回転」
    private Vector3 spineRestDir, bellyRestDir, torsoRestDir, neckRestDir;
    private Vector3 leftArmRestDir, leftForearmRestDir, rightArmRestDir, rightForearmRestDir;
    private Vector3 leftHipRestDir, leftLegRestDir, leftCalfRestDir;
    private Vector3 rightHipRestDir, rightLegRestDir, rightCalfRestDir;

    private Quaternion spineRestRot, bellyRestRot, torsoRestRot, neckRestRot;
    private Quaternion leftArmRestRot, leftForearmRestRot, rightArmRestRot, rightForearmRestRot;
    private Quaternion leftHipRestRot, leftLegRestRot, leftCalfRestRot;
    private Quaternion rightHipRestRot, rightLegRestRot, rightCalfRestRot;

    private MaterialPropertyBlock propertyBlock;
    private bool initialized;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
        CacheRest();
    }

    private Vector3 DirToChild(Transform bone)
    {
        if (bone == null || bone.childCount == 0) return Vector3.up;
        return (bone.GetChild(0).position - bone.position).normalized;
    }

    private void CacheRest()
    {
        if (spine != null) { spineRestDir = DirToChild(spine); spineRestRot = spine.rotation; }
        if (belly != null) { bellyRestDir = DirToChild(belly); bellyRestRot = belly.rotation; }
        if (torso != null) { torsoRestDir = (neck != null ? (neck.position - torso.position).normalized : DirToChild(torso)); torsoRestRot = torso.rotation; }
        if (neck != null) { neckRestDir = DirToChild(neck); neckRestRot = neck.rotation; }

        if (leftArm != null) { leftArmRestDir = DirToChild(leftArm); leftArmRestRot = leftArm.rotation; }
        if (leftForearm != null) { leftForearmRestDir = DirToChild(leftForearm); leftForearmRestRot = leftForearm.rotation; }
        if (rightArm != null) { rightArmRestDir = DirToChild(rightArm); rightArmRestRot = rightArm.rotation; }
        if (rightForearm != null) { rightForearmRestDir = DirToChild(rightForearm); rightForearmRestRot = rightForearm.rotation; }

        if (leftHip != null) { leftHipRestDir = DirToChild(leftHip); leftHipRestRot = leftHip.rotation; }
        if (leftLeg != null) { leftLegRestDir = DirToChild(leftLeg); leftLegRestRot = leftLeg.rotation; }
        if (leftCalf != null) { leftCalfRestDir = DirToChild(leftCalf); leftCalfRestRot = leftCalf.rotation; }

        if (rightHip != null) { rightHipRestDir = DirToChild(rightHip); rightHipRestRot = rightHip.rotation; }
        if (rightLeg != null) { rightLegRestDir = DirToChild(rightLeg); rightLegRestRot = rightLeg.rotation; }
        if (rightCalf != null) { rightCalfRestDir = DirToChild(rightCalf); rightCalfRestRot = rightCalf.rotation; }

        initialized = true;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

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

    // サーマルRendererの表示/非表示を切り替える。
    // ThermalHideZone内にいるプレイヤーのサーマル表示を消すために使う。
    public void SetThermalVisible(bool visible)
    {
        if (thermalRenderers == null) return;

        for (int i = 0; i < thermalRenderers.Length; i++)
        {
            if (thermalRenderers[i] == null) continue;
            thermalRenderers[i].enabled = visible;
        }
    }

    public void UpdatePose(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;
        if (!initialized) CacheRest();

        Vector3 hips = player.GetBonePosition(HumanBodyBones.Hips);
        Vector3 chest = player.GetBonePosition(HumanBodyBones.Chest);
        Vector3 head = player.GetBonePosition(HumanBodyBones.Head);

        Vector3 leftUpperArm = player.GetBonePosition(HumanBodyBones.LeftUpperArm);
        Vector3 leftLowerArm = player.GetBonePosition(HumanBodyBones.LeftLowerArm);
        Vector3 leftHand = player.GetBonePosition(HumanBodyBones.LeftHand);

        Vector3 rightUpperArm = player.GetBonePosition(HumanBodyBones.RightUpperArm);
        Vector3 rightLowerArm = player.GetBonePosition(HumanBodyBones.RightLowerArm);
        Vector3 rightHand = player.GetBonePosition(HumanBodyBones.RightHand);

        Vector3 leftUpperLeg = player.GetBonePosition(HumanBodyBones.LeftUpperLeg);
        Vector3 leftLowerLeg = player.GetBonePosition(HumanBodyBones.LeftLowerLeg);
        Vector3 leftFoot = player.GetBonePosition(HumanBodyBones.LeftFoot);

        Vector3 rightUpperLeg = player.GetBonePosition(HumanBodyBones.RightUpperLeg);
        Vector3 rightLowerLeg = player.GetBonePosition(HumanBodyBones.RightLowerLeg);
        Vector3 rightFoot = player.GetBonePosition(HumanBodyBones.RightFoot);

        if (armature != null)
        {
            armature.position = hips;
        }

        Vector3 spineDir = (chest - hips).normalized;
        Aim(spine, spineRestDir, spineRestRot, spineDir);
        Aim(belly, bellyRestDir, bellyRestRot, spineDir);
        Aim(torso, torsoRestDir, torsoRestRot, spineDir);

        Vector3 neckDir = (head - chest).normalized;
        Aim(neck, neckRestDir, neckRestRot, neckDir);

        Aim(leftArm, leftArmRestDir, leftArmRestRot, (leftLowerArm - leftUpperArm).normalized);
        Aim(leftForearm, leftForearmRestDir, leftForearmRestRot, (leftHand - leftLowerArm).normalized);
        Aim(rightArm, rightArmRestDir, rightArmRestRot, (rightLowerArm - rightUpperArm).normalized);
        Aim(rightForearm, rightForearmRestDir, rightForearmRestRot, (rightHand - rightLowerArm).normalized);

        Vector3 leftLegDir = (leftLowerLeg - leftUpperLeg).normalized;
        Aim(leftHip, leftHipRestDir, leftHipRestRot, leftLegDir);
        Aim(leftLeg, leftLegRestDir, leftLegRestRot, leftLegDir);
        Aim(leftCalf, leftCalfRestDir, leftCalfRestRot, (leftFoot - leftLowerLeg).normalized);

        Vector3 rightLegDir = (rightLowerLeg - rightUpperLeg).normalized;
        Aim(rightHip, rightHipRestDir, rightHipRestRot, rightLegDir);
        Aim(rightLeg, rightLegRestDir, rightLegRestRot, rightLegDir);
        Aim(rightCalf, rightCalfRestDir, rightCalfRestRot, (rightFoot - rightLowerLeg).normalized);
    }

    // restDir(静止姿勢での向き)からdesiredDir(今のプレイヤーの向き)への回転差分を、
    // 静止姿勢でのワールド回転に掛けて絶対回転として適用する。
    // (親ボーンの現在の回転状態に依存しないので、処理順序を気にしなくてよい)
    private void Aim(Transform bone, Vector3 restDir, Quaternion restRot, Vector3 desiredDir)
    {
        if (bone == null) return;
        if (desiredDir.sqrMagnitude < 0.0001f) return;

        Quaternion delta = Quaternion.FromToRotation(restDir, desiredDir);
        bone.rotation = delta * restRot;
    }
}
