using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Lobbies;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;


public class HostDisconnectUI : MonoBehaviour
{
    [SerializeField] private Button playAgainButton;
    private float lobbyCheckTimer = 5f;

    private void Awake()
    {
        playAgainButton.onClick.AddListener(() => {
            Loader.Load(Loader.Scene.HomeScene);
        });
        Hide();
    }

    private void Start()
    {
        GameOverUI.GameHasEnded = false;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }


    private void OnTransportFailure()
    {
        Debug.LogWarning("Transport failure detected — showing HostDisconnectUI");
        Show();
    }


    private void Update()
    {
        if (GameOverUI.GameHasEnded)
        {
            gameObject.SetActive(false);
            this.enabled = false;
            return;
        }
        // checks if host has disconnected
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            lobbyCheckTimer -= Time.deltaTime;
            if (lobbyCheckTimer <= 0f)
            {
                lobbyCheckTimer = 5f;
                CheckHostStillInLobby();
            }
        }
    }

    private async void CheckHostStillInLobby()
    {
        var currentLobby = LobbyManager.Instance.GetLobby();
        if (currentLobby == null)
        {
            Debug.LogWarning("Host's current lobby is already null. Possibly removed.");
            Show();
            return;
        }

        try
        {
            var refreshedLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            bool found = refreshedLobby.Players.Exists(p => p.Id == AuthenticationService.Instance.PlayerId);

            if (!found)
            {
                Debug.LogWarning("Host is no longer listed in the lobby!");
                Show();
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning("failed to fetch lobby (host probably disconnected): " + e.Message);
            Show();
        }
    }

    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
    {
        if (this == null || GameOverUI.GameHasEnded) return;

        if (clientId == NetworkManager.Singleton.LocalClientId && !NetworkManager.Singleton.IsHost)
        {
            Show();
        }
    }

    public void Show()
    {
        if (GameOverUI.GameHasEnded) return;

        Debug.Log("Showing HostDisconnectUI");
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }

}
