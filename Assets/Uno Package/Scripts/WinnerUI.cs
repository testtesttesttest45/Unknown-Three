using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WinnerUI : MonoBehaviour
{
    [Header("References")]
    public GameObject gameOverPopup;
    public ParticleSystem starParticle;
    public List<GameObject> playerObjectPanels; // Panels for 1st, 2nd, 3rd, 4th
    public GameObject loseTimerAnimation;
    public Text resultText;

    public GameObject simpleLayout;
    public GameObject detailedLayout;
    public Toggle detailedToggle;
    public List<GameObject> detailedWinnerPanels;
    public GameObject cardPrefab;
    private WinnerResultData[] lastWinnersData;

    void Start()
    {
        detailedToggle.onValueChanged.AddListener(OnDetailsToggleChanged);
    }

    void OnDetailsToggleChanged(bool isOn)
    {
        simpleLayout.SetActive(!isOn);
        detailedLayout.SetActive(isOn);

        if (isOn)
        {
            ShowDetailedWinners(lastWinnersData);
        }
    }


    public void ShowWinners(List<Player2> players)
    {
        // sort lowest to highest points
        players.Sort((a, b) => a.GetTotalPoints().CompareTo(b.GetTotalPoints()));

        foreach (var panel in playerObjectPanels)
            panel.SetActive(false);

        for (int i = 0; i < players.Count; i++)
        {
            var panel = playerObjectPanels[i];
            panel.SetActive(true);

            var images = panel.GetComponentsInChildren<Image>();
            if (images.Length > 1)
                images[1].sprite = players[i].avatarImage.sprite;

            var nameText = panel.GetComponentInChildren<Text>();
            nameText.text = players[i].playerName;

            string place = "";
            switch (i)
            {
                case 0: place = "1st Place"; break;
                case 1: place = "2nd Place"; break;
                case 2: place = "3rd Place"; break;
                case 3: place = "4th Place"; break;
            }
            var placeTextObj = panel.transform.Find("PlaceText");
            if (placeTextObj != null)
            {
                var placeText = placeTextObj.GetComponent<Text>();
                if (placeText != null) placeText.text = place;
            }
            var pointsTextObj = panel.transform.Find("Points");
            if (pointsTextObj != null)
            {
                var pointsText = pointsTextObj.GetComponent<Text>();
                if (pointsText != null)
                    pointsText.text = players[i].GetTotalPoints() + " points";
            }

        }

        if (starParticle != null)
            starParticle.gameObject.SetActive(players[0].isUserPlayer);

        if (loseTimerAnimation != null)
        {
            loseTimerAnimation.SetActive(false);
            for (int i = 1; i < players.Count; i++)
            {
                if (players[i].isUserPlayer)
                {
                    loseTimerAnimation.SetActive(true);
                    loseTimerAnimation.transform.position = playerObjectPanels[i].transform.position;
                    break;
                }
            }
        }

        if (resultText != null)
            resultText.text = players[0].isUserPlayer ? "You Won!" : "You Lost!";

        gameOverPopup.SetActive(true);
    }

    public void ShowWinnersFromNetwork(WinnerResultData[] data)
    {
        lastWinnersData = data;
        for (int i = 0; i < playerObjectPanels.Count; i++)
            playerObjectPanels[i].SetActive(false);

        for (int i = 0; i < data.Length; i++)
        {
            var panel = playerObjectPanels[i];
            panel.SetActive(true);

            var images = panel.GetComponentsInChildren<Image>();
            if (images.Length > 1)
                images[1].sprite = Resources.Load<Sprite>("Avatar/" + data[i].avatarIndex);

            var nameText = panel.GetComponentInChildren<Text>();
            nameText.text = data[i].playerName;

            string place = "";
            switch (i)
            {
                case 0: place = "1st Place"; break;
                case 1: place = "2nd Place"; break;
                case 2: place = "3rd Place"; break;
                case 3: place = "4th Place"; break;
            }
            var placeTextObj = panel.transform.Find("PlaceText");
            if (placeTextObj != null)
            {
                var placeText = placeTextObj.GetComponent<Text>();
                if (placeText != null) placeText.text = place;
            }
            var pointsTextObj = panel.transform.Find("Points");
            if (pointsTextObj != null)
            {
                var pointsText = pointsTextObj.GetComponent<Text>();
                if (pointsText != null)
                    pointsText.text = data[i].totalPoints + " points";
            }

        }

        // Who is the local player, and did they win?
        string myName = CardGameManager.PlayerAvatarName;
        int myPlace = -1;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].playerName == myName)
            {
                myPlace = i;
                break;
            }
        }
        bool localIsWinner = (myPlace == 0);

        // Show star if local player is 1st, lose anim if not
        if (starParticle != null)
            starParticle.gameObject.SetActive(localIsWinner);

        if (loseTimerAnimation != null)
        {
            loseTimerAnimation.SetActive(!localIsWinner && myPlace > 0);
            if (!localIsWinner && myPlace > 0)
            {
                // Move lose anim to the correct panel
                loseTimerAnimation.transform.position = playerObjectPanels[myPlace].transform.position;
            }
        }

        if (resultText != null)
            resultText.text = (localIsWinner ? "You Won!" : "You Lost!");

        gameOverPopup.SetActive(true);
    }

    public void ShowDetailedWinners(WinnerResultData[] data)
    {
        if (data == null)
        {
            Debug.LogError("ShowDetailedWinners called with null data!");
            return;
        }
        for (int i = 0; i < detailedWinnerPanels.Count; i++)
            detailedWinnerPanels[i].SetActive(false);

        for (int i = 0; i < data.Length; i++)
        {
            var panel = detailedWinnerPanels[i];
            panel.SetActive(true);

            var avatarImg = panel.transform.Find("AvatarImage").GetComponent<Image>();
            avatarImg.sprite = Resources.Load<Sprite>("Avatar/" + data[i].avatarIndex);

            var nameText = panel.transform.Find("Name/Name").GetComponent<Text>();
            nameText.text = data[i].playerName;

            var placeText = panel.transform.Find("Place").GetComponent<Text>();
            placeText.text = (i + 1) + GetPlaceSuffix(i + 1) + " Place";

            var pointsText = panel.transform.Find("Points").GetComponent<Text>();
            pointsText.text = data[i].totalPoints + " points";

            var cardsPanel = panel.transform.Find("CardsPanel");
            foreach (Transform child in cardsPanel)
                Destroy(child.gameObject);
            foreach (var card in data[i].cards)
            {
                var cardObj = Instantiate(cardPrefab, cardsPanel);
                var cardScript = cardObj.GetComponent<Card>();
                cardScript.Type = card.Type;
                cardScript.Value = card.Value;
                cardScript.IsOpen = true;
                cardScript.UpdateCard();

                // ---- Force gold aura for Zero cards in winner display ----
                if (card.Value == CardValue.Zero && cardScript.specialOutline != null)
                    cardScript.specialOutline.SetActive(true);
            }

        }
    }

    string GetPlaceSuffix(int place)
    {
        if (place == 1) return "st";
        if (place == 2) return "nd";
        if (place == 3) return "rd";
        return "th";
    }


}