using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectUI : MonoBehaviour
{
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    [SerializeField] private TextMeshProUGUI readyButtonText;
    [SerializeField] private Button addBotButton;
    public static CharacterSelectUI Instance { get; private set; }

    [SerializeField] private Transform playerSlotContainer;
    [SerializeField] private GameObject playerSlotPrefab;
    [SerializeField] private TextMeshProUGUI playerCountText;

    private void Awake()
    {
        Instance = this;
        if (!LobbyManager.Instance.IsLobbyHost())
            addBotButton.gameObject.SetActive(false);

        addBotButton.onClick.AddListener(() => {
            LobbyManager.Instance.AddBotToLobby();
        });
        mainMenuButton.onClick.AddListener(() => {
            if (LobbyManager.Instance.IsLobbyHost())
                LobbyManager.Instance.DeleteLobby();
            else
                LobbyManager.Instance.LeaveLobby();

            SessionManager.CleanUpSession();
            Loader.Load(Loader.Scene.HomeScene);
        });


        readyButton.onClick.AddListener(() => {
            CharacterSelectReady.Instance.ToggleReady();
        });

    }

    private void Start()
    {
        Lobby lobby = LobbyManager.Instance.GetLobby();

        lobbyNameText.text = "Lobby Name: " + lobby.Name;
        lobbyCodeText.text = "Lobby Code: " + lobby.LobbyCode;
        MultiplayerManager.Instance.OnPlayerDataNetworkListChanged += UpdateReadyButtonState;
        UpdateReadyButtonState(this, System.EventArgs.Empty);

        // Delay player slot refresh
        StartCoroutine(DelayedRefreshSlots());
    }

    private IEnumerator DelayedRefreshSlots()
    {
        yield return new WaitForSeconds(0.1f);
        RefreshPlayerSlots();
    }


    private void UpdateReadyButtonState(object sender, System.EventArgs e)
    {
        int total = MultiplayerManager.Instance.playerDataNetworkList.Count;
        int ready = MultiplayerManager.Instance.GetReadyPlayerCount();

        addBotButton.interactable = total < MultiplayerManager.MAX_PLAYER_AMOUNT;
        readyButton.interactable = true;
        readyButtonText.text = $"Ready {ready}/{total}";

        playerCountText.text = $"{total}/{MultiplayerManager.MAX_PLAYER_AMOUNT}";

        RefreshPlayerSlots();
    }


    private Dictionary<ulong, LobbyPlayerSingleUI> playerSlotMap = new Dictionary<ulong, LobbyPlayerSingleUI>();

    private void RefreshPlayerSlots()
    {
        var currentClientIds = new HashSet<ulong>();

        foreach (var playerData in MultiplayerManager.Instance.playerDataNetworkList)
        {
            currentClientIds.Add(playerData.clientId);

            if (!playerSlotMap.ContainsKey(playerData.clientId))
            {
                GameObject slotGO = Instantiate(playerSlotPrefab, playerSlotContainer);
                slotGO.SetActive(true);
                LobbyPlayerSingleUI playerUI = slotGO.GetComponent<LobbyPlayerSingleUI>();

                StartCoroutine(DelayedAssign(playerUI, playerData.clientId));
                playerSlotMap[playerData.clientId] = playerUI;

            }
        }



        // Remove old/disconnected players
        List<ulong> toRemove = new List<ulong>();
        foreach (var kvp in playerSlotMap)
        {
            if (!currentClientIds.Contains(kvp.Key))
            {
                Destroy(kvp.Value.gameObject);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove)
        {
            playerSlotMap.Remove(id);
        }
    }

    private IEnumerator DelayedAssign(LobbyPlayerSingleUI playerUI, ulong clientId)
    {
        yield return null; // Wait one frame so the object becomes active
        playerUI.SetPlayer(clientId);
    }



    private void OnDestroy()
    {
        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnPlayerDataNetworkListChanged -= UpdateReadyButtonState;
    }


}