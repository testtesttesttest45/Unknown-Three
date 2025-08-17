using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class WastePile : MonoBehaviour
{
    private Card wasteCard;
    private bool isChoosing = false;
    private Vector3 originalScale;
    private Coroutine wasteGuardCo;

    public void SetCard(Card card)
    {
        wasteCard = card;
        wasteCard.onClick = OnWasteCardClicked;

        // no clicks if killed by King
        if (wasteCard.killedOutline != null && wasteCard.killedOutline.activeSelf)
        {
            wasteCard.IsClickable = false;
            return;
        }

        bool isTop = wasteCard.transform.GetSiblingIndex() == wasteCard.transform.parent.childCount - 1;
        bool canClick = NetworkManager.Singleton.IsClient &&
                        GamePlayManager.instance.IsMyTurn() &&
                        GamePlayManager.instance.players[0].isUserPlayer &&
                        !GamePlayManager.instance.hasPeekedCard &&
                        isTop;

        wasteCard.IsClickable = canClick;
    }


    public void ForceUpdateClickable()
    {
        if (wasteCard == null) return;

        if (wasteCard.killedOutline != null && wasteCard.killedOutline.activeSelf)
        {
            wasteCard.IsClickable = false;
            return;
        }

        bool isTop = IsTopCard();
        var gpm = GamePlayManager.instance;

        bool suppress =
            gpm.wasteInteractionStarted ||
            gpm.deckInteractionLocked ||
            gpm.isTurnEnding;

        bool canClick =
            NetworkManager.Singleton.IsClient &&
            gpm.IsMyTurn() &&
            gpm.players[0].isUserPlayer &&
            !gpm.hasPeekedCard &&
            isTop &&
            !suppress;

        wasteCard.IsClickable = canClick;
        wasteCard.onClick = canClick ? OnWasteCardClicked : null; // extra safety check
    }

    private bool IsTopCard()
    {
        if (wasteCard == null || wasteCard.transform.parent == null) return false;
        return wasteCard.transform.GetSiblingIndex() == wasteCard.transform.parent.childCount - 1;
    }


    public void Initialize(Card card)
    {
        SetCard(card);
    }

    private void OnWasteCardClicked(Card clicked)
    {
        Debug.Log("[WastePile] Waste card clicked: " + clicked.name + ", IsClickable: " + clicked.IsClickable);

        if (!GamePlayManager.instance.IsMyTurn() ||
        GamePlayManager.instance.hasPeekedCard ||
        isChoosing ||
        GamePlayManager.instance.deckInteractionLocked)
            return;

        if (!IsTopCard())
        {
            Debug.Log("[WastePile] Ignored click because this is not the top card.");
            return;
        }

        GamePlayManager.instance?.NotifyWasteInteractionStarted();

        isChoosing = true;
        GamePlayManager.instance.wasteInteractionStarted = true;

        originalScale = clicked.transform.localScale;
        clicked.transform.localScale = originalScale * 1.15f;

        var handCards = GamePlayManager.instance.players[0].cardsPanel.cards;
        for (int i = 0; i < handCards.Count; i++)
        {
            var handCard = handCards[i];
            handCard.ShowGlow(true);
            handCard.IsClickable = true;

            int index = i;
            handCard.onClick = (c) => OnHandCardClicked(index, c);
        }

        if (wasteGuardCo != null) StopCoroutine(wasteGuardCo);
        wasteGuardCo = GamePlayManager.instance.StartCoroutine(WasteSelectionGuard());

        GamePlayManager.instance.peekedCard = wasteCard;
        GamePlayManager.instance.hasPeekedCard = true;

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var peekedSerCard = new SerializableCard(wasteCard.Type, wasteCard.Value);
        GamePlayManager.instance.peekedCardsByClientId[myClientId] = peekedSerCard;

        if (!NetworkManager.Singleton.IsServer)
        {
            GamePlayManager.instance.NotifyHostPeekedCardServerRpc((int)wasteCard.Type, (int)wasteCard.Value);
        }

    }

    private void OnHandCardClicked(int index, Card handCard)
    {
        if (!isChoosing || wasteCard == null) return;
        isChoosing = false;


        GamePlayManager.instance.deckInteractionLocked = true;
        GamePlayManager.instance.wasteInteractionStarted = true;

        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();

        StopAllCoroutines();
        DisableHandGlow();

        SerializableCard newCard = new SerializableCard(wasteCard.Type, wasteCard.Value);
        SerializableCard replacedCard = new SerializableCard(handCard.Type, handCard.Value);

        wasteCard = null;

        GamePlayManager.instance.RequestWasteCardSwapServerRpc(index, newCard, replacedCard);
    }

    private IEnumerator WasteSelectionGuard()
    {
        float timeout = Mathf.Max(0.1f, GamePlayManager.instance.turnTimerDuration + 0.25f);
        float t = 0f;

        while (isChoosing && GamePlayManager.instance.IsMyTurn() && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (isChoosing) CancelChoosing();
        wasteGuardCo = null;
    }

    private void DisableHandGlow()
    {
        var handCards = GamePlayManager.instance.players[0].cardsPanel.cards;
        foreach (var c in handCards)
        {
            c.ShowGlow(false);
            c.IsClickable = false;
            c.onClick = null;
        }
    }

    public void CancelChoosing()
    {
        if (!isChoosing || wasteCard == null) return;

        isChoosing = false;
        StopAllCoroutines();
        DisableHandGlow();

        wasteCard.transform.localScale = originalScale;

        GamePlayManager.instance.wasteInteractionStarted = false;
        GamePlayManager.instance.hasPeekedCard = false;
        GamePlayManager.instance.peekedCard = null;

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        GamePlayManager.instance.peekedCardsByClientId.Remove(myClientId);

        GamePlayManager.instance.UpdateDeckClickability();
        GamePlayManager.instance.RefreshWasteInteractivity();
    }
}
