using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;

public class King : NetworkBehaviour
{
    public static King Instance;
    private bool isKingPhase = false;

    private Card selectedCard = null;
    private int selectedLocalSeat = -1;
    private int selectedCardIndex = -1;
    private Vector3 lastKilledCardPos;
    private float lastKilledCardZRot;

    void Awake() => Instance = this;

    public void StartKingPhase()
    {
        if (GamePlayManager.instance.cards.Count == 0)
        {
            return;
        }
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
        KingKillCardServerRpc(globalSeat, cardIndex, card.transform.position, card.transform.rotation.eulerAngles.z, -1);
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
    private void KingKillCardServerRpc(
    int globalSeat, int cardIndex, Vector3 pos, float zRot,
    int actingBotLocalSeat,
    ServerRpcParams rpcParams = default)
    {
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();

        int killerGlobalSeat;
        ulong killerClientId;

        if (actingBotLocalSeat >= 0 && actingBotLocalSeat < GamePlayManager.instance.players.Count)
        {
            killerGlobalSeat = GamePlayManager.instance.GetGlobalIndexFromLocal(actingBotLocalSeat);
            killerClientId = MultiplayerManager.Instance.playerDataNetworkList[killerGlobalSeat].clientId;
        }
        else
        {
            killerClientId = rpcParams.Receive.SenderClientId;
            killerGlobalSeat = -1;
            var playerList = MultiplayerManager.Instance.playerDataNetworkList;
            for (int i = 0; i < playerList.Count; i++)
                if (playerList[i].clientId == killerClientId)
                    killerGlobalSeat = i;
        }


        bool isOpponent = (killerGlobalSeat != globalSeat);

        var targetPlayer = GamePlayManager.instance.players[GamePlayManager.instance.GetLocalIndexFromGlobal(globalSeat)];
        var killedCard = targetPlayer.cardsPanel.cards[cardIndex];
        bool killedWasFiend = (killedCard != null && killedCard.Value == CardValue.Fiend);

        KingKillCardClientRpc(globalSeat, cardIndex);

        KingRefillHandAfterDelay(globalSeat, cardIndex, pos, zRot, killerGlobalSeat, isOpponent, killedWasFiend, actingBotLocalSeat);
    }

    private async void KingRefillHandAfterDelay(
    int globalSeat, int cardIndex, Vector3 toPos, float toZRot,
    int killerGlobalSeat, bool isOpponent, bool killedWasFiend,
    int actingBotLocalSeat = -1)
    {
        GamePlayManager.instance.isKingRefillPhase = true;
        await System.Threading.Tasks.Task.Delay(1000);

        var gpm = GamePlayManager.instance;
        if (gpm.cards.Count == 0) return;

        SerializableCard topCard = gpm.cards[gpm.cards.Count - 1];
        gpm.cards.RemoveAt(gpm.cards.Count - 1);

        KingRefillHandClientRpc(globalSeat, cardIndex, topCard, gpm.cards.ToArray());

        GamePlayManager.instance.isKingRefillPhase = false;

        if (killedWasFiend && isOpponent)
        {
            ShowFiendRevengeClientRpc(globalSeat);
            await System.Threading.Tasks.Task.Delay(400);

            int killerLocalSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(killerGlobalSeat);
            ulong killerClientId = 0;
            if (killerGlobalSeat >= 0 && killerGlobalSeat < MultiplayerManager.Instance.playerDataNetworkList.Count)
                killerClientId = MultiplayerManager.Instance.playerDataNetworkList[killerGlobalSeat].clientId;


            if (killerLocalSeat >= 0 && killerLocalSeat < GamePlayManager.instance.players.Count)
            {
                if (IsBotClientId(killerClientId))
                {
                    JumbleBotHandClientRpc(killerLocalSeat);
                }
                else
                {
                    JumbleOwnHandClientRpc(killerGlobalSeat);


                }
            }
        }
        else
        {
            if (NetworkManager.Singleton.IsHost)
                GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));
        }
    }

    [ClientRpc]
    private void ShowFiendRevengeClientRpc(int victimGlobalSeat)
    {
        if (GamePlayManager.instance.fiendRevengeVoiceClip != null && GamePlayManager.instance._audioSource != null)
            GamePlayManager.instance._audioSource.PlayOneShot(GamePlayManager.instance.fiendRevengeVoiceClip, 0.95f);

        int victimLocalSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(victimGlobalSeat);
        var victimPlayer = GamePlayManager.instance.players[victimLocalSeat];
        victimPlayer.ShowMessage("Fiend's Revenge", true, 2f);
    }

    private bool IsBotClientId(ulong clientId) => clientId >= 9000;

    [ClientRpc]
    public void JumbleOwnHandClientRpc(int killerGlobalSeat)
    {
        int killerLocalSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(killerGlobalSeat);
        if (killerLocalSeat < 0 || killerLocalSeat >= GamePlayManager.instance.players.Count)
            return;

        var player = GamePlayManager.instance.players[killerLocalSeat];
        var hand = player.cardsPanel.cards;

        GamePlayManager.instance.FreezeTimerUI();
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }

        int cardCount = hand.Count;
        List<int> indices = new List<int>();
        for (int i = 0; i < cardCount; i++) indices.Add(i);
        for (int i = cardCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = indices[i]; indices[i] = indices[j]; indices[j] = temp;
        }

        player.StartCoroutine(Fiend.Instance.JumbleHandAnimation(player, hand, indices.ToArray(), 3.0f, true, () =>
        {
            if (GamePlayManager.instance.IsHost && NetworkManager.Singleton.IsServer)
                NotifyJumbleFinishedServerRpc();
        }));
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyJumbleFinishedServerRpc(ServerRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));
        }
    }

    [ClientRpc]
    private void KingRefillHandClientRpc(int globalSeat, int cardIndex, SerializableCard refillCard, SerializableCard[] newDeck)
    {
        int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(globalSeat);
        var player = GamePlayManager.instance.players[localSeat];

        Vector3 toPos = lastKilledCardPos;
        float toZRot = lastKilledCardZRot;

        var newCardObj = Instantiate(
            GamePlayManager.instance._cardPrefab,
            GamePlayManager.instance.cardDeckTransform.position,
            Quaternion.identity,
            player.cardsPanel.transform.parent);

        Card newCard = newCardObj.GetComponent<Card>();
        newCard.Type = refillCard.Type;
        newCard.Value = refillCard.Value;
        newCard.IsOpen = false;
        newCard.CalcPoint();

        GamePlayManager.instance.StartCoroutine(
            KingAnimateDeckToHandSlotAndInsert(player, cardIndex, newCard, toPos, toZRot, 0.3f)
        );

        if (GamePlayManager.instance.draw_card_clip != null && GamePlayManager.instance._audioSource != null)
            GamePlayManager.instance._audioSource.PlayOneShot(GamePlayManager.instance.draw_card_clip, 0.9f);

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

        player.AddCard(card, handIndex);
        player.cardsPanel.UpdatePos();

        Card justInserted = player.cardsPanel.cards[handIndex];
        if (justInserted != null)
            justInserted.FlashMarkedOutline();

        float flashDuration = 2.0f;
        yield return new WaitForSeconds(flashDuration);

        if (GamePlayManager.instance.IsHost && GamePlayManager.instance.cards.Count == 0)
        {
            GamePlayManager.instance.unoBtn.SetActive(false);
            GamePlayManager.instance.arrowObject.SetActive(false);
        }
    }

    [ClientRpc]
    private void KingKillCardClientRpc(int globalSeat, int cardIndex)
    {
        int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(globalSeat);
        var player = GamePlayManager.instance.players[localSeat];
        Card killed = player.cardsPanel.cards[cardIndex];

        lastKilledCardPos = killed.transform.position;
        lastKilledCardZRot = killed.transform.rotation.eulerAngles.z;

        var wasteObj = GameObject.Instantiate(
            GamePlayManager.instance._cardPrefab,
            lastKilledCardPos,
            Quaternion.Euler(0, 0, lastKilledCardZRot),
            GamePlayManager.instance.cardWastePile.transform.parent);

        wasteObj.Type = killed.Type;
        wasteObj.Value = killed.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();
        wasteObj.gameObject.AddComponent<WastePile>().Initialize(wasteObj);

        wasteObj.ShowKilledOutline(true);

        wasteObj.IsClickable = false;
        wasteObj.onClick = null;

        float randomRot = Random.Range(-50, 50f);
        GamePlayManager.instance.StartCoroutine(
            GamePlayManager.instance.AnimateCardMove(
                wasteObj, lastKilledCardPos, GamePlayManager.instance.cardWastePile.transform.position, 0.3f, randomRot
            ));

        player.RemoveCard(killed, false);
    }

    public void StartBotKingPhase(ulong botClientId)
    {
        StartCoroutine(BotKingPhaseRoutine(botClientId));
    }

    private IEnumerator BotKingPhaseRoutine(ulong botClientId)
    {
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var gpm = GamePlayManager.instance;
        var candidates = new System.Collections.Generic.List<(int seat, int cardIndex)>();

        int botSeat = -1;
        for (int i = 0; i < gpm.players.Count; i++)
        {
            var pd = MultiplayerManager.Instance.playerDataNetworkList[GamePlayManager.instance.GetGlobalIndexFromLocal(i)];
            if (pd.clientId == botClientId)
            {
                botSeat = i;
                break;
            }
        }

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            if (seat == botSeat) continue; // but skip self  

            var player = gpm.players[seat];
            for (int cardIdx = 0; cardIdx < player.cardsPanel.cards.Count; cardIdx++)
            {
                if (player.cardsPanel.cards[cardIdx] != null)
                {
                    candidates.Add((seat, cardIdx));
                }
            }
        }
        if (candidates.Count == 0) yield break;

        var choice = candidates[Random.Range(0, candidates.Count)];
        var card = gpm.players[choice.seat].cardsPanel.cards[choice.cardIndex];
        Vector3 pos = card.transform.position;
        float zRot = card.transform.rotation.eulerAngles.z;

        KingKillCardServerRpc(
            gpm.GetGlobalIndexFromLocal(choice.seat),
            choice.cardIndex,
            pos,
            zRot,
            botSeat,
            new ServerRpcParams
            {
                Receive = new ServerRpcReceiveParams { SenderClientId = botClientId }
            }
        );
    }

    [ClientRpc]
    private void JumbleBotHandClientRpc(int botLocalSeat)
    {
        GamePlayManager.instance.FreezeTimerUI();
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }

        if (botLocalSeat < 0 || botLocalSeat >= GamePlayManager.instance.players.Count) return;
        var player = GamePlayManager.instance.players[botLocalSeat];
        var hand = player.cardsPanel.cards;

        int cardCount = hand.Count;
        List<int> indices = new List<int>();
        for (int i = 0; i < cardCount; i++) indices.Add(i);
        for (int i = cardCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = indices[i]; indices[i] = indices[j]; indices[j] = temp;
        }

        player.StartCoroutine(Fiend.Instance.JumbleHandAnimation(player, hand, indices.ToArray(), 3.0f, true, () =>
        {
            if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsServer)
                GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));
        }));
    }


}
