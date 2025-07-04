using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerSingleUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private Image characterImage;
    [SerializeField] private GameObject readyIndicator;
    [SerializeField] private Button kickPlayerButton;
    [SerializeField] private GameObject rootContainer;

    private ulong clientId;

    private void Awake()
    {
        kickPlayerButton.onClick.AddListener(() => {
            if (!LobbyManager.Instance.IsLobbyHost() || clientId == NetworkManager.Singleton.LocalClientId)
                return;

            var playerData = MultiplayerManager.Instance.GetPlayerDataFromClientId(clientId);

            if (playerData.clientId >= 9000)
            {
                LobbyManager.Instance.RemoveBotFromLobbyById(playerData.clientId);
            }
            else
            {
                if (!string.IsNullOrEmpty(playerData.playerId.ToString()))
                {
                    LobbyManager.Instance.KickPlayer(playerData.playerId.ToString());

                    if (NetworkManager.Singleton.IsServer)
                    {
                        NetworkManager.Singleton.DisconnectClient(playerData.clientId);
                        MultiplayerManager.Instance.RemoveBotPlayerById(playerData.clientId);
                    }
                }

            }
        });


        MultiplayerManager.Instance.OnPlayerDataNetworkListChanged += OnPlayerDataUpdated;
    }

    private void OnDestroy()
    {
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnPlayerDataNetworkListChanged -= OnPlayerDataUpdated;
        }
    }

    public void SetKickPlayerButtonVisible(bool visible)
    {
        kickPlayerButton.gameObject.SetActive(visible);
    }

    public void SetPlayer(ulong clientId)
    {
        this.clientId = clientId;
        UpdateUI();
        rootContainer.SetActive(true);
    }


    private IEnumerator DelayedUIUpdate()
    {
        yield return new WaitForSeconds(0.1f);
        UpdateUI();
        rootContainer.SetActive(true); // show once ready
    }



    private void OnPlayerDataUpdated(object sender, System.EventArgs e)
    {
        UpdateUI();
    }

    public void UpdateUI()
    {
        var playerData = MultiplayerManager.Instance.GetPlayerDataFromClientId(clientId);

        playerNameText.text = playerData.playerName.ToString();
        characterImage.sprite = Resources.Load<Sprite>($"Avatar/{playerData.avatarIndex}");
        if (readyIndicator != null)
            readyIndicator.SetActive(playerData.isReady);

        if (LobbyManager.Instance.IsLobbyHost())
        {
            bool isSelf = clientId == NetworkManager.Singleton.LocalClientId;
            bool isHostSlot = playerData.playerId == AuthenticationService.Instance.PlayerId;

            kickPlayerButton.gameObject.SetActive(!isHostSlot && !isSelf);
        }
        else
        {
            kickPlayerButton.gameObject.SetActive(false);
        }
    }


}
