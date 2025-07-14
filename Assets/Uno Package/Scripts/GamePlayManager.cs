using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
#pragma warning disable 0618

public class GamePlayManager : NetworkBehaviour
{
    [Header("Sound")]
    public AudioClip music_win_clip;
    public AudioClip music_loss_clip;
    public AudioClip draw_card_clip;
    public AudioClip throw_card_clip;
    public AudioClip uno_btn_clip;
    public AudioClip choose_color_clip;

    [Header("Gameplay")]
    [Range(0, 100)]
    public int LeftRoomProbability = 10;
    [Range(0, 100)]
    public int UnoProbability = 70;
    [Range(0, 100)]
    public int LowercaseNameProbability = 30;

    public float cardDealTime = 3f;
    public Card _cardPrefab;
    public Transform cardDeckTransform;
    public Image cardWastePile;
    public GameObject arrowObject, unoBtn, cardDeckBtn;
    public Popup colorChoose, playerChoose, noNetwork;
    public GameObject loadingView, rayCastBlocker;
    public Animator cardEffectAnimator;
    public ParticleSystem wildCardParticle;
    public GameObject menuButton;
    public int previousPlayerIndex = -1;
    private Coroutine turnTimeoutCoroutine;
    private bool isJackRevealPhase = false;


    [Header("Player Setting")]
    public List<Player2> players;
    public TextAsset multiplayerNames;
    public TextAsset computerProfiles;
    public bool clockwiseTurn = true;
    public int currentPlayerIndex = 0;
    public Player2 CurrentPlayer { get { return players[currentPlayerIndex]; } }

    [Header("Game Over")]
    public GameObject gameOverPopup;
    public ParticleSystem starParticle;
    public List<GameObject> playerObject;
    public GameObject loseTimerAnimation;
    public GameObject screenCanvas;

    private List<SerializableCard> cards;
    private List<SerializableCard> wasteCards;
    private int peekedDeckIndex = -1;
    private Coroutine turnTimerCoroutine;
    private float turnTimerDuration = 6f;
    private float turnTimerLeft = 0f;
    private bool isTurnEnding = false;



    private Card peekedCard = null;
    private bool hasPeekedCard = false;
    private Dictionary<ulong, SerializableCard> peekedCardsByClientId = new Dictionary<ulong, SerializableCard>();

    public CardType CurrentType
    {
        get { return _currentType; }
        set { _currentType = value; cardWastePile.color = value.GetColor(); }
    }

    public CardValue CurrentValue
    {
        get { return _currentValue; }
        set { _currentValue = value; }
    }

    [SerializeField] CardType _currentType;
    [SerializeField] CardValue _currentValue;

    public bool IsDeckArrow
    {
        get { return arrowObject.activeSelf; }
    }
    public static GamePlayManager instance;

    System.DateTime pauseTime;
    int fastForwardTime = 0;
    bool setup = false, multiplayerLoaded = false, gameOver = false;

    void Start()
    {
        instance = this;
        Input.multiTouchEnabled = false;
        previousPlayerIndex = -1;
    }

    public void StartMultiplayerGame()
    {
        menuButton.SetActive(true);
        if (cardDeckTransform != null)
            cardDeckTransform.gameObject.SetActive(true);
        if (cardWastePile != null)
            cardWastePile.gameObject.SetActive(true);

        if (!IsHost) return;

        currentPlayerIndex = Random.Range(0, players.Count);

        CreateDeck();
        cards.Shuffle();

        int cardsPerPlayer = 3;
        int playerCount = players.Count;

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        ulong[] clientIds = new ulong[playerCount];
        SerializableCard[] allCards = new SerializableCard[playerCount * cardsPerPlayer];

        for (int playerIdx = 0; playerIdx < playerCount; playerIdx++)
        {
            clientIds[playerIdx] = playerList[playerIdx].clientId;
            for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
            {
                int index = playerIdx * cardsPerPlayer + cardSlot;
                allCards[index] = cards[0];
                cards.RemoveAt(0);
            }
        }

        DealCardsClientRpc(clientIds, allCards, cardsPerPlayer, playerCount);

        UpdateDeckVisualClientRpc(cards.ToArray());
    }

    [ClientRpc]
    void DealCardsClientRpc(ulong[] clientIds, SerializableCard[] allCardsFlat, int cardsPerPlayer, int playerCount)
    {
        for (int globalSeat = 0; globalSeat < playerCount; globalSeat++)
        {
            ulong targetClientId = clientIds[globalSeat];
            int localSeat = MapClientIdToLocalSeat(targetClientId);
            if (localSeat < 0 || localSeat >= players.Count) continue;

            var hand = new List<SerializableCard>();
            for (int c = 0; c < cardsPerPlayer; c++)
                hand.Add(allCardsFlat[globalSeat * cardsPerPlayer + c]);

            AssignHandToSeat(localSeat, hand);
        }

        for (int seat = 0; seat < playerCount; seat++)
            players[seat].cardsPanel.UpdatePos();

        StartCoroutine(StartPeekingPhase());
    }

    void AssignHandToSeat(int localSeat, List<SerializableCard> hand)
    {
        var player = players[localSeat];
        player.cardsPanel.Clear();
        for (int j = 0; j < hand.Count; j++)
        {
            var sc = hand[j];
            var card = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, player.cardsPanel.transform);
            card.Type = sc.Type;
            card.Value = sc.Value;
            card.IsOpen = (localSeat == 0);
            card.CalcPoint();
            card.name = $"{sc.Type}_{sc.Value}";
            player.AddCard(card);
            card.transform.SetSiblingIndex(j);
            card.localSeat = localSeat;
            card.cardIndex = j;
            card.IsClickable = true;
            card.onClick = null;
        }
    }

    int MapClientIdToLocalSeat(ulong clientId)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;
        int playerCount = playerList.Count;
        for (int localSeat = 0; localSeat < playerCount; localSeat++)
            if (playerList[(myGlobalSeat + localSeat) % playerCount].clientId == clientId)
                return localSeat;
        return -1; // Not found
    }



    public void SetupNetworkedPlayerSeats()
    {
        for (int i = 0; i < players.Count; i++)
        {
            players[i].gameObject.SetActive(true);
            players[i].CardPanelBG.SetActive(true);
        }

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int totalPlayers = playerList.Count;
        int myIndex = MultiplayerManager.Instance.GetPlayerDataIndexFromClientId(Unity.Netcode.NetworkManager.Singleton.LocalClientId);

        for (int seat = 0; seat < totalPlayers; seat++)
        {
            int dataIndex = (myIndex + seat) % totalPlayers;
            PlayerData pd = playerList[dataIndex];
            Player2 p2 = players[seat];

            p2.SetAvatarProfile(new AvatarProfile
            {
                avatarIndex = pd.avatarIndex,
                avatarName = pd.playerName.ToString()
            });
            p2.isUserPlayer = (seat == 0);
        }
    }

    void CreateDeck()
    {
        cards = new List<SerializableCard>();
        wasteCards = new List<SerializableCard>();

        List<CardValue> values = new List<CardValue>
    {
        CardValue.Zero,
        CardValue.One,
        CardValue.Two,
        CardValue.Three,
        CardValue.Four,
        CardValue.Five,
        CardValue.Six,
        CardValue.Seven,
        CardValue.Eight,
        CardValue.Nine,
        CardValue.Ten,
        CardValue.Jack,
        CardValue.Queen,
        CardValue.King
    };

        for (int j = 0; j < 4; j++)
        {
            foreach (var val in values)
            {
                cards.Add(new SerializableCard((CardType)j, val));
            }
        }

    }

    IEnumerator DealCardsAnimated(List<List<SerializableCard>> allCards, int cardsPerPlayer, int playerCount)
    {
        for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
        {
            for (int seat = 0; seat < playerCount; seat++)
            {
                var player = players[seat];
                var sc = allCards[seat][cardSlot];
                var card = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, player.cardsPanel.transform);
                card.Type = sc.Type;
                card.Value = sc.Value;
                card.IsOpen = (seat == 0);
                card.CalcPoint();
                card.name = $"{sc.Type}_{sc.Value}";
                player.AddCard(card);
                card.transform.SetSiblingIndex(cardSlot);
                card.localSeat = seat;
                card.cardIndex = cardSlot;
                card.IsClickable = true;
                card.onClick = null;

                CardGameManager.PlaySound(throw_card_clip);
            }
            yield return new WaitForSeconds(cardDealTime);
        }

        for (int seat = 0; seat < playerCount; seat++)
        {
            players[seat].cardsPanel.UpdatePos();
        }

        StartCoroutine(StartPeekingPhase());

    }

    private IEnumerator StartPeekingPhase()
    {
        float peekTime = 3f;
        int localPlayerIndex = 0;
        var myCards = players[localPlayerIndex].cardsPanel.cards;
        players[localPlayerIndex].ShowMessage("Peek Time", false, peekTime);

        arrowObject.SetActive(false);
        for (int i = 0; i < cardDeckTransform.childCount; i++)
        {
            var card = cardDeckTransform.GetChild(i).GetComponent<Card>();
            if (card != null)
            {
                card.IsClickable = false;
                card.onClick = null;
            }
        }

        for (int i = 0; i < myCards.Count; i++)
        {
            var card = myCards[i];
            bool canPeek = (i == 0 || i == 2);
            card.IsClickable = canPeek;
            card.PeekMode = canPeek;
            card.IsOpen = false;
            card.onClick = null;

            if (canPeek)
            {
                card.onClick = (c) =>
                {
                    if (c.PeekMode && c.IsClickable)
                    {
                        c.IsOpen = true;
                    }
                };
            }
        }

        for (int pi = 1; pi < players.Count; pi++)
        {
            foreach (var card in players[pi].cardsPanel.cards)
            {
                card.IsClickable = false;
                card.PeekMode = false;
                card.onClick = null;
            }
        }

        yield return new WaitForSeconds(peekTime);

        for (int i = 0; i < myCards.Count; i++)
        {
            var card = myCards[i];
            card.IsOpen = false;
            card.IsClickable = false;
            card.PeekMode = false;
            card.onClick = null;
        }

        if (IsHost)
        {
            currentPlayerIndex = 0;
            StartPlayerTurnForAllClientRpc(currentPlayerIndex);
        }
    }

    int GetGlobalIndexFromLocal(int localIndex)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;
        int playerCount = playerList.Count;
        return (myGlobalSeat + localIndex) % playerCount;
    }

    int GetLocalIndexFromGlobal(int globalIndex)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;
        int playerCount = playerList.Count;
        for (int localIndex = 0; localIndex < playerCount; localIndex++)
            if ((myGlobalSeat + localIndex) % playerCount == globalIndex)
                return localIndex;
        return 0;
    }
    public void NextPlayerIndex()
    {
        int step = clockwiseTurn ? 1 : -1;
        do
        {
            currentPlayerIndex = Mod(currentPlayerIndex + step, players.Count);
        } while (!players[currentPlayerIndex].isInRoom);
    }

    private bool IsMyTurn()
    {
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalIndex = -1;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalIndex = i;
        return (myGlobalIndex == GetGlobalIndexFromLocal(currentPlayerIndex));
    }

    [ClientRpc]
    void StartPlayerTurnForAllClientRpc(int globalPlayerIndex)
    {
        isTurnEnding = false;
        isJackRevealPhase = false;
        ulong curClientId = MultiplayerManager.Instance.playerDataNetworkList[GetGlobalIndexFromLocal(currentPlayerIndex)].clientId;
        peekedCardsByClientId.Remove(curClientId);
        hasPeekedCard = false;
        peekedCard = null;
        DisableAllHandCardGlowAllPlayers();
        unoBtn.SetActive(false);
        arrowObject.SetActive(false);

        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);

        if (previousPlayerIndex >= 0 && previousPlayerIndex < players.Count)
            players[previousPlayerIndex].OnTurnEnd();

        currentPlayerIndex = localIndex;
        CurrentPlayer.OnTurn();
        previousPlayerIndex = currentPlayerIndex;

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalIndex = -1;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalIndex = i;

        if (myGlobalIndex == globalPlayerIndex && players[0].isUserPlayer)
        {
            EnableDeckClick();
            UpdateDeckClickability();
        }
        else
        {
            arrowObject.SetActive(false);
            UpdateDeckClickability();
        }

        if (IsHost)
        {
            if (turnTimeoutCoroutine != null)
            {
                StopCoroutine(turnTimeoutCoroutine);
                turnTimeoutCoroutine = null;
            }
            turnTimeoutCoroutine = StartCoroutine(HostTurnTimeoutRoutine());
        }
    }

    private void EndTurnAndGoNextPlayer()
    {
        if (isTurnEnding) return;
        isTurnEnding = true;

        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }

        CurrentPlayer.OnTurnEnd();
        EndTurnForAllClientRpc();

        if (IsHost)
        {
            NextPlayerTurn();
        }
    }

    void OnJackCardDiscardedByMe()
    {
        isJackRevealPhase = true;
        for (int seat = 0; seat < players.Count; seat++)
        {
            var p = players[seat];
            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
            {
                var handCard = p.cardsPanel.cards[i];
                handCard.ShowGlow(true);
                handCard.IsClickable = true;
                int s = seat, idx = i;
                handCard.onClick = null;
                handCard.onClick = (clickedCard) =>
                {
                    if (!isJackRevealPhase) return;
                    isJackRevealPhase = false;
                    DisableAllHandCardGlowAllPlayers();
                    int globalPlayerIndex = GetGlobalIndexFromLocal(s);
                    RequestRevealHandCardServerRpc(globalPlayerIndex, idx);
                    UpdateDeckClickability();
                };

            }
        }
        if (players[0].isUserPlayer)
        {
            players[0].SetTimerVisible(true);
            players[0].UpdateTurnTimerUI(turnTimerDuration, turnTimerDuration);
        }
    }

    IEnumerator HideCardAfterDelay(Card card, float delay)
    {
        yield return new WaitForSeconds(delay);
        card.IsOpen = false;
    }

    [ClientRpc]
    void RevealHandCardClientRpc(
    int playerIndex, int cardIndex, CardType type, CardValue value,
    ClientRpcParams rpcParams = default)
    {
        var p = players[GetLocalIndexFromGlobal(playerIndex)];
        var card = p.cardsPanel.cards[cardIndex];

        card.Type = type;
        card.Value = value;
        card.IsOpen = true;
        StartCoroutine(HideCardAfterDelay(card, 1f));
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealHandCardServerRpc(int playerIndex, int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();
        var handCard = players[playerIndex].cardsPanel.cards[cardIndex];

        ulong jackUserClientId = serverRpcParams.Receive.SenderClientId;

        RevealHandCardClientRpc(
            playerIndex, cardIndex, handCard.Type, handCard.Value,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new List<ulong> { jackUserClientId }
                }
            }
        );

        OnJackRevealDoneServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void OnJackRevealDoneServerRpc(ServerRpcParams rpcParams = default)
    {
        isJackRevealPhase = false;
        if (IsHost)
            StartCoroutine(DelayedNextPlayerTurn(0.5f));
    }


    private void ResetAndRestartTurnTimerCoroutine()
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        turnTimerLeft = turnTimerDuration;
        turnTimeoutCoroutine = StartCoroutine(HostTurnTimeoutRoutine());
    }


    void DisableAllHandCardGlowAllPlayers()
    {
        foreach (var p in players)
            foreach (var card in p.cardsPanel.cards)
            {
                card.ShowGlow(false);
                card.IsClickable = false;
                card.onClick = null;
            }
    }

    [ClientRpc]
    void StartJackRevealLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        OnJackCardDiscardedByMe();
    }


    [ClientRpc]
    void EndTurnForAllClientRpc()
    {
        foreach (var p in players)
            p.OnTurnEnd();
    }

    public void NextPlayerTurn()
    {
        if (!IsHost) return;
        NextPlayerIndex();
        StartPlayerTurnForAllClientRpc(GetGlobalIndexFromLocal(currentPlayerIndex));
    }

    int GetPlayerIndexFromClientId(ulong clientId)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == clientId)
                return i;
        return -1;
    }

    public void EnableDeckClick()
    {
        arrowObject.SetActive(true);
    }

    [ClientRpc]
    void BroadcastTurnTimerClientRpc(float secondsLeft, float totalSeconds)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (i == currentPlayerIndex)
            {
                players[i].SetTimerVisible(true);
                players[i].UpdateTurnTimerUI(secondsLeft, totalSeconds);
            }
            else
            {
                players[i].SetTimerVisible(false);
            }
        }
    }

    [ClientRpc]
    void ResetTurnTimerClientRpc(int playerIndex, float totalSeconds)
    {
        for (int i = 0; i < players.Count; i++)
        {
            players[i].SetTimerVisible(i == playerIndex);
            players[i].UpdateTurnTimerUI(totalSeconds, totalSeconds);
        }
    }

    private IEnumerator HostTurnTimeoutRoutine()
    {
        turnTimerLeft = turnTimerDuration;
        while (turnTimerLeft > 0f)
        {
            BroadcastTurnTimerClientRpc(turnTimerLeft, turnTimerDuration);
            yield return new WaitForSeconds(0.1f);
            turnTimerLeft -= 0.1f;
        }
        BroadcastTurnTimerClientRpc(0f, turnTimerDuration);

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        ulong currentTurnClientId = playerList[GetGlobalIndexFromLocal(currentPlayerIndex)].clientId;

        if (peekedCardsByClientId.ContainsKey(currentTurnClientId))
        {
            var serCard = peekedCardsByClientId[currentTurnClientId];
            DiscardPeekedCardServerRpc((int)serCard.Type, (int)serCard.Value);
            yield return new WaitForSeconds(0.3f);
        }

        if (isTurnEnding) yield break;
        EndTurnAndGoNextPlayer();
    }

    public void UpdateDeckClickability()
    {
        if (isJackRevealPhase)
        {
            arrowObject.SetActive(false);
            int n = cardDeckTransform.childCount;
            for (int i = 0; i < n; i++)
            {
                Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
                if (c == null) continue;
                c.IsClickable = false;
                c.onClick = null;
            }
            return;
        }

        if (players != null && players.Count > 0 && players[0].isUserPlayer && IsMyTurn() && !hasPeekedCard)
        {
            int n = cardDeckTransform.childCount;
            for (int i = 0; i < n; i++)
            {
                Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
                if (c == null) continue;
                c.IsClickable = true;
                c.onClick = (card) =>
                {
                    OnDeckClickedByPlayer();
                };
            }
        }
        else
        {
            int n = cardDeckTransform.childCount;
            for (int i = 0; i < n; i++)
            {
                Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
                if (c == null) continue;
                c.IsClickable = false;
                c.onClick = null;
            }
        }
    }

    [ClientRpc]
    void UpdateDeckVisualClientRpc(SerializableCard[] deckCards)
    {
        for (int i = cardDeckTransform.childCount - 1; i >= 0; i--)
            Destroy(cardDeckTransform.GetChild(i).gameObject);

        cards = new List<SerializableCard>(deckCards);

        for (int i = 0; i < deckCards.Length; i++)
        {
            var sc = deckCards[i];
            var card = Instantiate(_cardPrefab, cardDeckTransform);
            card.IsOpen = false;
            card.Type = sc.Type;
            card.Value = sc.Value;
            card.CalcPoint();
            card.name = $"{sc.Type}_{sc.Value}";

            card.transform.localPosition = new Vector3(
                Random.Range(-2f, 2f),
                0,
                -i * 1.15f
            );
        }

        UpdateDeckClickability();
    }

    public void OnDeckClickedByPlayer()
    {
        if (players == null || players.Count == 0 || players[0] == null) return;
        if (unoBtn == null) return;
        if (!players[0].isUserPlayer || !IsMyTurn()) return;
        if (hasPeekedCard) return;
        if (cardDeckTransform.childCount == 0) return;

        peekedDeckIndex = cards.Count - 1;

        Transform topCardTf = cardDeckTransform.GetChild(cardDeckTransform.childCount - 1);
        Card card = topCardTf.GetComponent<Card>();
        if (card == null) return;

        card.IsOpen = true;

        peekedCard = card;
        hasPeekedCard = true;

        unoBtn.SetActive(true);
        unoBtn.GetComponent<Button>().interactable = true;
        unoBtn.GetComponent<Button>().onClick.RemoveAllListeners();
        unoBtn.GetComponent<Button>().onClick.AddListener(OnDiscardClicked);

        EnableHandCardReplacementGlow();

        ulong myClientId = NetworkManager.Singleton.LocalClientId;

        var peekedSerCard = new SerializableCard(card.Type, card.Value);
        peekedCardsByClientId[myClientId] = peekedSerCard;

        if (!IsHost || (IsHost && !NetworkManager.Singleton.IsServer))
            NotifyHostPeekedCardServerRpc((int)card.Type, (int)card.Value);
    }

    [ServerRpc(RequireOwnership = false)]
    void NotifyHostPeekedCardServerRpc(int cardType, int cardValue, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        var peekedCard = new SerializableCard((CardType)cardType, (CardValue)cardValue);
        peekedCardsByClientId[clientId] = peekedCard;

        Debug.Log($"Client {clientId} peeked card: {peekedCard.Type} {peekedCard.Value}");
    }

    public Card PickCardFromDeck(Player2 p, bool updatePos = false)
    {
        if (cards.Count == 0)
        {
            Debug.Log("Card Over");
            while (wasteCards.Count > 5)
            {
                cards.Add(wasteCards[0]);
                wasteCards.RemoveAt(0);
            }
            UpdateDeckVisualClientRpc(cards.ToArray());
        }

        SerializableCard topCard = cards[0];
        cards.RemoveAt(0);

        Card temp = Instantiate(_cardPrefab, p.cardsPanel.transform);
        temp.Type = topCard.Type;
        temp.Value = topCard.Value;
        temp.IsOpen = p.isUserPlayer;
        temp.CalcPoint();
        temp.name = $"{topCard.Type}_{topCard.Value}";

        p.AddCard(temp);

        if (updatePos)
            p.cardsPanel.UpdatePos();
        else
            temp.SetTargetPosAndRot(Vector3.zero, 0f);

        CardGameManager.PlaySound(throw_card_clip);
        UpdateDeckVisualClientRpc(cards.ToArray());

        return temp;
    }

    IEnumerator JackRevealRoutine(Player2 p, Card revealedCard)
    {
        yield return new WaitForSeconds(1f);
        revealedCard.IsOpen = false;
        DisableAllHandCardGlowAllPlayers();

        hasPeekedCard = true;
        UpdateDeckClickability();

        players[0].SetTimerVisible(false);
        if (!IsHost)
            OnJackRevealDoneServerRpc();
        else
            EndTurnAndGoNextPlayer();
    }

    void EnableHandCardReplacementGlow()
    {
        for (int i = 0; i < players[0].cardsPanel.cards.Count; i++)
        {
            var handCard = players[0].cardsPanel.cards[i];
            handCard.ShowGlow(true);
            handCard.IsClickable = true;
            int handIndex = i;
            handCard.onClick = null;
            handCard.onClick = (clickedCard) =>
            {
                DisableAllHandCardGlow();
                OnHandCardReplaceRequested(handIndex);
            };
        }
    }

    public void OnDiscardClicked()
    {
        if (!hasPeekedCard || peekedCard == null) return;

        var discardValue = peekedCard.Value;
        unoBtn.SetActive(false);
        DisableAllHandCardGlow();
        RequestDiscardPeekedCardServerRpc(discardValue);

        if (discardValue == CardValue.Jack || discardValue == CardValue.Queen || discardValue == CardValue.King)
        {
            isJackRevealPhase = true;
            arrowObject.SetActive(false);
            UpdateDeckClickability();

            if (players[0].isUserPlayer)
            {
                players[0].SetTimerVisible(true);
                players[0].UpdateTurnTimerUI(turnTimerDuration, turnTimerDuration);
            }

            if (IsHost)
            {
                OnJackCardDiscardedByMe();
            }
        }

    }



    [ServerRpc(RequireOwnership = false)]
    void DiscardPeekedCardServerRpc(int cardType, int cardValue, ServerRpcParams rpcParams = default)
    {
        if (cards.Count == 0) return;

        SerializableCard topCard = cards[cards.Count - 1];

        if ((int)topCard.Type != cardType || (int)topCard.Value != cardValue)
        {
            Debug.LogWarning("DiscardPeekedCardServerRpc: Mismatch! Passing turn without discard.");
            EndTurnAndGoNextPlayer();
            return;
        }


        cards.RemoveAt(cards.Count - 1);
        wasteCards.Add(topCard);

        ShowWastePileCardClientRpc(cardType, cardValue);

        Debug.Log($"Remaining cards left in Deck: {cards.Count}");

        DiscardPeekedCardClientRpc(cardType, cardValue, cards.ToArray());

        ForceDiscardClientRpc();

        EndTurnAndGoNextPlayer();
    }

    [ClientRpc]
    void ForceDiscardClientRpc()
    {
        if (peekedCard != null)
        {
            Destroy(peekedCard.gameObject);
            peekedCard = null;
            hasPeekedCard = false;
        }
    }

    [ClientRpc]
    void DiscardPeekedCardClientRpc(int cardType, int cardValue, SerializableCard[] deckCards)
    {
        UpdateDeckVisualClientRpc(deckCards);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestDiscardPeekedCardServerRpc(CardValue discardValue, ServerRpcParams rpcParams = default)
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        int playerIndex = GetPlayerIndexFromClientId(senderClientId);
        if (playerIndex < 0 || cards.Count == 0) return;

        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);

        wasteCards.Add(drawn);

        DiscardPeekedCardWithVisualClientRpc(playerIndex, drawn, cards.ToArray(), wasteCards.ToArray());

        if (discardValue == CardValue.Jack || discardValue == CardValue.Queen || discardValue == CardValue.King)
        {
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);

            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();

            StartJackRevealLocalOnlyClientRpc(
                senderClientId,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                }
            );

            return;
        }
        if (IsHost)
            StartCoroutine(DelayedNextPlayerTurn(0.5f));
        else
            EndTurnForAllClientRpc();
    }

    [ClientRpc]
    void DiscardPeekedCardWithVisualClientRpc(int playerIndex, SerializableCard discardedCard, SerializableCard[] deck, SerializableCard[] wastePile)
    {
        if (peekedCard != null)
        {
            Destroy(peekedCard.gameObject);
            peekedCard = null;
            hasPeekedCard = false;
        }

        var wasteObj = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, cardWastePile.transform);
        wasteObj.Type = discardedCard.Type;
        wasteObj.Value = discardedCard.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();
        wasteObj.name = $"{wasteObj.Type}_{wasteObj.Value}";

        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(wasteObj, cardDeckTransform.position, cardWastePile.transform.position, 0.3f, randomRot));
        UpdateDeckVisualClientRpc(deck);
        OnUnoClick();
    }

    IEnumerator AnimateCardMove(Card card, Vector3 from, Vector3 to, float duration, float? targetZRot = null)
    {
        if (card == null) yield break;

        float elapsed = 0;
        Quaternion startRot = card.transform.rotation;
        Quaternion endRot = targetZRot.HasValue
            ? Quaternion.Euler(0, 0, targetZRot.Value)
            : card.transform.rotation;

        while (elapsed < duration)
        {
            if (card == null) yield break;

            card.transform.position = Vector3.Lerp(from, to, elapsed / duration);
            card.transform.rotation = Quaternion.Slerp(startRot, endRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (card == null) yield break;
        card.transform.position = to;
        card.transform.rotation = endRot;

        if (cardWastePile != null)
        {
            card.transform.SetParent(cardWastePile.transform, true);
            card.transform.SetAsLastSibling();
            card.transform.localPosition = Vector3.zero;
        }
    }

    public void OnHandCardReplaceRequested(int handIndex)
    {
        RequestReplaceHandCardServerRpc(handIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestReplaceHandCardServerRpc(int handIndex, ServerRpcParams rpcParams = default)
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        int playerIndex = GetPlayerIndexFromClientId(senderClientId);
        if (playerIndex < 0 || playerIndex >= players.Count) return;
        if (cards.Count == 0) return;

        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);

        Player2 p = players[playerIndex];
        Card replacedCard = p.cardsPanel.cards[handIndex];
        SerializableCard replacedSer = new SerializableCard(replacedCard.Type, replacedCard.Value);

        wasteCards.Add(replacedSer);

        ReplaceHandCardClientRpc(playerIndex, handIndex, drawn, replacedSer, cards.ToArray(), wasteCards.ToArray());

        if (IsHost)
        {
            StartCoroutine(DelayedNextPlayerTurn(0.5f));
        }
    }

    IEnumerator DelayedNextPlayerTurn(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (isTurnEnding) yield break;
        EndTurnAndGoNextPlayer();
    }

    [ClientRpc]
    void ReplaceHandCardClientRpc(int playerIndex, int handIndex, SerializableCard newCard, SerializableCard waste, SerializableCard[] deck, SerializableCard[] wastePile)
    {
        int localSeat = GetLocalIndexFromGlobal(playerIndex);
        var p = players[localSeat];

        Vector3 handSlotPos = p.cardsPanel.cards[handIndex].transform.position;
        var deckCard = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, p.cardsPanel.transform.parent);
        deckCard.Type = newCard.Type;
        deckCard.Value = newCard.Value;
        deckCard.IsOpen = p.isUserPlayer;
        deckCard.CalcPoint();
        StartCoroutine(AnimateCardMove(deckCard, cardDeckTransform.position, handSlotPos, 0.3f));
        Destroy(deckCard.gameObject, 0.3f);

        Card cardToRemove = p.cardsPanel.cards[handIndex];
        Vector3 handCardPos = cardToRemove.transform.position;
        var wasteObj = Instantiate(_cardPrefab, handCardPos, Quaternion.identity, cardWastePile.transform.parent);
        wasteObj.Type = waste.Type;
        wasteObj.Value = waste.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();

        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(wasteObj, handCardPos, cardWastePile.transform.position, 0.3f, randomRot));

        if (IsMyLocalPlayer(playerIndex))
        {
            StartCoroutine(DelayedReplaceHandCard(p, handIndex, newCard, cardToRemove, 0.35f));
        }
        else
        {
            p.RemoveCard(cardToRemove);
            p.AddSerializableCard(newCard, handIndex);
        }

        UpdateDeckVisualClientRpc(deck);
        OnUnoClick();
    }


    bool IsMyLocalPlayer(int playerIndex)
    {
        int myLocalSeat = 0;
        int mappedSeat = GetLocalIndexFromGlobal(playerIndex);
        return mappedSeat == myLocalSeat;
    }



    IEnumerator DelayedReplaceHandCard(Player2 p, int handIndex, SerializableCard newCard, Card cardToRemove, float delay)
    {
        yield return new WaitForSeconds(delay);
        p.RemoveCard(cardToRemove);
        p.AddSerializableCard(newCard, handIndex);
    }

    void DisableAllHandCardGlow()
    {
        foreach (var card in players[0].cardsPanel.cards)
        {
            card.ShowGlow(false);
            card.IsClickable = false;
            card.onClick = null;
        }
    }

    [ClientRpc]
    void ShowWastePileCardClientRpc(int cardType, int cardValue)
    {
        var discard = Instantiate(_cardPrefab, cardWastePile.transform);
        discard.Type = (CardType)cardType;
        discard.Value = (CardValue)cardValue;
        discard.IsOpen = true;
        discard.CalcPoint();
        discard.name = $"{discard.Type}_{discard.Value}";

        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(discard, cardDeckTransform.position, cardWastePile.transform.position, 0.3f, randomRot));
        OnUnoClick();
    }

    public void PutCardToWastePile(Card c, Player2 p = null)
    {
        if (p != null)
        {
            c.ShowGlow(false);
            p.RemoveCard(c);
            if (p.cardsPanel.cards.Count == 1 && !p.unoClicked)
                CardGameManager.PlaySound(draw_card_clip);
        }

        CurrentType = c.Type;
        CurrentValue = c.Value;

        SerializableCard serializableDiscard = new SerializableCard(c.Type, c.Value);
        wasteCards.Add(serializableDiscard);



        if (p != null)
        {
            if (p.cardsPanel.cards.Count == 0)
            {
                Invoke("SetupGameOver", 2f);
                return;
            }
        }
    }

    private int Mod(int x, int m)
    {
        return (x % m + m) % m;
    }



    public void DisableUnoBtn()
    {
        unoBtn.GetComponent<Button>().interactable = false;
    }

    public void OnUnoClick()
    {
        DisableUnoBtn();
        CurrentPlayer.ShowMessage("Uno!", true);
        CurrentPlayer.unoClicked = true;
        CardGameManager.PlaySound(uno_btn_clip);
    }

    public void SetupGameOver()
    {
        gameOver = true;
        for (int i = players.Count - 1; i >= 0; i--)
        {
            if (!players[i].isInRoom)
            {
                players.RemoveAt(i);
            }
        }

        if (players.Count == 2)
        {
            playerObject[0].SetActive(true);
            playerObject[2].SetActive(true);
            playerObject[2].transform.GetChild(2).GetComponent<Text>().text = "2nd Place";
            playerObject.RemoveAt(3);
            playerObject.RemoveAt(1);

        }
        else if (players.Count == 3)
        {
            playerObject.RemoveAt(2);
            for (int i = 0; i < 3; i++)
            {
                playerObject[i].SetActive(true);
            }
            playerObject[2].transform.GetChild(2).GetComponent<Text>().text = "3rd Place";

        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                playerObject[i].SetActive(true);
            }
        }

        players.Sort((x, y) => x.GetTotalPoints().CompareTo(y.GetTotalPoints()));
        var winner = players[0];

        starParticle.gameObject.SetActive(winner.isUserPlayer);
        playerObject[0].GetComponentsInChildren<Image>()[1].sprite = winner.avatarImage.sprite;

        for (int i = 1; i < playerObject.Count; i++)
        {
            var playerNameText = playerObject[i].GetComponentInChildren<Text>();
            playerNameText.text = players[i].playerName;
            playerNameText.GetComponent<EllipsisText>().UpdateText();
            playerObject[i].GetComponentsInChildren<Image>()[1].sprite = players[i].avatarImage.sprite;
        }

        CardGameManager.PlaySound(winner.isUserPlayer ? music_win_clip : music_loss_clip);
        gameOverPopup.SetActive(true);
        screenCanvas.SetActive(false);

        for (int i = 1; i < players.Count; i++)
        {
            if (players[i].isUserPlayer)
            {
                loseTimerAnimation.SetActive(true);
                loseTimerAnimation.transform.position = playerObject[i].transform.position;
                break;
            }
        }

        gameOverPopup.GetComponentInChildren<Text>().text = winner.isUserPlayer ? "You win Game." : "You Lost Game ...   Try Again.";
        fastForwardTime = 0;
    }

    IEnumerator CheckNetwork()
    {
        while (true)
        {
            WWW www = new WWW("https://www.google.com");
            yield return www;
            if (string.IsNullOrEmpty(www.error))
            {
                if (noNetwork.isOpen)
                {
                    noNetwork.HidePopup();

                    Time.timeScale = 1;
                    OnApplicationPause(false);
                }
            }
            else
            {
                if (Time.timeScale == 1)
                {
                    noNetwork.ShowPopup();

                    Time.timeScale = 0;
                    pauseTime = System.DateTime.Now;
                }
            }

            yield return new WaitForSecondsRealtime(1f);
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            pauseTime = System.DateTime.Now;
        }
        else
        {
            if (CardGameManager.currentGameMode == GameMode.MultiPlayer && multiplayerLoaded && !gameOver)
            {
                fastForwardTime += Mathf.Clamp((int)(System.DateTime.Now - pauseTime).TotalSeconds, 0, 3600);
                if (Time.timeScale == 1f)
                {
                    StartCoroutine(DoFastForward());
                }
            }
        }
    }

    IEnumerator DoFastForward()
    {
        Time.timeScale = 10f;
        rayCastBlocker.SetActive(true);
        while (fastForwardTime > 0)
        {
            yield return new WaitForSeconds(1f);
            fastForwardTime--;
        }
        Time.timeScale = 1f;
        rayCastBlocker.SetActive(false);

    }

    private void FreezeTimerUI()
    {
        BroadcastTurnTimerClientRpc(turnTimerLeft, turnTimerDuration);
    }

}

[System.Serializable]
public struct SerializableCard : INetworkSerializable
{
    public CardType Type;
    public CardValue Value;
    public SerializableCard(CardType t, CardValue v) { Type = t; Value = v; }

    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
    {
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref Value);
    }


}

[System.Serializable]
public class AvatarProfile
{
    public int avatarIndex;
    public string avatarName;
}

public class AvatarProfiles
{
    public List<AvatarProfile> profiles;
}