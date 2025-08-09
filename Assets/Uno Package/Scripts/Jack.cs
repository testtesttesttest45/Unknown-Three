using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Jack : NetworkBehaviour
{
    public static Jack Instance;

    public bool isJackRevealPhase = false;
    public bool isGoldenJackRevealPhase = false;
    private ulong currentJackUserClientId = ulong.MaxValue;
    private GamePlayManager gpm => GamePlayManager.instance;
    private bool currentJackUserIsBot = false; 
    private bool botGoldenJackRevealActive = false;

    void Awake() => Instance = this;

    void OnJackCardDiscardedByMe()
    {
        if (!gpm.IsMyTurn() || !gpm.players[0].isUserPlayer)
        {
            return;
        }
        isJackRevealPhase = true;
        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p =  gpm.players[seat];
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
                    gpm.DisableAllHandCardGlowAllPlayers();
                    int globalPlayerIndex = gpm.GetGlobalIndexFromLocal(s);
                    RequestRevealHandCardServerRpc(globalPlayerIndex, idx);
                    gpm.UpdateDeckClickability();
                };

            }
        }
        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    [ClientRpc]
    public void StartJackRevealLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId)
            return;

        OnJackCardDiscardedByMe();
    }

    IEnumerator HideCardAfterDelay(Card card, float delay)
    {
        yield return new WaitForSeconds(delay);
        card.IsOpen = false;
    }

    [ClientRpc]
    void RevealHandCardClientRpc(
     int playerIndex, int cardIndex, CardType type, CardValue value, ulong jackUserClientId,
     ClientRpcParams rpcParams = default)
    {
        if (gpm.IsBotClientId(jackUserClientId))
        {
            return;
        }

        if (gpm.IsBotClientId(NetworkManager.Singleton.LocalClientId))
            return;

        if (NetworkManager.Singleton.LocalClientId != jackUserClientId)
            return;
        if (gpm.IsBotClientId(jackUserClientId))
            return;

        var p = gpm.players[gpm.GetLocalIndexFromGlobal(playerIndex)];
        var card = p.cardsPanel.cards[cardIndex];
        card.Type = type;
        card.Value = value;
        card.IsOpen = true;
        card.FlashMarkedOutline();
        StartCoroutine(HideCardAfterDelay(card, 2f));
    }

    [ClientRpc]
    void PlayJackZeroBonusSoundClientRpc(ClientRpcParams rpcParams = default)
    {
        if (gpm._audioSource != null && gpm.jackSpecialVoiceClip != null)
            gpm._audioSource.PlayOneShot(gpm.jackSpecialVoiceClip);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealHandCardServerRpc(int playerIndex, int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (gpm.turnTimeoutCoroutine != null)
        {
            StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        var handCard = gpm.players[playerIndex].cardsPanel.cards[cardIndex];
        ulong jackUserClientId = serverRpcParams.Receive.SenderClientId;
        if (gpm.IsBotClientId(jackUserClientId))
        {
            Debug.LogWarning("RequestRevealHandCardServerRpc called for bot—this should never happen! Please check SimulateBotJackReveal.");
            FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, true);
            OnJackRevealDoneServerRpc();
            return;
        }

        // reveal for Jack user only
        RevealHandCardClientRpc(
            playerIndex, cardIndex, handCard.Type, handCard.Value, jackUserClientId,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { jackUserClientId }
                }
            }
        );
        // reveal for others: flash
        FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, false);

        // delay before exposing the Zero
        StartCoroutine(ExposeZeroAfterDelayCoroutine(jackUserClientId));
    }


    private IEnumerator ExposeZeroAfterDelayCoroutine(ulong jackUserClientId)
    {
        yield return new WaitForSeconds(1.0f);

        // Find the player (not Jack user) who has the Zero
        int zeroHolderPlayerIndex = -1;
        int zeroHolderCardIndex = -1;

        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(pi)].clientId;
            if (pClientId == jackUserClientId) continue; // skip Jack user

            for (int ci = 0; ci < gpm.players[pi].cardsPanel.cards.Count; ci++)
            {
                var card = gpm.players[pi].cardsPanel.cards[ci];
                if (card == null) continue;
                if (card.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    zeroHolderCardIndex = ci;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            // Play sound for all real clients (not bots), including Jack user
            var allRealClientIds = new List<ulong>();
            foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
            {
                if (!gpm.IsBotClientId(pd.clientId)) // Only real clients
                    allRealClientIds.Add(pd.clientId);
            }

            PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = allRealClientIds }
            });

            ShowZeroHolderTimerEffectClientRpc(gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex));

            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            OnJackRevealDoneServerRpc();
        }
    }

    [ClientRpc]
    void ShowZeroHolderTimerEffectClientRpc(int globalPlayerIndex)
    {
        int localIndex = gpm.GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex < 0 || localIndex >= gpm.players.Count) return;
        var player = gpm.players[localIndex];

        // Turn ON the timer object and set its color to red
        if (player.timerOjbect != null)
        {
            player.timerOjbect.SetActive(true);
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = Color.red;

            StartCoroutine(RestoreTimerEffectAfterDelay(player, 3.0f));
        }
    }

    private IEnumerator RestoreTimerEffectAfterDelay(Player2 player, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (player.timerOjbect != null)
        {
            // Restore to gold color #927620
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = new Color(0.572f, 0.463f, 0.125f, 1f);

            player.timerOjbect.SetActive(false);
        }
    }

    private IEnumerator JackZeroBonusEndTurnCoroutine()
    {
        yield return new WaitForSeconds(3.0f); // Wait for bonus sound
        OnJackRevealDoneServerRpc();
    }

    [ClientRpc]
    void FlashMarkedOutlineClientRpc(int playerIndex, int cardIndex, ulong jackUserClientId, bool jackUserIsBot, ClientRpcParams rpcParams = default)
    {
        // If Jack user is a bot, everyone sees only the flash.
        if (!jackUserIsBot && NetworkManager.Singleton.LocalClientId == jackUserClientId)
            return; // Jack user skips flash—they see the value already

        var p = gpm.players[gpm.GetLocalIndexFromGlobal(playerIndex)];
        var card = p.cardsPanel.cards[cardIndex];

        card.IsOpen = false;
        card.FlashEyeOutline();
    }

    [ServerRpc(RequireOwnership = false)]
    void OnJackRevealDoneServerRpc(ServerRpcParams rpcParams = default)
    {
        isJackRevealPhase = false;

        if (gpm.currentPowerOwnerGlobalSeat >= 0)
        {
            var targets = gpm.GetAllHumanClientIds();
            if (targets.Count > 0)
                gpm.EndPowerAvatarClientRpc(
                    gpm.currentPowerOwnerGlobalSeat,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
                );
        }
        gpm.currentPowerOwnerGlobalSeat = -1;

        if (IsHost)
            StartCoroutine(gpm.DelayedNextPlayerTurn(1.0f));
        currentJackUserClientId = ulong.MaxValue;
        currentJackUserIsBot = false;
    }

    [ClientRpc]
    public void StartGoldenJackRevealLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId)
            return;
        OnGoldenJackCardDiscardedByMe();
    }

    void OnGoldenJackCardDiscardedByMe()
    {
        if (!gpm.IsMyTurn() || !gpm.players[0].isUserPlayer)
        {
            return;
        }
        isGoldenJackRevealPhase = true;

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p = gpm.players[seat];
            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
            {
                var handCard = p.cardsPanel.cards[i];
                handCard.ShowGlow(true);
                handCard.IsClickable = true;
                int s = seat, idx = i;
                handCard.onClick = null;
                handCard.onClick = (clickedCard) =>
                {
                    if (!isGoldenJackRevealPhase) return;
                    isGoldenJackRevealPhase = false;
                    gpm.DisableAllHandCardGlowAllPlayers();
                    int globalPlayerIndex = gpm.GetGlobalIndexFromLocal(s);
                    RequestRevealAllHandCardsServerRpc(globalPlayerIndex);
                    gpm.UpdateDeckClickability();
                };
            }
        }
        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealAllHandCardsServerRpc(int playerIndex, ServerRpcParams serverRpcParams = default)
    {
        if (gpm.turnTimeoutCoroutine != null)
        {
            StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        ulong goldenJackUserClientId = serverRpcParams.Receive.SenderClientId;

        List<CardRevealInfo> revealInfos = new List<CardRevealInfo>();
        var targetPlayer = gpm.players[playerIndex];
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            var handCard = targetPlayer.cardsPanel.cards[i];
            if (handCard == null) continue;
            revealInfos.Add(new CardRevealInfo { cardIndex = i, type = handCard.Type, value = handCard.Value });
        }

        RevealAllHandCardsClientRpc(playerIndex, revealInfos.ToArray(), goldenJackUserClientId,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { goldenJackUserClientId } } }
        );

        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            FlashMarkedOutlineClientRpc(playerIndex, i, goldenJackUserClientId, false);
        }

        StartCoroutine(ExposeZeroAfterDelayCoroutine(goldenJackUserClientId));
    }

    [ClientRpc]
    void RevealAllHandCardsClientRpc(int playerIndex, CardRevealInfo[] infos, ulong goldenJackUserClientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != goldenJackUserClientId)
            return;
        var p = gpm.players[gpm.GetLocalIndexFromGlobal(playerIndex)];
        foreach (var info in infos)
        {
            var card = p.cardsPanel.cards[info.cardIndex];
            if (card != null)
            {
                card.Type = info.type;
                card.Value = info.value;
                card.IsOpen = true;
                card.FlashMarkedOutline();
                StartCoroutine(HideCardAfterDelay(card, 2f));
            }
        }
    }

    public IEnumerator SimulateBotGoldenJackReveal(ulong botClientId)
    {
        if (botGoldenJackRevealActive)
        {
            yield break;
        }
        botGoldenJackRevealActive = true;

        yield return new WaitForSeconds(Random.Range(1f, 2f));

        List<int> validTargets = new List<int>();
        for (int i = 0; i < gpm.players.Count; i++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(i)].clientId;
            if (gpm.players[i].isInRoom && gpm.players[i].cardsPanel.cards.Count > 0 && pClientId != botClientId)
                validTargets.Add(i);
        }
        if (validTargets.Count == 0)
        {
            Debug.LogWarning("[SimulateBotGoldenJackReveal] No valid target players!");
            botGoldenJackRevealActive = false;
            yield break;
        }
        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];

        var targetPlayer = gpm.players[targetPlayerIndex];
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            List<ulong> realClientIds = new List<ulong>();
            foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
            {
                if (!gpm.IsBotClientId(pd.clientId))
                    realClientIds.Add(pd.clientId);
            }
            FlashMarkedOutlineClientRpc(targetPlayerIndex, i, botClientId, false,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = realClientIds } });
        }

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        yield return new WaitForSeconds(1.0f);

        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(pi)].clientId;
            if (pClientId == botClientId) continue; // skip bot user

            for (int ci = 0; ci < gpm.players[pi].cardsPanel.cards.Count; ci++)
            {
                var card = gpm.players[pi].cardsPanel.cards[ci];
                if (card == null) continue;
                if (card.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        // Play sound and effect for all real clients if Zero found
        if (zeroHolderPlayerIndex != -1)
        {
            List<ulong> realClientIds = new List<ulong>();
            foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
            {
                if (!gpm.IsBotClientId(pd.clientId))
                    realClientIds.Add(pd.clientId);
            }
            PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = realClientIds }
            });

            ShowZeroHolderTimerEffectClientRpc(gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex));
            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
            OnJackRevealDoneServerRpc();
        }

        botGoldenJackRevealActive = false;
    }

    private bool botJackRevealActive = false;

    public IEnumerator SimulateBotJackReveal(ulong botClientId)
    {
        if (botJackRevealActive)
        {
            Debug.LogWarning("SimulateBotJackReveal: Already active, ignoring duplicate call.");
            yield break;
        }
        botJackRevealActive = true;

        Debug.Log($"[SimulateBotJackReveal] botClientId={botClientId}");
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        List<int> validTargets = new List<int>();
        for (int i = 0; i < gpm.players.Count; i++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(i)].clientId;
            if (gpm.players[i].isInRoom && gpm.players[i].cardsPanel.cards.Count > 0 && pClientId != botClientId)
                validTargets.Add(i);
        }
        if (validTargets.Count == 0)
        {
            Debug.LogWarning("[SimulateBotJackReveal] No valid target players!");
            botJackRevealActive = false;
            yield break;
        }
        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];
        int targetCardIndex = Random.Range(0, gpm.players[targetPlayerIndex].cardsPanel.cards.Count);

        Debug.Log($"[SimulateBotJackReveal] Bot reveals playerIndex={targetPlayerIndex} cardIndex={targetCardIndex}");

        List<ulong> realClientIds = new List<ulong>();
        foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
        {
            if (!gpm.IsBotClientId(pd.clientId))
                realClientIds.Add(pd.clientId);
        }
        FlashMarkedOutlineClientRpc(targetPlayerIndex, targetCardIndex, botClientId, true,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = realClientIds }
            }
        );

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        yield return new WaitForSeconds(1.0f);

        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(pi)].clientId;
            if (pClientId == botClientId) continue; // skip Jack user

            for (int ci = 0; ci < gpm.players[pi].cardsPanel.cards.Count; ci++)
            {
                var card = gpm.players[pi].cardsPanel.cards[ci];
                if (card == null) continue;
                if (card.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = realClientIds }
            });

            ShowZeroHolderTimerEffectClientRpc(gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex));
            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
            OnJackRevealDoneServerRpc();
        }

        botJackRevealActive = false;
    }

}
