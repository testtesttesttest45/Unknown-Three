using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class Queen : NetworkBehaviour
{
    public static Queen Instance;

    private Card firstCardSelected = null;
    private Card secondCardSelected = null;
    private bool isQueenSwapPhase = false;

    private int firstLocalSeat, firstCardIndex;

    void Awake() => Instance = this;

    public void StartQueenSwap()
    {
        isQueenSwapPhase = true;
        firstCardSelected = null;
        secondCardSelected = null;

        foreach (var player in GamePlayManager.instance.players)
        {
            for (int i = 0; i < player.cardsPanel.cards.Count; i++)
            {
                var handCard = player.cardsPanel.cards[i];
                handCard.ShowGlow(true);
                handCard.IsClickable = true;

                int capturedLocalSeat = GamePlayManager.instance.players.IndexOf(player);
                int capturedCardIndex = i;

                handCard.onClick = (clickedCard) =>
                {
                    OnHandCardSelected(clickedCard, capturedLocalSeat, capturedCardIndex);
                };
            }
        }
    }

    private void OnHandCardSelected(Card clickedCard, int localSeat, int cardIndex)
    {
        if (!isQueenSwapPhase) return;

        if (clickedCard == firstCardSelected)
        {
            clickedCard.ShowGlow(true);
            firstCardSelected = null;
            return;
        }

        if (firstCardSelected == null)
        {
            firstCardSelected = clickedCard;
            firstLocalSeat = localSeat;
            firstCardIndex = cardIndex;
            clickedCard.ShowGlow(false);
        }
        else if (secondCardSelected == null && clickedCard != firstCardSelected)
        {
            secondCardSelected = clickedCard;
            clickedCard.ShowGlow(false);

            EndQueenSelectionPhase();

            int secondLocalSeat = localSeat;
            int secondCardIndex = cardIndex;

            int firstGlobalSeat = GamePlayManager.instance.GetGlobalIndexFromLocal(firstLocalSeat);
            int secondGlobalSeat = GamePlayManager.instance.GetGlobalIndexFromLocal(secondLocalSeat);

            RequestQueenSwapServerRpc(firstGlobalSeat, firstCardIndex, secondGlobalSeat, secondCardIndex);
        }
    }

    private void EndQueenSelectionPhase()
    {
        isQueenSwapPhase = false;
        foreach (var player in GamePlayManager.instance.players)
        {
            foreach (var card in player.cardsPanel.cards)
            {
                card.ShowGlow(false);
                card.IsClickable = false;
                card.onClick = null;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestQueenSwapServerRpc(int playerAIndex, int cardAIndex, int playerBIndex, int cardBIndex, ServerRpcParams rpcParams = default)
    {
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();

        var players = GamePlayManager.instance.players;

        var cardA = players[playerAIndex].cardsPanel.cards[cardAIndex];
        var cardB = players[playerBIndex].cardsPanel.cards[cardBIndex];

        SerializableCard dataA = new SerializableCard(cardA.Type, cardA.Value);
        SerializableCard dataB = new SerializableCard(cardB.Type, cardB.Value);

        cardA.Type = dataB.Type;
        cardA.Value = dataB.Value;
        cardA.CalcPoint();

        cardB.Type = dataA.Type;
        cardB.Value = dataA.Value;
        cardB.CalcPoint();

        QueenSwapClientRpc(playerAIndex, cardAIndex, dataB, playerBIndex, cardBIndex, dataA);

    }

    [ClientRpc]
    private void QueenSwapClientRpc(int playerAIndex, int cardAIndex, SerializableCard newA, int playerBIndex, int cardBIndex, SerializableCard newB)
    {
        var players = GamePlayManager.instance.players;

        int localASeat = GamePlayManager.instance.GetLocalIndexFromGlobal(playerAIndex);
        int localBSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(playerBIndex);

        var playerA = players[localASeat];
        var playerB = players[localBSeat];

        var panelA = playerA.cardsPanel;
        var panelB = playerB.cardsPanel;

        var tempCard = panelA.cards[cardAIndex];
        panelA.cards[cardAIndex] = panelB.cards[cardBIndex];
        panelB.cards[cardBIndex] = tempCard;

        var cardA = panelA.cards[cardAIndex];
        var cardB = panelB.cards[cardBIndex];

        cardA.Type = newA.Type;
        cardA.Value = newA.Value;
        cardA.CalcPoint();
        cardA.IsOpen = false;
        cardA.UpdateCard();

        cardB.Type = newB.Type;
        cardB.Value = newB.Value;
        cardB.CalcPoint();
        cardB.IsOpen = false;
        cardB.UpdateCard();

        cardA.transform.SetParent(panelA.transform, true);
        cardB.transform.SetParent(panelB.transform, true);

        playerA.ResyncCardIndices();
        if (playerB != playerA)
            playerB.ResyncCardIndices();

        StartCoroutine(AnimateCardSwap(cardA, cardB, panelA, panelB));
    }

    private IEnumerator AnimateCardSwap(Card cardA, Card cardB, PlayerCards panelA, PlayerCards panelB)
    {
        panelA.autoUpdatePositions = false;
        panelB.autoUpdatePositions = false;

        Vector3 posA = cardA.transform.position;
        Vector3 posB = cardB.transform.position;

        float duration = 0.35f;
        float elapsed = 0f;

        // Animate the swap
        while (elapsed < duration)
        {
            cardA.transform.position = Vector3.Lerp(posA, posB, elapsed / duration);
            cardB.transform.position = Vector3.Lerp(posB, posA, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        cardA.transform.position = posB;
        cardB.transform.position = posA;

        cardA.transform.localRotation = Quaternion.identity;
        cardA.transform.localScale = Vector3.one;
        cardB.transform.localRotation = Quaternion.identity;
        cardB.transform.localScale = Vector3.one;

        panelA.autoUpdatePositions = true;
        panelB.autoUpdatePositions = true;

        panelA.UpdatePos();
        panelB.UpdatePos();

        // --- NEW: Flash both cards ---
        cardA.FlashMarkedOutline();
        cardB.FlashMarkedOutline();

        // Wait for flash to finish (set to your effect's duration, e.g. 2f)
        yield return new WaitForSeconds(2f);

        // --- Host advances turn after flash ---
        if (NetworkManager.Singleton.IsHost)
            GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0f));
    }

}
