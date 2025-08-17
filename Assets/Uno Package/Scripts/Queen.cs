using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Queen : NetworkBehaviour
{
    public static Queen Instance;

    private Card firstCardSelected = null;
    private Card secondCardSelected = null;
    public bool isQueenSwapPhase = false;

    private int firstLocalSeat = -1;
    private int firstCardIndex = -1;

    private GamePlayManager gpm => GamePlayManager.instance;

    void Awake() => Instance = this;

    // normal Queen flow
    public void StartQueenSwap()
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;

        isQueenSwapPhase = true;
        firstCardSelected = null;
        secondCardSelected = null;
        firstLocalSeat = -1;
        firstCardIndex = -1;

        // Let the current player pick ANY two cards on the table
        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p = gpm.players[seat];
            if (p?.cardsPanel?.cards == null) continue;

            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
            {
                var handCard = p.cardsPanel.cards[i];
                if (handCard == null) continue;

                handCard.ShowGlow(true);
                handCard.IsClickable = true;

                int capturedLocalSeat = seat;
                int capturedCardIndex = i;

                handCard.onClick = _ => OnHandCardSelected(handCard, capturedLocalSeat, capturedCardIndex);
            }
        }

        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    private void OnHandCardSelected(Card clickedCard, int localSeat, int cardIndex)
    {
        if (!isQueenSwapPhase || clickedCard == null) return;

        // Deselection
        if (clickedCard == firstCardSelected)
        {
            clickedCard.ShowGlow(true);
            firstCardSelected = null;
            firstLocalSeat = -1;
            firstCardIndex = -1;
            return;
        }

        if (firstCardSelected == null)
        {
            firstCardSelected = clickedCard;
            firstLocalSeat = localSeat;
            firstCardIndex = cardIndex;
            clickedCard.ShowGlow(false);
            return;
        }

        if (secondCardSelected == null)
        {
            if (firstLocalSeat == localSeat && firstCardIndex == cardIndex) return;

            secondCardSelected = clickedCard;
            secondCardSelected.ShowGlow(false);

            EndQueenSelectionPhase();

            int secondLocalSeat = localSeat;
            int secondCardIndex = cardIndex;

            int firstGlobalSeat = gpm.GetGlobalIndexFromLocal(firstLocalSeat);
            int secondGlobalSeat = gpm.GetGlobalIndexFromLocal(secondLocalSeat);

            RequestQueenSwapServerRpc(firstGlobalSeat, firstCardIndex, secondGlobalSeat, secondCardIndex);
        }
    }

    private void EndQueenSelectionPhase()
    {
        isQueenSwapPhase = false;

        if (gpm == null || gpm.players == null) return;
        foreach (var player in gpm.players)
        {
            if (player?.cardsPanel?.cards == null) continue;
            foreach (var card in player.cardsPanel.cards)
            {
                if (card == null) continue;
                card.ShowGlow(false);
                card.IsClickable = false;
                card.onClick = null;
            }
        }
    }

    
    [ServerRpc(RequireOwnership = false)]
    private void RequestQueenSwapServerRpc(int playerAIndex, int cardAIndex, int playerBIndex, int cardBIndex, ServerRpcParams rpcParams = default)
    {
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        if (gpm.players == null || gpm.players.Count == 0) return;

        int localA = gpm.GetLocalIndexFromGlobal(playerAIndex);
        int localB = gpm.GetLocalIndexFromGlobal(playerBIndex);
        if (localA < 0 || localA >= gpm.players.Count) return;
        if (localB < 0 || localB >= gpm.players.Count) return;

        var pA = gpm.players[localA];
        var pB = gpm.players[localB];
        if (pA?.cardsPanel?.cards == null || pB?.cardsPanel?.cards == null) return;
        if (cardAIndex < 0 || cardAIndex >= pA.cardsPanel.cards.Count) return;
        if (cardBIndex < 0 || cardBIndex >= pB.cardsPanel.cards.Count) return;

        var cardA = pA.cardsPanel.cards[cardAIndex];
        var cardB = pB.cardsPanel.cards[cardBIndex];
        if (cardA == null || cardB == null) return;

        var dataA = new SerializableCard(cardA.Type, cardA.Value);
        var dataB = new SerializableCard(cardB.Type, cardB.Value);

        QueenSwapClientRpc(playerAIndex, cardAIndex, dataB, playerBIndex, cardBIndex, dataA);
    }
    
    [ClientRpc]
    private void QueenSwapClientRpc(int playerAIndex, int cardAIndex, SerializableCard newA,
                                    int playerBIndex, int cardBIndex, SerializableCard newB)
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;

        int localASeat = gpm.GetLocalIndexFromGlobal(playerAIndex);
        int localBSeat = gpm.GetLocalIndexFromGlobal(playerBIndex);
        if (localASeat < 0 || localASeat >= gpm.players.Count) return;
        if (localBSeat < 0 || localBSeat >= gpm.players.Count) return;

        var playerA = gpm.players[localASeat];
        var playerB = gpm.players[localBSeat];
        if (playerA?.cardsPanel?.cards == null || playerB?.cardsPanel?.cards == null) return;

        if (cardAIndex < 0 || cardAIndex >= playerA.cardsPanel.cards.Count) return;
        if (cardBIndex < 0 || cardBIndex >= playerB.cardsPanel.cards.Count) return;

        var panelA = playerA.cardsPanel;
        var panelB = playerB.cardsPanel;

        var cardA = panelA.cards[cardAIndex];
        var cardB = panelB.cards[cardBIndex];
        if (cardA == null || cardB == null) return;
        // Capture curse state BEFORE we modify anything
        bool aWasCursed = (cardA != null) && (cardA.IsCursed || (cardA.cursedOutline && cardA.cursedOutline.activeSelf));
        bool bWasCursed = (cardB != null) && (cardB.IsCursed || (cardB.cursedOutline && cardB.cursedOutline.activeSelf));


        panelA.cards[cardAIndex] = cardB;
        panelB.cards[cardBIndex] = cardA;

        var movedA = panelA.cards[cardAIndex];
        var movedB = panelB.cards[cardBIndex];

        // card that ends in A gets newA
        movedA.Type = newA.Type;
        movedA.Value = newA.Value;
        movedA.CalcPoint();
        movedA.IsOpen = false;
        movedA.UpdateCard();
        movedA.transform.SetParent(panelA.transform, true);

        // card that ends in B gets newB
        movedB.Type = newB.Type;
        movedB.Value = newB.Value;
        movedB.CalcPoint();
        movedB.IsOpen = false;
        movedB.UpdateCard();
        movedB.transform.SetParent(panelB.transform, true);

        if (movedA != null)
        {
            movedA.SetCursed(bWasCursed);
            // Hand cards should NOT show curse particles
            if (movedA.cursedOutline && movedA.cursedOutline.transform.childCount > 0)
                movedA.cursedOutline.transform.GetChild(0).gameObject.SetActive(false);
        }
        if (movedB != null)
        {
            movedB.SetCursed(aWasCursed);
            if (movedB.cursedOutline && movedB.cursedOutline.transform.childCount > 0)
                movedB.cursedOutline.transform.GetChild(0).gameObject.SetActive(false);
        }

        // Fix indices
        playerA.ResyncCardIndices();
        if (playerB != playerA) playerB.ResyncCardIndices();

        StartCoroutine(AnimateCardSwap(movedA, movedB, panelA, panelB));
    }

    private IEnumerator AnimateCardSwap(Card cardInA, Card cardInB, PlayerCards panelA, PlayerCards panelB)
    {
        if (cardInA == null || cardInB == null) yield break;

        panelA.autoUpdatePositions = false;
        panelB.autoUpdatePositions = false;

        Vector3 posA = cardInA.transform.position;
        Vector3 posB = cardInB.transform.position;

        float duration = 0.35f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (cardInA == null || cardInB == null) yield break;

            cardInA.transform.position = Vector3.Lerp(posA, posB, elapsed / duration);
            cardInB.transform.position = Vector3.Lerp(posB, posA, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cardInA == null || cardInB == null) yield break;

        cardInA.transform.position = posB;
        cardInB.transform.position = posA;

        if (gpm.draw_card_clip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.draw_card_clip, 0.9f);

        cardInA.transform.localRotation = Quaternion.identity;
        cardInA.transform.localScale = Vector3.one;
        cardInB.transform.localRotation = Quaternion.identity;
        cardInB.transform.localScale = Vector3.one;

        panelA.autoUpdatePositions = true;
        panelB.autoUpdatePositions = true;

        panelA.UpdatePos();
        panelB.UpdatePos();

        cardInA.FlashMarkedOutline();
        cardInB.FlashMarkedOutline();

        yield return new WaitForSeconds(2f);

        gpm.EndCurrentPowerAvatarFromServer();

        if (IsHost)
            gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0f, gpm.CurrentTurnSerial));
    }

    // bot handling
    public void StartBotQueenSwapPhase(ulong botClientId)
    {
        StartCoroutine(BotQueenSwapRoutine(botClientId));
    }

    private IEnumerator BotQueenSwapRoutine(ulong botClientId)
    {
        // thinking
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        if (gpm == null || gpm.players == null || gpm.players.Count == 0) yield break;

        // Build per-seat card lists
        var seatToCards = new List<int>[gpm.players.Count];
        var seatsWithCards = new List<int>();

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p = gpm.players[seat];
            if (p?.cardsPanel?.cards == null) continue;

            seatToCards[seat] = new List<int>();
            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
                if (p.cardsPanel.cards[i] != null)
                    seatToCards[seat].Add(i);

            if (seatToCards[seat].Count > 0)
                seatsWithCards.Add(seat);
        }

        // Need at least two different players
        if (seatsWithCards.Count < 2) yield break;

        int seatA = seatsWithCards[Random.Range(0, seatsWithCards.Count)];
        int seatB;
        do { seatB = seatsWithCards[Random.Range(0, seatsWithCards.Count)]; } while (seatB == seatA);

        int cardA = seatToCards[seatA][Random.Range(0, seatToCards[seatA].Count)];
        int cardB = seatToCards[seatB][Random.Range(0, seatToCards[seatB].Count)];

        int firstGlobalSeat = gpm.GetGlobalIndexFromLocal(seatA);
        int secondGlobalSeat = gpm.GetGlobalIndexFromLocal(seatB);

        RequestQueenSwapServerRpc(
            firstGlobalSeat, cardA,
            secondGlobalSeat, cardB,
            new ServerRpcParams { Receive = new ServerRpcReceiveParams { SenderClientId = botClientId } }
        );
    }

}
