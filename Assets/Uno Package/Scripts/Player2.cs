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
            card.SetGaryColor(false);
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


    public IEnumerator DoComputerTurn()
    {
        if (cardsPanel.AllowedCard.Count > 0)
        {
            StartCoroutine(ComputerTurnHasCard(0.25f));
        }
        else
        {
            yield return new WaitForSeconds(Random.Range(1f, totalTimer * .3f));
           //  GamePlayManager.instance.EnableDeckClick();
            GamePlayManager.instance.OnDeckClick();

            if (cardsPanel.AllowedCard.Count > 0)
            {
                StartCoroutine(ComputerTurnHasCard(0.2f));
            }
        }
    }

    private IEnumerator ComputerTurnHasCard(float unoCoef)
    {
        bool unoClick = false;
        float unoPossibality = GamePlayManager.instance.UnoProbability / 100f;

        if (Random.value < unoPossibality && cardsPanel.cards.Count == 2)
        {
            yield return new WaitForSeconds(Random.Range(1f, totalTimer * unoCoef));
            GamePlayManager.instance.OnUnoClick();
            unoClick = true;
        }

        yield return new WaitForSeconds(Random.Range(1f, totalTimer * (unoClick ? unoCoef : unoCoef * 2)));
        OnCardClick(FindBestPutCard());
    }

    public Card FindBestPutCard()
    {
        List<Card> allow = cardsPanel.AllowedCard;
        allow.Sort((x, y) => y.Type.CompareTo(x.Type));
        return allow[0];
    }

    public void ChooseBestColor()
    {
        CardType temp = CardType.Other;
        if (cardsPanel.cards.Count == 1)
        {
            temp = cardsPanel.cards[0].Type;
        }
        else
        {
            int max = 1;
            for (int i = 0; i < 5; i++)
            {
                if (cardsPanel.GetCount((CardType)i) > max)
                {
                    max = cardsPanel.GetCount((CardType)i);
                    temp = (CardType)i;
                }
            }
        }

        //if (temp == CardType.Other)
        //{
        //    GamePlayManager.instance.SelectColor(Random.Range(1, 5));
        //}
        //else
        //{
        //    if (Random.value < 0.7f)
        //        GamePlayManager.instance.SelectColor((int)temp);
        //    else
        //        GamePlayManager.instance.SelectColor(Random.Range(1, 5));
        //}
    }

    Vector3 GetHandSlotWorldPos(Player2 p, int handIndex)
    {
        return p.cardsPanel.cards[handIndex].transform.position;
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
