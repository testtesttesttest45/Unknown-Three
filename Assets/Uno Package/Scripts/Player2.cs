using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Player2 : MonoBehaviour
{
    public GameObject CardPanelBG;
    public PlayerCards cardsPanel;
    public string playerName;
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

    void Start()
    {
        Timer = false;
    }

    public void SetAvatarProfile(AvatarProfile p)
    {
        playerName = p.avatarName;
        if (avatarName != null)
        {
            avatarName.text = p.avatarName;
            avatarName.GetComponent<EllipsisText>().UpdateText();
        }
        if (avatarImage != null)
            avatarImage.sprite = Resources.Load<Sprite>("Avatar/" + p.avatarIndex);
    }

    public bool Timer
    {
        get
        {
            return timerOjbect.activeInHierarchy;
        }
        set
        {
            CancelInvoke("UpdateTimer");
            timerOjbect.SetActive(value);
            if (value)
            {
                timerImage.fillAmount = 1f;
                InvokeRepeating("UpdateTimer", 0f, .1f);
            }
            else
            {
                timerImage.fillAmount = 0f;
            }
        }
    }

    void UpdateTimer()
    {
        timerImage.fillAmount -= 0.1f / totalTimer;
        if (timerImage.fillAmount <= 0)
        {
            if (choosingColor)
            {
                if (isUserPlayer)
                {
                    GamePlayManager.instance.colorChoose.HidePopup();
                }
                // ChooseBestColor();
            }
            else if (GamePlayManager.instance.IsDeckArrow)
            {
                GamePlayManager.instance.OnDeckClick();
            }
            //else if (cardsPanel.AllowedCard.Count > 0)
            //{
            //    OnCardClick(FindBestPutCard());
            //}
            else
            {
                OnTurnEnd();
            }
        }
    }


    public void OnTurn()
    {
        unoClicked = false;
        pickFromDeck = false;
        totalTimer = turnTimerDuration;
        Timer = true;

        //if (isUserPlayer)
        //{
        //    if (cardsPanel.AllowedCard.Count == 0)
        //    {
        //        GamePlayManager.instance.EnableDeckClick();
        //    }
        //}
        //else
        //{
        //    StartCoroutine(DoComputerTurn());
        //}
    }



    public void AddCard(Card c)
    {
        c.transform.SetParent(cardsPanel.transform);
        cardsPanel.cards.Add(c);
        if (isUserPlayer)
        {
            c.onClick = OnCardClick;
            c.IsClickable = false;
        }
    }

    public void AddCard(Card c, int insertIndex)
    {
        c.transform.SetParent(cardsPanel.transform);
        insertIndex = Mathf.Clamp(insertIndex, 0, cardsPanel.cards.Count);
        cardsPanel.cards.Insert(insertIndex, c);
        if (isUserPlayer)
        {
            c.onClick = OnCardClick;
            c.IsClickable = false;
        }
    }

    public void RemoveCard(Card c)
    {
        cardsPanel.cards.Remove(c);
        c.onClick = null;
        c.IsClickable = false;
        Destroy(c.gameObject);
    }



    public void OnCardClick(Card c)
    {
        if (Timer)
        {
            GamePlayManager.instance.PutCardToWastePile(c, this);
            OnTurnEnd();
        }
    }

    public void OnTurnEnd()
    {
        Timer = false;
        cardsPanel.UpdatePos();

        foreach (var card in cardsPanel.cards)
        {
            card.IsClickable = false;
            card.onClick = null;
            // card.SetGaryColor(false);
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
        int total = 0;
        foreach(var c in cardsPanel.cards)
        {
            total += c.point;
        }
        return total;
    }

    public void AddSerializableCard(SerializableCard sc, int insertIndex)
    {
        Card card = GameObject.Instantiate(GamePlayManager.instance._cardPrefab, cardsPanel.transform);
        card.Type = sc.Type;
        card.Value = sc.Value;
        card.IsOpen = false;
        card.CalcPoint();
        card.name = $"{sc.Type}_{sc.Value}";

        insertIndex = Mathf.Clamp(insertIndex, 0, cardsPanel.cards.Count);
        cardsPanel.cards.Insert(insertIndex, card);

        if (isUserPlayer)
        {
            card.onClick = OnCardClick;
            card.IsClickable = false;
        }

        cardsPanel.UpdatePos();
    }



}
