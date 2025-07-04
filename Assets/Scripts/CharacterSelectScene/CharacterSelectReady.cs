using Unity.Netcode;
using UnityEngine;

public class CharacterSelectReady : MonoBehaviour
{
    public static CharacterSelectReady Instance { get; private set; }

    private void Awake() => Instance = this;

    public void ToggleReady()
    {
        MultiplayerManager.Instance.TogglePlayerReadyServerRpc();
    }
}
