using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

public class Player2 : MonoBehaviour
{
    public GameObject CardPanelBG;
    public PlayerCards cardsPanel;
    public string playerName;
    public int avatarIndex;
    public bool isUserPlayer;
    public Image avatarImage;
    public Text avatarName;
    public Text messageLbl;
    public ParticleSystem starParticleSystem;
    public Image timerImage;
    public GameObject timerOjbect;

    public float turnTimerDuration = 6f;
    private float totalTimer;

    [HideInInspector]
    public bool pickFromDeck, unoClicked, choosingColor;
    [HideInInspector]
    public bool isInRoom = true;
    public bool wasTimeout = false;

    public void SetAvatarProfile(AvatarProfile p)
    {
        playerName = p.avatarName;
        avatarIndex = p.avatarIndex;
        if (avatarName != null)
        {
            avatarName.text = p.avatarName;
            avatarName.GetComponent<EllipsisText>().UpdateText();
        }
        if (avatarImage != null)
            avatarImage.sprite = Resources.Load<Sprite>("Avatar/" + p.avatarIndex);
    }

    void Start()
    {
        SetTimerVisible(false);
    }

    public void OnTurn()
    {
        unoClicked = false;
        pickFromDeck = false;
        SetTimerVisible(true);
        timerImage.fillAmount = 1f;
    }

    public void UpdateTurnTimerUI(float secondsLeft, float totalSeconds)
    {
        SetTimerVisible(true);
        timerImage.fillAmount = Mathf.Clamp01(secondsLeft / totalSeconds);
    }

    public void AddCard(Card c, int slot)
    {
        if (c == null)
        {
            // No card, just clear the slot and return
            if (cardsPanel.cards[slot] != null)
                Destroy(cardsPanel.cards[slot].gameObject);

            cardsPanel.cards[slot] = null;
            cardsPanel.UpdatePos();
            ResyncCardIndices();
            return;
        }

        c.transform.SetParent(cardsPanel.transform);
        if (cardsPanel.cards[slot] != null)
        {
            Destroy(cardsPanel.cards[slot].gameObject);
        }
        cardsPanel.cards[slot] = c;
        if (isUserPlayer)
        {
            c.onClick = OnCardClick;
            c.IsClickable = false;
        }
        cardsPanel.UpdatePos();
        ResyncCardIndices();
    }

    public void RemoveCard(Card c, bool updatePos = true)
    {
        int idx = cardsPanel.cards.IndexOf(c);
        if (idx >= 0)
        {
            c.onClick = null;
            c.IsClickable = false;
            Destroy(c.gameObject);
            cardsPanel.cards[idx] = null;
            if (updatePos)
                cardsPanel.UpdatePos();
            ResyncCardIndices();
        }
    }

    public void AddSerializableCard(SerializableCard sc, int slot)
    {
        Card card = Instantiate(GamePlayManager.instance._cardPrefab, cardsPanel.transform);
        card.Type = sc.Type;
        card.Value = sc.Value;
        card.IsOpen = false;
        card.CalcPoint();
        card.name = $"{sc.Type}_{sc.Value}";

        if (cardsPanel.cards[slot] != null)
            Destroy(cardsPanel.cards[slot].gameObject);
        cardsPanel.cards[slot] = card;

        if (isUserPlayer)
        {
            card.onClick = OnCardClick;
            card.IsClickable = false;
        }

        cardsPanel.UpdatePos();
        ResyncCardIndices();
    }

    public void ResyncCardIndices()
    {
        if (GamePlayManager.instance == null || GamePlayManager.instance.players == null)
            return;
        int localSeat = GamePlayManager.instance.players.IndexOf(this);
        for (int i = 0; i < cardsPanel.cards.Count; i++)
        {
            var card = cardsPanel.cards[i];
            if (card == null) continue;
            card.cardIndex = i;
            card.localSeat = localSeat;
        }

    }



    public void OnCardClick(Card c)
    {
        if (timerOjbect.activeInHierarchy)
        {
            GamePlayManager.instance.PutCardToWastePile(c, this);
            OnTurnEnd();
        }
    }


    public void OnTurnEnd()
    {
        SetTimerVisible(false);
        cardsPanel.UpdatePos();

        foreach (var card in cardsPanel.cards)
        {
            if (card == null) continue;
            card.IsClickable = false;
            card.onClick = null;
        }

        GamePlayManager.instance.arrowObject.SetActive(false);
        GamePlayManager.instance.unoBtn.SetActive(false);
    }

    public void ShowMessage(string message, bool playStarParticle = false, float duration = 1.5f)
    {
        StopCoroutine("MessageHideRoutine");
        messageLbl.text = message;
        messageLbl.GetComponent<Animator>().SetTrigger("show");
        if (playStarParticle)
        {
            starParticleSystem.gameObject.SetActive(true);
            starParticleSystem.Emit(30);
        }
        StartCoroutine(MessageHideRoutine(duration));
    }

    private IEnumerator MessageHideRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        messageLbl.GetComponent<Animator>().SetTrigger("hide");
    }

    public int GetTotalPoints()
    {
        int points = 0;
        foreach (var c in cardsPanel.cards)
        {
            if (c == null) continue;
            if (c.Value >= CardValue.Zero && c.Value <= CardValue.Ten)
                points += (int)c.Value;
            else // J, Q, K, Fiend, Skip
                points += 10;
        }
        return points;
    }


    public void SetTimerVisible(bool visible)
    {
        if (timerOjbect != null)
            timerOjbect.SetActive(visible);
        if (timerImage != null)
            timerImage.gameObject.SetActive(visible);
    }


}
