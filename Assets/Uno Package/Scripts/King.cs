using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class King : NetworkBehaviour
{
    public static King Instance;
    private bool isKingPhase = false;

    private Card selectedCard = null;
    private int selectedLocalSeat = -1;
    private int selectedCardIndex = -1;

    void Awake() => Instance = this;

    public void StartKingPhase()
    {
        isKingPhase = true;
        selectedCard = null;
        selectedLocalSeat = -1;
        selectedCardIndex = -1;

        // Glow all hand cards for all players on local screen (only local can click)
        for (int seat = 0; seat < GamePlayManager.instance.players.Count; seat++)
        {
            var player = GamePlayManager.instance.players[seat];
            for (int i = 0; i < player.cardsPanel.cards.Count; i++)
            {
                var handCard = player.cardsPanel.cards[i];
                handCard.ShowGlow(true);
                handCard.IsClickable = true;

                int capturedSeat = seat;
                int capturedCardIndex = i;
                handCard.onClick = (clickedCard) => {
                    OnHandCardSelected(clickedCard, capturedSeat, capturedCardIndex);
                };
            }
        }

        if (GamePlayManager.instance.players[0].isUserPlayer)
        {
            GamePlayManager.instance.players[0].SetTimerVisible(true);
            GamePlayManager.instance.players[0].UpdateTurnTimerUI(
                GamePlayManager.instance.turnTimerDuration,
                GamePlayManager.instance.turnTimerDuration
            );
        }
    }

    private void OnHandCardSelected(Card card, int localSeat, int cardIndex)
    {
        if (!isKingPhase) return;
        isKingPhase = false;

        selectedCard = card;
        selectedLocalSeat = localSeat;
        selectedCardIndex = cardIndex;

        EndKingPhase();

        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();

        int globalSeat = GamePlayManager.instance.GetGlobalIndexFromLocal(localSeat);
        KingKillCardServerRpc(globalSeat, cardIndex, card.transform.position, card.transform.rotation.eulerAngles.z);
    }

    public void EndKingPhase()
    {
        isKingPhase = false;
        foreach (var player in GamePlayManager.instance.players)
            foreach (var c in player.cardsPanel.cards)
            {
                c.ShowGlow(false);
                c.IsClickable = false;
                c.onClick = null;
            }
    }


    [ServerRpc(RequireOwnership = false)]
    private void KingKillCardServerRpc(int globalSeat, int cardIndex, Vector3 pos, float zRot, ServerRpcParams rpcParams = default)
    {
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();
        ulong killerClientId = rpcParams.Receive.SenderClientId;

        KingKillCardClientRpc(globalSeat, cardIndex);

        KingRefillHandAfterDelay(globalSeat, cardIndex, pos, zRot, killerClientId);
    }

    private async void KingRefillHandAfterDelay(int globalSeat, int cardIndex, Vector3 toPos, float toZRot, ulong killerClientId)
    {
        GamePlayManager.instance.isKingRefillPhase = true;
        await System.Threading.Tasks.Task.Delay(1000);

        var gpm = GamePlayManager.instance;
        if (gpm.cards.Count == 0) return;

        SerializableCard topCard = gpm.cards[gpm.cards.Count - 1];
        gpm.cards.RemoveAt(gpm.cards.Count - 1);

        KingRefillHandClientRpc(globalSeat, cardIndex, topCard, gpm.cards.ToArray()); 
        GamePlayManager.instance.isKingRefillPhase = false;
        if (IsHost)
            GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.3f));

    }

    [ClientRpc]
    private void KingRefillHandClientRpc(int globalSeat, int cardIndex, SerializableCard refillCard, SerializableCard[] newDeck)
    {
        int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(globalSeat);
        var player = GamePlayManager.instance.players[localSeat];

        // Get hand slot position for this seat/index
        Vector3 toPos;
        float toZRot;

        // If using fixed slots:
        if (player.cardsPanel.cards.Count > cardIndex && player.cardsPanel.cards[cardIndex] == null)
        {
            toPos = player.cardsPanel.transform.GetChild(cardIndex).position;
            toZRot = player.cardsPanel.transform.GetChild(cardIndex).rotation.eulerAngles.z;
        }
        else
        {
            // fallback to hand anchor, or default
            toPos = player.cardsPanel.transform.position;
            toZRot = 0f;
        }

        var newCardObj = GameObject.Instantiate(
            GamePlayManager.instance._cardPrefab,
            GamePlayManager.instance.cardDeckTransform.position,
            Quaternion.identity,
            player.cardsPanel.transform.parent);
        Card newCard = newCardObj.GetComponent<Card>();
        newCard.Type = refillCard.Type;
        newCard.Value = refillCard.Value;
        newCard.IsOpen = false;
        newCard.CalcPoint();

        // Animate deck to hand slot
        GamePlayManager.instance.StartCoroutine(
            KingAnimateDeckToHandSlotAndInsert(player, cardIndex, newCard, toPos, toZRot, 0.3f)
        );

        GamePlayManager.instance.UpdateDeckVisualClientRpc(newDeck);
    }

    private static IEnumerator KingAnimateDeckToHandSlotAndInsert(Player2 player, int handIndex, Card card, Vector3 toPos, float toZRot, float duration)
    {
        Vector3 from = card.transform.position;
        Quaternion fromRot = card.transform.rotation;
        Quaternion toRot = Quaternion.Euler(0, 0, toZRot);

        float elapsed = 0;
        while (elapsed < duration)
        {
            card.transform.position = Vector3.Lerp(from, toPos, elapsed / duration);
            card.transform.rotation = Quaternion.Slerp(fromRot, toRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        card.transform.position = toPos;
        card.transform.rotation = toRot;

        // Insert into hand
        player.AddCard(card, handIndex);
        player.cardsPanel.UpdatePos();
    }

    [ClientRpc]
    private void KingKillCardClientRpc(int globalSeat, int cardIndex)
    {
        int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(globalSeat);
        var player = GamePlayManager.instance.players[localSeat];
        Card killed = player.cardsPanel.cards[cardIndex];

        Vector3 fromPos = killed.transform.position;
        float fromZRot = killed.transform.rotation.eulerAngles.z;

        // Animate card to waste
        var wasteObj = GameObject.Instantiate(
            GamePlayManager.instance._cardPrefab,
            fromPos,
            Quaternion.Euler(0, 0, fromZRot),
            GamePlayManager.instance.cardWastePile.transform.parent);

        wasteObj.Type = killed.Type;
        wasteObj.Value = killed.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();
        wasteObj.gameObject.AddComponent<WastePile>().Initialize(wasteObj);

        wasteObj.ShowKilledOutline(true);

        // Prevent interaction with this waste card
        wasteObj.IsClickable = false;
        wasteObj.onClick = null;

        float randomRot = Random.Range(-50, 50f);
        GamePlayManager.instance.StartCoroutine(
            GamePlayManager.instance.AnimateCardMove(
                wasteObj, fromPos, GamePlayManager.instance.cardWastePile.transform.position, 0.3f, randomRot
            ));


        player.RemoveCard(killed, false);
    }




}
