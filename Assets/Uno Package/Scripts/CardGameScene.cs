using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CardGameScene : MonoBehaviour
{
    public static CardGameScene instance;
    public Popup menuPopup, exitPopup;
    public Toggle menuSfxToggle;

    void Awake()
    {
        instance = this;
        Time.timeScale = 1f;
        menuSfxToggle.isOn = CardGameManager.IsSound;
        menuSfxToggle.onValueChanged.RemoveAllListeners();
        menuSfxToggle.onValueChanged.AddListener((arg0) =>
        {
            CardGameManager.PlayButton();
            CardGameManager.IsSound = arg0;
        });
    }

    private IEnumerator Start()
    {
        while (MultiplayerManager.Instance == null || MultiplayerManager.Instance.playerDataNetworkList == null)
            yield return null;
        while (MultiplayerManager.Instance.playerDataNetworkList.Count < 4)
            yield return null;
        yield return null;

        if (CardGameManager.currentGameMode == GameMode.MultiPlayer)
        {
            GamePlayManager.instance.SetupNetworkedPlayerSeats();
            GamePlayManager.instance.StartMultiplayerGame();
        }
        else
        {
            GamePlayManager.instance.SetupGame();
        }

    }



    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && Time.timeScale == 1f)
        {
            if (Popup.currentPopup != null && Popup.currentPopup.closeOnEsc)
            {
                Popup.currentPopup.HidePopup();
            }
            else if (Popup.currentPopup == null)
            {
                ShowExit();
            }
        }
    }

    public void ShowMenu()
    {
        menuPopup.ShowPopup();

        Timer.Schedule(this, 0.25f, () =>
        {
            // CUtils.ShowInterstitialAd();
        });
        CardGameManager.PlayButton();
    }

    public void HideMenu()
    {
        menuPopup.HidePopup();
    }

    public void ShowExit()
    {
        exitPopup.ShowPopup();
        Timer.Schedule(this, 0.25f, () =>
        {
            // CUtils.ShowInterstitialAd();
        });
        CardGameManager.PlayButton();
    }

    public void HideExit()
    {
        exitPopup.HidePopup();
    }

    public void CloseGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("HomeScene");
    }
}
