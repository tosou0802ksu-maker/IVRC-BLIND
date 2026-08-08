
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class OutlineSwitch : UdonSharpBehaviour
{
    [SerializeField]
    private OutlineController targetController;

    private bool isOn = false;

    public override void Interact()
    {
        ToggleSwitch();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleSwitch();
        }
    }

    [ContextMenu("Test Toggle Switch")]
    public void ToggleSwitch()
    {
        Debug.Log("ToggleSwitch called, targetController=" + targetController);
        if (targetController == null) return;

        isOn = !isOn;
        Debug.Log("isOn=" + isOn);
        targetController.SetHighlight(isOn);
    }
}