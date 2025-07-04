using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyMessageUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        closeButton.onClick.AddListener(Hide);
    }

    private void Start()
    {
        MultiplayerManager.Instance.OnFailedToJoinGame += KitchenGameMultiplayer_OnFailedToJoinGame;
        LobbyManager.Instance.OnCreateLobbyStarted += KitchenGameLobby_OnCreateLobbyStarted;
        LobbyManager.Instance.OnCreateLobbyFailed += KitchenGameLobby_OnCreateLobbyFailed;
        LobbyManager.Instance.OnJoinStarted += KitchenGameLobby_OnJoinStarted;
        LobbyManager.Instance.OnJoinFailed += KitchenGameLobby_OnJoinFailed;
        LobbyManager.Instance.OnQuickJoinFailed += KitchenGameLobby_OnQuickJoinFailed;

        Hide();
    }

    private void KitchenGameLobby_OnQuickJoinFailed(object sender, System.EventArgs e)
    {
        ShowMessage("Could not find a Lobby to Quick Join!");
    }

    private void KitchenGameLobby_OnJoinFailed(object sender, System.EventArgs e)
    {
        ShowMessage("Failed to join Lobby!");
    }

    private void KitchenGameLobby_OnJoinStarted(object sender, System.EventArgs e)
    {
        ShowMessage("Joining Lobby...");
    }

    private void KitchenGameLobby_OnCreateLobbyFailed(object sender, System.EventArgs e)
    {
        ShowMessage("Failed to create Lobby!");
    }

    private void KitchenGameLobby_OnCreateLobbyStarted(object sender, System.EventArgs e)
    {
        ShowMessage("Creating Lobby...");
    }

    private void KitchenGameMultiplayer_OnFailedToJoinGame(object sender, System.EventArgs e)
    {
        if (NetworkManager.Singleton.DisconnectReason == "")
        {
            ShowMessage("Failed to connect");
        }
        else
        {
            ShowMessage(NetworkManager.Singleton.DisconnectReason);
        }
    }

    private void ShowMessage(string message)
    {
        Show();
        messageText.text = message;
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        MultiplayerManager.Instance.OnFailedToJoinGame -= KitchenGameMultiplayer_OnFailedToJoinGame;
        LobbyManager.Instance.OnCreateLobbyStarted -= KitchenGameLobby_OnCreateLobbyStarted;
        LobbyManager.Instance.OnCreateLobbyFailed -= KitchenGameLobby_OnCreateLobbyFailed;
        LobbyManager.Instance.OnJoinStarted -= KitchenGameLobby_OnJoinStarted;
        LobbyManager.Instance.OnJoinFailed -= KitchenGameLobby_OnJoinFailed;
        LobbyManager.Instance.OnQuickJoinFailed -= KitchenGameLobby_OnQuickJoinFailed;
    }

}