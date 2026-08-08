
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public enum GameState
{
    Waiting,
    Room1,
    Room2,
    Clear
}

public class GameManager : UdonSharpBehaviour
{

    [Header("復活ポイント")]
    [SerializeField] private Transform startSpawnPoint;
    [SerializeField] private Transform room1SpawnPoint;
    [SerializeField] private Transform room2SpawnPoint;

    [UdonSynced] private GameState currentState;

    public void TriggerPitfallDeath()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, "PitfallRespawnAll");
    }

    public void TriggerThermalDeath()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, "ThermalRespawnAll");
    }

    public void PitfallRespawnAll()
    {
        if (currentState == GameState.Room1)
        {
            Networking.LocalPlayer.TeleportTo(room1SpawnPoint.position, room1SpawnPoint.rotation);
        }
        else if (currentState == GameState.Room2)
        {
            Networking.LocalPlayer.TeleportTo(room2SpawnPoint.position, room2SpawnPoint.rotation);
        }
    }

    public void ThermalRespawnAll()
    {
        if (Networking.IsOwner(gameObject))
        {
            currentState = GameState.Waiting;
            RequestSerialization();
        }

        Networking.LocalPlayer.TeleportTo(startSpawnPoint.position, startSpawnPoint.rotation);
    }

    public void ChangeState(GameState newState)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        currentState = newState;
        RequestSerialization();
        Debug.Log("GameManager: state changed to " + currentState);
    }

    public override void OnDeserialization()
    {
        Debug.Log("GameManager: state changed to " + currentState);
    }
}
