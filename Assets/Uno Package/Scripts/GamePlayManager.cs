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


    private Card peekedCard = null;
    private bool hasPeekedCard = false;

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

    void CreateDeck()
    {
        cards = new List<SerializableCard>();
        wasteCards = new List<SerializableCard>();
        for (int j = 1; j <= 4; j++)
        {
            cards.Add(new SerializableCard(CardType.Other, CardValue.Wild));
            cards.Add(new SerializableCard(CardType.Other, CardValue.DrawFour));
        }
        for (int i = 0; i <= 4; i++)
        {
            for (int j = 1; j <= 4; j++)
            {
                cards.Add(new SerializableCard((CardType)j, (CardValue)i));
                cards.Add(new SerializableCard((CardType)j, (CardValue)i));
            }
        }
    }

    [ClientRpc]
    void DealCardsClientRpc(SerializableCard[] allCardsFlat, int cardsPerPlayer, int playerCount)
    {
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;

        List<List<SerializableCard>> allCards = new List<List<SerializableCard>>();
        for (int localSeat = 0; localSeat < playerCount; localSeat++)
        {
            int globalSeat = (myGlobalSeat + localSeat) % playerCount;
            var hand = new List<SerializableCard>();
            for (int c = 0; c < cardsPerPlayer; c++)
                hand.Add(allCardsFlat[globalSeat * cardsPerPlayer + c]);
            allCards.Add(hand);
        }
        StartCoroutine(DealCardsAnimated(allCards, cardsPerPlayer, playerCount));
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

    [ClientRpc]
    void StartPlayerTurnForAllClientRpc(int globalPlayerIndex)
    {
        hasPeekedCard = false;
        peekedCard = null;
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

    private IEnumerator HostTurnTimeoutRoutine()
    {
        yield return new WaitForSeconds(6f);

        if (IsHost)
        {
            Debug.Log($"Player {currentPlayerIndex} turn timed out. Passing turn...");

            if (hasPeekedCard && peekedCard != null)
            {
                int cardType = (int)peekedCard.Type;
                int cardValue = (int)peekedCard.Value;

                DiscardPeekedCardServerRpc(cardType, cardValue);

                peekedCard = null;
                hasPeekedCard = false;
            }
            else
            {
                players[currentPlayerIndex].OnTurnEnd();
                NextPlayerTurn();
            }
        }
    }

    public void OnDeckClickedByPlayer()
    {
        if (players == null || players.Count == 0 || players[0] == null) return;
        if (unoBtn == null) return;
        if (!players[0].isUserPlayer || !IsMyTurn()) return;
        if (hasPeekedCard) return;
        if (cardDeckTransform.childCount == 0) return;

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
    }

    public void UpdateDeckClickability()
    {
        int n = cardDeckTransform.childCount;
        for (int i = 0; i < n; i++)
        {
            Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
            if (c == null) continue;

            if (i == n - 1 && players.Count > 0 && players[0].isUserPlayer && IsMyTurn())
            {
                c.IsClickable = true;
                c.onClick = (card) => OnDeckClickedByPlayer();
            }
            else
            {
                c.IsClickable = false;
                c.onClick = null;
            }
        }
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

    public void OnDiscardClicked()
    {
        if (!hasPeekedCard || peekedCard == null) return;

        unoBtn.SetActive(false);

        int cardType = (int)peekedCard.Type;
        int cardValue = (int)peekedCard.Value;

        Destroy(peekedCard.gameObject);

        peekedCard = null;
        hasPeekedCard = false;

        CurrentPlayer.Timer = false;

        DiscardPeekedCardServerRpc(cardType, cardValue);
    }

    [ServerRpc(RequireOwnership = false)]
    void DiscardPeekedCardServerRpc(int cardType, int cardValue, ServerRpcParams rpcParams = default)
    {
        if (cards.Count == 0) return;

        SerializableCard topCard = cards[cards.Count - 1];

        if ((int)topCard.Type != cardType || (int)topCard.Value != cardValue)
        {
            Debug.LogError("Mismatch! Peeked card and actual top card are different!");
            return;
        }

        cards.RemoveAt(cards.Count - 1);
        wasteCards.Add(topCard);

        Debug.Log($"Remaining cards left in Deck: {cards.Count}");

        DiscardPeekedCardClientRpc(cardType, cardValue, cards.ToArray());

        players[currentPlayerIndex].OnTurnEnd();
        NextPlayerTurn();
    }


    [ClientRpc]
    void DiscardPeekedCardClientRpc(int cardType, int cardValue, SerializableCard[] deckCards)
    {
        UpdateDeckVisualClientRpc(deckCards);

        var discard = Instantiate(_cardPrefab, cardWastePile.transform);
        discard.Type = (CardType)cardType;
        discard.Value = (CardValue)cardValue;
        discard.IsOpen = true;
        discard.CalcPoint();
        discard.name = $"{discard.Type}_{discard.Value}";

        discard.transform.localPosition = Vector3.zero;
        discard.SetTargetPosAndRot(
            new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 1),
            Random.Range(-15f, 15f)
        );
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


    IEnumerator DealCardsToPlayer(Player2 p, int NoOfCard = 1, float delay = 0f)
    {
        yield return new WaitForSeconds(delay);
        for (int t = 0; t < NoOfCard; t++)
        {
            PickCardFromDeck(p, true);
            yield return new WaitForSeconds(cardDealTime);
        }
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


    public void PutCardToWastePile(Card c, Player2 p = null)
    {
        if (p != null)
        {
            p.RemoveCard(c);
            if (p.cardsPanel.cards.Count == 1 && !p.unoClicked)
            {
                ApplyUnoCharge(CurrentPlayer);
            }
            CardGameManager.PlaySound(draw_card_clip);
        }

        CurrentType = c.Type;
        CurrentValue = c.Value;

        SerializableCard serializableDiscard = new SerializableCard(c.Type, c.Value);
        wasteCards.Add(serializableDiscard);

        c.IsOpen = true;
        c.transform.SetParent(cardWastePile.transform, true);
        c.SetTargetPosAndRot(
            new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 1),
            c.transform.localRotation.eulerAngles.z + Random.Range(-15f, 15f)
        );

        if (p != null)
        {
            if (p.cardsPanel.cards.Count == 0)
            {
                Invoke("SetupGameOver", 2f);
                return;
            }
            if (c.Type == CardType.Other)
            {
                CurrentPlayer.Timer = true;
                CurrentPlayer.choosingColor = true;
                if (CurrentPlayer.isUserPlayer)
                {
                    colorChoose.ShowPopup();
                }
                else
                {
                    Invoke("ChooseColorforAI", Random.Range(3f, 9f));
                }
            }
            else
            {
                if (c.Value == CardValue.Reverse)
                {
                    clockwiseTurn = !clockwiseTurn;
                    cardEffectAnimator.Play(clockwiseTurn ? "ClockWiseAnim" : "AntiClockWiseAnim");
                    Invoke("NextPlayerTurn", 1.5f);
                }
                else if (c.Value == CardValue.Skip)
                {
                    NextPlayerIndex();
                    CurrentPlayer.ShowMessage("Turn Skipped!");
                    Invoke("NextPlayerTurn", 1.5f);
                }
                else if (c.Value == CardValue.DrawTwo)
                {
                    NextPlayerIndex();
                    CurrentPlayer.ShowMessage("+2");
                    wildCardParticle.Emit(30);
                    StartCoroutine(DealCardsToPlayer(CurrentPlayer, 2, .5f));
                    Invoke("NextPlayerTurn", 1.5f);
                }
                else
                {
                    NextPlayerTurn();
                }
            }
        }
    }

    void ChooseColorforAI()
    {
        CurrentPlayer.ChooseBestColor();
    }

    public void NextPlayerIndex()
    {
        int step = clockwiseTurn ? 1 : -1;
        do
        {
            currentPlayerIndex = Mod(currentPlayerIndex + step, players.Count);
        } while (!players[currentPlayerIndex].isInRoom);
    }

    private int Mod(int x, int m)
    {
        return (x % m + m) % m;
    }

    public void NextPlayerTurn()
    {
        if (!IsHost) return;
        NextPlayerIndex();
        StartPlayerTurnForAllClientRpc(GetGlobalIndexFromLocal(currentPlayerIndex));
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

    public void EnableDeckClick()
    {
        arrowObject.SetActive(true);
    }

    public void OnDeckClick()
    {
        if (!setup) return;

        if (arrowObject.activeInHierarchy)
        {

            arrowObject.SetActive(false);
            CurrentPlayer.pickFromDeck = true;
            PickCardFromDeck(CurrentPlayer, true);
            if (!CurrentPlayer.Timer && CurrentPlayer.isUserPlayer)
            {
                CurrentPlayer.OnTurnEnd();
                NextPlayerTurn();
            }
        }
        else if (!CurrentPlayer.pickFromDeck && CurrentPlayer.isUserPlayer)
        {
            PickCardFromDeck(CurrentPlayer, true);
            CurrentPlayer.pickFromDeck = true;
        }
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

    public void ApplyUnoCharge(Player2 p)
    {
        DisableUnoBtn();
        CurrentPlayer.ShowMessage("Uno Charges");
        StartCoroutine(DealCardsToPlayer(p, 2, .3f));
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

        CreateDeck();
        cards.Shuffle();

        int cardsPerPlayer = 3;
        int playerCount = players.Count;
        SerializableCard[] allCards = new SerializableCard[playerCount * cardsPerPlayer];
        for (int playerIdx = 0; playerIdx < playerCount; playerIdx++)
            for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
            {
                allCards[playerIdx * cardsPerPlayer + cardSlot] = cards[0];
                cards.RemoveAt(0);
            }

        DealCardsClientRpc(allCards, cardsPerPlayer, playerCount);
        UpdateDeckVisualClientRpc(cards.ToArray());

        int a = cards.FindIndex(c => c.Type != CardType.Other);
        var waste = cards[a];
        cards.RemoveAt(a);
        wasteCards.Add(waste);
        SetWastePileClientRpc(waste);

    }

    [ClientRpc]
    void SetWastePileClientRpc(SerializableCard wasteCard)
    {
        var card = Instantiate(_cardPrefab, cardWastePile.transform);
        card.transform.localPosition = Vector3.zero;
        card.Type = wasteCard.Type;
        card.Value = wasteCard.Value;
        card.IsOpen = true;
        card.CalcPoint();
        card.name = wasteCard.Type.ToString() + "_" + wasteCard.Value.ToString();


        card.transform.position = cardWastePile.transform.position;
        card.SetTargetPosAndRot(new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 1), card.transform.localRotation.eulerAngles.z + Random.Range(-15f, 15f));
    }

    [ClientRpc]
    void UpdateDeckVisualClientRpc(SerializableCard[] deckCards)
    {
        // Destroy all visual cards in deck first
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