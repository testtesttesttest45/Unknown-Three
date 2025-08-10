using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

public class DisconnectUI : MonoBehaviour
{
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private GameObject panel; // assign the visible root

    void Awake()
    {
        mainMenuButton.onClick.AddListener(() => Loader.Load(Loader.Scene.HomeScene));
        Hide();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnServerStopped += OnServerStopped;
        }
    }

    private void OnServerStopped(bool _) => TryShow("Server stopped (host)");

    private void OnClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsHost &&
            clientId == NetworkManager.Singleton.LocalClientId)
        {
            TryShow("Disconnected from host (client)");
        }
    }

    private void TryShow(string reason)
    {
        if (GamePlayManager.GameHasEnded) return;
        Debug.LogWarning($"[DisconnectUI] {reason} — showing Disconnect UI");
        Show();
    }

    public void Show() => panel.SetActive(true);
    public void Hide() => panel.SetActive(false);

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
        }
    }
}
