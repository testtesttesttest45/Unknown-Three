using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class King : NetworkBehaviour
{
    public static King Instance;

    private bool isKingPhase = false;
    private Card selectedCard = null;
    private int selectedLocalSeat = -1;
    private int selectedCardIndex = -1;

    // cached per-client so the refill anim can land exactly where the killed card was
    private Vector3 lastKilledCardPos;
    private float lastKilledCardZRot;

    private GamePlayManager gpm => GamePlayManager.instance;

    void Awake() => Instance = this;

    public void StartKingPhase()
    {
        if (gpm == null || gpm.cards == null || gpm.cards.Count == 0) return;

        isKingPhase = true;
        selectedCard = null;
        selectedLocalSeat = -1;
        selectedCardIndex = -1;

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var player = gpm.players[seat];
            for (int i = 0; i < player.cardsPanel.cards.Count; i++)
            {
                var handCard = player.cardsPanel.cards[i];
                if (handCard == null) continue;

                handCard.ShowGlow(true);
                handCard.IsClickable = true;

                int capturedSeat = seat;
                int capturedCardIndex = i;
                handCard.onClick = (clickedCard) =>
                {
                    OnHandCardSelected(clickedCard, capturedSeat, capturedCardIndex);
                };
            }
        }

        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    private void OnHandCardSelected(Card card, int localSeat, int cardIndex)
    {
        if (!isKingPhase || card == null) return;
        isKingPhase = false;

        selectedCard = card;
        selectedLocalSeat = localSeat;
        selectedCardIndex = cardIndex;

        EndKingPhase();

        // Freeze timer
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        int globalSeat = gpm.GetGlobalIndexFromLocal(localSeat);
        KingKillCardServerRpc(globalSeat, cardIndex, card.transform.position, card.transform.rotation.eulerAngles.z, -1);
    }

    public void EndKingPhase()
    {
        isKingPhase = false;
        foreach (var p in gpm.players)
            foreach (var c in p.cardsPanel.cards)
            {
                if (c == null) continue;
                c.ShowGlow(false);
                c.IsClickable = false;
                c.onClick = null;
            }
    }

    [ServerRpc(RequireOwnership = false)]
    private void KingKillCardServerRpc(
        int globalSeat,
        int cardIndex,
        Vector3 pos,
        float zRot,
        int actingBotLocalSeat,
        ServerRpcParams rpcParams = default)
    {
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        // Determine killer (human or bot) in GLOBAL seat space
        int killerGlobalSeat;
        ulong killerClientId;

        if (actingBotLocalSeat >= 0 && actingBotLocalSeat < gpm.players.Count)
        {
            killerGlobalSeat = gpm.GetGlobalIndexFromLocal(actingBotLocalSeat);
            killerClientId = MultiplayerManager.Instance.playerDataNetworkList[killerGlobalSeat].clientId;
        }
        else
        {
            killerClientId = rpcParams.Receive.SenderClientId;
            killerGlobalSeat = -1;
            var plist = MultiplayerManager.Instance.playerDataNetworkList;
            for (int i = 0; i < plist.Count; i++)
                if (plist[i].clientId == killerClientId) { killerGlobalSeat = i; break; }
        }

        bool isOpponent = (killerGlobalSeat != globalSeat);

        // Inspect target card to know if special revenge should trigger
        int targetLocalSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
        if (targetLocalSeat < 0 || targetLocalSeat >= gpm.players.Count) return;

        var targetPlayer = gpm.players[targetLocalSeat];
        if (cardIndex < 0 || cardIndex >= targetPlayer.cardsPanel.cards.Count) return;

        var killedCard = targetPlayer.cardsPanel.cards[cardIndex];
        bool killedWasFiend = (killedCard != null && killedCard.Value == CardValue.Fiend);
        bool killedWasGoldenJack = (killedCard != null && killedCard.Value == CardValue.GoldenJack);

        KingKillCardClientRpc(globalSeat, cardIndex);

        // refill + possible revenge on the server timeline
        KingRefillHandAfterDelay(
            globalSeat, cardIndex, pos, zRot,
            killerGlobalSeat, isOpponent, killedWasFiend, killedWasGoldenJack, actingBotLocalSeat, killerClientId
        );
    }

    private async void KingRefillHandAfterDelay(
        int globalSeat, int cardIndex, Vector3 toPos, float toZRot,
        int killerGlobalSeat, bool isOpponent, bool killedWasFiend, bool killedWasGoldenJack,
        int actingBotLocalSeat = -1, ulong killerClientId = 0)
    {
        gpm.isKingRefillPhase = true;
        await System.Threading.Tasks.Task.Delay(1000);

        // Draw top card if deck not empty
        if (gpm.cards == null || gpm.cards.Count == 0)
        {
            gpm.isKingRefillPhase = false;
            gpm.EndCurrentPowerAvatarFromServer();
            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
            return;
        }

        SerializableCard topCard = gpm.cards[gpm.cards.Count - 1];
        gpm.cards.RemoveAt(gpm.cards.Count - 1);

        KingRefillHandClientRpc(globalSeat, cardIndex, topCard, gpm.cards.ToArray());

        gpm.isKingRefillPhase = false;

        // Revenge effects
        if (killedWasFiend && isOpponent)
        {
            float jumbleSeconds = 3.5f;

            gpm.BeginTemporaryAvatarFromServer(globalSeat, CardValue.Fiend, jumbleSeconds);
            ShowFiendRevengeClientRpc(globalSeat);
            await System.Threading.Tasks.Task.Delay(400);

            int killerLocalSeat = gpm.GetLocalIndexFromGlobal(killerGlobalSeat);
            if (killerGlobalSeat >= 0 && killerGlobalSeat < MultiplayerManager.Instance.playerDataNetworkList.Count)
                killerClientId = MultiplayerManager.Instance.playerDataNetworkList[killerGlobalSeat].clientId;

            if (killerLocalSeat >= 0 && killerLocalSeat < gpm.players.Count)
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
        else if (killedWasGoldenJack && isOpponent)
        {
            float revealSeconds = 3.0f;

            gpm.BeginTemporaryAvatarFromServer(globalSeat, CardValue.GoldenJack, revealSeconds);
            ShowGoldenJackRevengeClientRpc(killerGlobalSeat, globalSeat, killerClientId);

            await System.Threading.Tasks.Task.Delay((int)(revealSeconds * 1000));

            if (NetworkManager.Singleton.IsHost)
                gpm.EndAvatarForSeatFromServer(killerGlobalSeat);

            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
        }
        else
        {
            gpm.EndCurrentPowerAvatarFromServer();
            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
        }
    }

    [ClientRpc]
    private void KingKillCardClientRpc(int globalSeat, int cardIndex)
    {
        int localSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var player = gpm.players[localSeat];
        if (cardIndex < 0 || cardIndex >= player.cardsPanel.cards.Count) return;

        Card killed = player.cardsPanel.cards[cardIndex];
        if (killed == null) return;

        // Cache position/rot to land the refill exactly there later
        lastKilledCardPos = killed.transform.position;
        lastKilledCardZRot = killed.transform.rotation.eulerAngles.z;

        // Spawn a copy to the waste pile with a little throw animation
        var wasteObj = Object.Instantiate(
            gpm._cardPrefab,
            lastKilledCardPos,
            Quaternion.Euler(0, 0, lastKilledCardZRot),
            gpm.cardWastePile.transform.parent
        );

        wasteObj.Type = killed.Type;
        wasteObj.Value = killed.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();
        wasteObj.gameObject.AddComponent<WastePile>().Initialize(wasteObj);

        if (killed.Value != CardValue.GoldenJack)
        {
            wasteObj.ShowKilledOutline(true);
            wasteObj.IsClickable = false;
            wasteObj.onClick = null;
        }
        else
        {
            wasteObj.ShowKilledOutline(false);
        }

        float randomRot = Random.Range(-50, 50f);
        gpm.StartCoroutine(gpm.AnimateCardMove(
            wasteObj, lastKilledCardPos, gpm.cardWastePile.transform.position, 0.3f, randomRot
        ));

        player.RemoveCard(killed, false);
    }

    [ClientRpc]
    private void KingRefillHandClientRpc(int globalSeat, int cardIndex, SerializableCard refillCard, SerializableCard[] newDeck)
    {
        int localSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var player = gpm.players[localSeat];

        var newCardObj = Instantiate(
            gpm._cardPrefab,
            gpm.cardDeckTransform.position,
            Quaternion.identity,
            player.cardsPanel.transform.parent
        );

        Card newCard = newCardObj.GetComponent<Card>();
        newCard.Type = refillCard.Type;
        newCard.Value = refillCard.Value;
        newCard.IsOpen = false;
        newCard.CalcPoint();

        gpm.StartCoroutine(KingAnimateDeckToHandSlotAndInsert(
            player, cardIndex, newCard, lastKilledCardPos, lastKilledCardZRot, 0.3f
        ));

        if (gpm.draw_card_clip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.draw_card_clip, 0.9f);

        gpm.UpdateDeckVisualClientRpc(newDeck);
    }

    private static IEnumerator KingAnimateDeckToHandSlotAndInsert(
        Player2 player, int handIndex, Card card, Vector3 toPos, float toZRot, float duration)
    {
        Vector3 from = card.transform.position;
        Quaternion fromRot = card.transform.rotation;
        Quaternion toRot = Quaternion.Euler(0, 0, toZRot);

        float elapsed = 0;
        while (elapsed < duration)
        {
            if (card == null) yield break;
            card.transform.position = Vector3.Lerp(from, toPos, elapsed / duration);
            card.transform.rotation = Quaternion.Slerp(fromRot, toRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (card == null) yield break;

        card.transform.position = toPos;
        card.transform.rotation = toRot;

        player.AddCard(card, handIndex);
        player.cardsPanel.UpdatePos();

        Card justInserted = player.cardsPanel.cards[handIndex];
        if (justInserted != null) justInserted.FlashMarkedOutline();

        yield return new WaitForSeconds(2.0f);

        if (GamePlayManager.instance.IsHost && GamePlayManager.instance.cards.Count == 0)
        {
            GamePlayManager.instance.unoBtn.SetActive(false);
            GamePlayManager.instance.arrowObject.SetActive(false);
        }
    }

    [ClientRpc]
    private void ShowFiendRevengeClientRpc(int victimGlobalSeat)
    {
        if (gpm.fiendRevengeVoiceClip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.fiendRevengeVoiceClip, 0.95f);

        int victimLocalSeat = gpm.GetLocalIndexFromGlobal(victimGlobalSeat);
        if (victimLocalSeat < 0 || victimLocalSeat >= gpm.players.Count) return;

        var victimPlayer = gpm.players[victimLocalSeat];
        victimPlayer.ShowMessage("Fiend's Revenge", true, 2f);
    }

    [ClientRpc]
    private void ShowGoldenJackRevengeClientRpc(int kingUserGlobalSeat, int goldenJackVictimGlobalSeat, ulong killerClientId)
    {
        if (gpm.goldenJackRevengeVoiceClip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.goldenJackRevengeVoiceClip, 0.95f);

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        int kingUserLocalSeat = gpm.GetLocalIndexFromGlobal(kingUserGlobalSeat);
        int victimLocalSeat = gpm.GetLocalIndexFromGlobal(goldenJackVictimGlobalSeat);

        if (victimLocalSeat >= 0 && victimLocalSeat < gpm.players.Count)
            gpm.players[victimLocalSeat].ShowMessage("Golden Jack's Revenge!", true, 2.5f);

        if (kingUserLocalSeat < 0 || kingUserLocalSeat >= gpm.players.Count) return;
        var kingPlayer = gpm.players[kingUserLocalSeat];

        bool isKiller = (myClientId == killerClientId);

        for (int i = 0; i < kingPlayer.cardsPanel.cards.Count; i++)
        {
            var c = kingPlayer.cardsPanel.cards[i];
            if (c == null) continue;

            if (isKiller)
            {
                // Killer sees only a “peeked” flash (no reveal)
                c.FlashEyeOutline();
            }
            else
            {
                // Others see reveal + flash
                c.IsOpen = true;
                c.FlashMarkedOutline();
                kingPlayer.StartCoroutine(HideCardAfterDelay(c, 3.0f));
            }
        }
    }

    private IEnumerator HideCardAfterDelay(Card card, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (card != null) card.IsOpen = false;
    }

    private bool IsBotClientId(ulong clientId) => clientId >= 9000;

    [ClientRpc]
    public void JumbleOwnHandClientRpc(int killerGlobalSeat)
    {
        int killerLocalSeat = gpm.GetLocalIndexFromGlobal(killerGlobalSeat);
        if (killerLocalSeat < 0 || killerLocalSeat >= gpm.players.Count) return;

        var player = gpm.players[killerLocalSeat];
        var hand = player.cardsPanel.cards;

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        int cardCount = hand.Count;
        if (cardCount <= 1) { if (IsHost) gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f)); return; }

        var indices = BuildRandomPermutation(cardCount);

        player.StartCoroutine(
            (Fiend.Instance != null ? Fiend.Instance.JumbleHandAnimation(
                player, hand, indices.ToArray(), 3.0f, true, () =>
                {
                    if (NetworkManager.Singleton.IsHost)
                        gpm.EndAvatarForSeatFromServer(killerGlobalSeat);

                    if (gpm.IsHost && NetworkManager.Singleton.IsServer)
                        NotifyJumbleFinishedServerRpc();
                })
            : FallbackJumble(player, hand, indices))
        );
    }

    private IEnumerator FallbackJumble(Player2 player, List<Card> hand, List<int> indices)
    {
        // instant reorder as a failsafe if Fiend is missing
        var panel = player.cardsPanel;
        var copy = new List<Card>(hand);
        panel.cards.Clear();
        for (int i = 0; i < indices.Count; i++)
            panel.cards.Add(copy[indices[i]]);
        panel.UpdatePos();

        if (NetworkManager.Singleton.IsHost)
            GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));

        yield break;
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyJumbleFinishedServerRpc(ServerRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.IsServer)
            gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
    }

    [ClientRpc]
    private void JumbleBotHandClientRpc(int botLocalSeat)
    {
        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        if (botLocalSeat < 0 || botLocalSeat >= gpm.players.Count) return;

        var player = gpm.players[botLocalSeat];
        var hand = player.cardsPanel.cards;
        int cardCount = hand.Count;
        if (cardCount <= 1)
        {
            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
            return;
        }

        var indices = BuildRandomPermutation(cardCount);
        int killerGlobalSeat = gpm.GetGlobalIndexFromLocal(botLocalSeat);

        player.StartCoroutine(
            (Fiend.Instance != null ? Fiend.Instance.JumbleHandAnimation(
                player, hand, indices.ToArray(), 3.0f, true, () =>
                {
                    if (NetworkManager.Singleton.IsHost)
                        gpm.EndAvatarForSeatFromServer(killerGlobalSeat);

                    if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsServer)
                        gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
                })
            : FallbackJumble(player, hand, indices))
        );
    }

    public void StartBotKingPhase(ulong botClientId)
    {
        StartCoroutine(BotKingPhaseRoutine(botClientId));
    }

    private IEnumerator BotKingPhaseRoutine(ulong botClientId)
    {
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var candidates = new List<(int seat, int cardIndex)>();
        int botLocalSeat = -1;

        // find bot's local seat
        for (int i = 0; i < gpm.players.Count; i++)
        {
            var pd = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(i)];
            if (pd.clientId == botClientId) { botLocalSeat = i; break; }
        }

        // choose any opponent card
        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            if (seat == botLocalSeat) continue;
            var player = gpm.players[seat];
            for (int ci = 0; ci < player.cardsPanel.cards.Count; ci++)
                if (player.cardsPanel.cards[ci] != null)
                    candidates.Add((seat, ci));
        }

        if (candidates.Count == 0) yield break;

        var choice = candidates[Random.Range(0, candidates.Count)];
        var card = gpm.players[choice.seat].cardsPanel.cards[choice.cardIndex];
        if (card == null) yield break;

        Vector3 pos = card.transform.position;
        float zRot = card.transform.rotation.eulerAngles.z;

        KingKillCardServerRpc(
            gpm.GetGlobalIndexFromLocal(choice.seat),
            choice.cardIndex,
            pos,
            zRot,
            botLocalSeat,
            new ServerRpcParams { Receive = new ServerRpcReceiveParams { SenderClientId = botClientId } }
        );
    }

    private static List<int> BuildRandomPermutation(int n)
    {
        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }
        return idx;
    }
}
