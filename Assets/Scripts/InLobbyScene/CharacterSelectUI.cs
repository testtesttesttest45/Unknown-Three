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
    [SerializeField] private Button addSuperbotButton;

    [SerializeField] private Toggle tooltipsToggle;
    [SerializeField] private Button tooltipsToggleParent;

    [SerializeField] private Button scoutsButton;
    [SerializeField] private TextMeshProUGUI scoutsStatusLabel;

    public static CharacterSelectUI Instance { get; private set; }

    [SerializeField] private Transform playerSlotContainer;
    [SerializeField] private GameObject playerSlotPrefab;
    [SerializeField] private TextMeshProUGUI playerCountText;

    private void Awake()
    {
        Instance = this;

        bool isHost = LobbyManager.Instance.IsLobbyHost();

        // Hide host-only controls for clients
        if (!isHost)
        {
            if (addBotButton != null) addBotButton.gameObject.SetActive(false);
            if (addSuperbotButton != null) addSuperbotButton.gameObject.SetActive(false);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveAllListeners();
            mainMenuButton.onClick.AddListener(() =>
            {
                if (LobbyManager.Instance.IsLobbyHost())
                    LobbyManager.Instance.DeleteLobby();
                else
                    LobbyManager.Instance.LeaveLobby();

                SessionManager.CleanUpSession();
                Loader.Load(Loader.Scene.HomeScene);
            });
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(() =>
            {
                CharacterSelectReady.Instance.ToggleReady();
            });
        }

        if (addBotButton != null)
        {
            addBotButton.onClick.RemoveAllListeners();
            addBotButton.onClick.AddListener(() =>
            {
                LobbyManager.Instance.AddBotToLobby();
            });
        }

        if (addSuperbotButton != null)
        {
            addSuperbotButton.onClick.RemoveAllListeners();
            addSuperbotButton.onClick.AddListener(() =>
            {
                LobbyManager.Instance.AddSuperbotToLobby();
            });
        }

        if (tooltipsToggle != null)
        {
            tooltipsToggle.onValueChanged.RemoveAllListeners();
            tooltipsToggle.SetIsOnWithoutNotify(CardGameManager.ShowTooltips);
            tooltipsToggle.onValueChanged.AddListener(isOn =>
            {
                CardGameManager.SetShowTooltips(isOn);
            });
        }
        if (tooltipsToggleParent != null)
        {
            tooltipsToggleParent.onClick.RemoveAllListeners();
            tooltipsToggleParent.onClick.AddListener(() =>
            {
                if (tooltipsToggle != null)
                    tooltipsToggle.isOn = !tooltipsToggle.isOn;
            });
        }
    }

    private void Start()
    {
        // Init Scouts row after NetworkVariables are spawned/synced
        StartCoroutine(SetupScoutsRow());

        Lobby lobby = LobbyManager.Instance.GetLobby();
        lobbyNameText.text = "Lobby Name: " + lobby.Name;
        lobbyCodeText.text = "Lobby Code: " + lobby.LobbyCode;

        // Player list / ready state
        MultiplayerManager.Instance.OnPlayerDataNetworkListChanged += UpdateReadyButtonState;
        UpdateReadyButtonState(this, System.EventArgs.Empty);

        StartCoroutine(DelayedRefreshSlots());
    }

    private IEnumerator SetupScoutsRow()
    {
        if (scoutsButton == null || scoutsStatusLabel == null) yield break;

        bool isHost = LobbyManager.Instance.IsLobbyHost();

        scoutsButton.gameObject.SetActive(true);
        scoutsButton.interactable = isHost;

        yield return new WaitUntil(() => MultiplayerManager.Instance != null
                                          && MultiplayerManager.Instance.NetworkObject != null
                                          && MultiplayerManager.Instance.NetworkObject.IsSpawned);

        var mm = MultiplayerManager.Instance;

        mm.ScoutsEnabled.OnValueChanged += OnScoutsRuleChanged;

        bool current = mm.ScoutsEnabled.Value;

        if (isHost)
            GamePlayManager.AddScoutCards = current;

        UpdateScoutStatusUI(current, isHost);

        scoutsButton.onClick.RemoveAllListeners();
        if (isHost)
        {
            scoutsButton.onClick.AddListener(() =>
            {
                bool next = !mm.ScoutsEnabled.Value;
                GamePlayManager.AddScoutCards = next;
                UpdateScoutStatusUI(next, true);
                mm.SetScoutsEnabledServerRpc(next);
            });
        }
    }

    private IEnumerator DelayedRefreshSlots()
    {
        yield return new WaitForSeconds(0.1f);
        RefreshPlayerSlots();
    }

    private void UpdateReadyButtonState(object sender, System.EventArgs e)
    {
        int total = MultiplayerManager.Instance.playerDataNetworkList.Count;

        addBotButton.interactable = total < MultiplayerManager.MAX_PLAYER_AMOUNT;
        if (addSuperbotButton != null)
            addSuperbotButton.interactable = total < MultiplayerManager.MAX_PLAYER_AMOUNT;

        readyButton.interactable = true;
        readyButtonText.text = $"Ready {MultiplayerManager.Instance.GetReadyPlayerCount()}/{total}";
        playerCountText.text = $"{total}/{MultiplayerManager.MAX_PLAYER_AMOUNT}";

        RefreshPlayerSlots();
    }

    private readonly Dictionary<ulong, LobbyPlayerSingleUI> playerSlotMap = new Dictionary<ulong, LobbyPlayerSingleUI>();

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

        var toRemove = new List<ulong>();
        foreach (var kvp in playerSlotMap)
        {
            if (!currentClientIds.Contains(kvp.Key))
            {
                Destroy(kvp.Value.gameObject);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove)
            playerSlotMap.Remove(id);
    }

    private IEnumerator DelayedAssign(LobbyPlayerSingleUI playerUI, ulong clientId)
    {
        yield return null;
        playerUI.SetPlayer(clientId);
    }

    private void OnDestroy()
    {
        var mm = MultiplayerManager.Instance;
        if (mm != null)
        {
            MultiplayerManager.Instance.OnPlayerDataNetworkListChanged -= UpdateReadyButtonState;
            mm.ScoutsEnabled.OnValueChanged -= OnScoutsRuleChanged;
        }
    }

    private void OnScoutsRuleChanged(bool prev, bool now)
    {
        bool isHost = LobbyManager.Instance.IsLobbyHost();
        if (isHost)
            GamePlayManager.AddScoutCards = now;

        UpdateScoutStatusUI(now, isHost);
    }

    private void UpdateScoutStatusUI(bool enabled, bool isHost)
    {
        if (scoutsStatusLabel == null) return;

        scoutsStatusLabel.text = isHost
            ? (enabled ? "Enabled Scouts" : "Disabled Scouts")
            : (enabled ? "Scouts Enabled" : "Scouts Disabled");

        Color enabledGreen = new Color(0.0f, 0.55f, 0.25f);
        Color disabledRed = new Color(0.86f, 0.22f, 0.22f);
        scoutsStatusLabel.color = enabled ? enabledGreen : disabledRed;
    }
}
