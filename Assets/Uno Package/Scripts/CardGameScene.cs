using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CardGameScene : MonoBehaviour
{
    public static CardGameScene instance;
    public Popup menuPopup, exitPopup;
    public Toggle menuSfxToggle;
    public Toggle menuTooltipsToggle;

    void Awake()
    {
        instance = this;
        Time.timeScale = 1f;

        if (menuSfxToggle != null)
        {
            menuSfxToggle.onValueChanged.RemoveAllListeners();
            menuSfxToggle.SetIsOnWithoutNotify(CardGameManager.IsSound);
            menuSfxToggle.onValueChanged.AddListener(on =>
            {
                CardGameManager.IsSound = on;
                CardGameManager.PlayButton();
            });
        }
        else
        {
            Debug.LogWarning("[CardGameScene] menuSfxToggle not assigned.");
        }

        if (menuTooltipsToggle != null)
        {
            menuTooltipsToggle.onValueChanged.RemoveAllListeners();
            menuTooltipsToggle.SetIsOnWithoutNotify(CardGameManager.ShowTooltips);
            menuTooltipsToggle.onValueChanged.AddListener(OnTooltipsToggleChanged);
        }
        else
        {
            Debug.LogWarning("[CardGameScene] menuTooltipsToggle not assigned.");
        }
    }

    private IEnumerator Start()
    {
        while (MultiplayerManager.Instance == null || MultiplayerManager.Instance.playerDataNetworkList == null)
            yield return null;

        if (GamePlayManager.GameHasEnded)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("HomeScene");
            yield break;
        }

        while (MultiplayerManager.Instance.playerDataNetworkList.Count < 4)
            yield return null;
        yield return null;

        if (CardGameManager.currentGameMode == GameMode.MultiPlayer)
        {
            GamePlayManager.instance._peekPhaseStarted = false;
            GamePlayManager.instance._wheelPhaseStarted = false;
            GamePlayManager.instance._wheelPhaseStarted = false;
            GamePlayManager.instance.SetupNetworkedPlayerSeats();
            GamePlayManager.instance.StartMultiplayerGame();
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
        CardGameManager.PlayButton();
    }

    public void HideMenu()
    {
        menuPopup.HidePopup();
    }

    public void ShowExit()
    {
        exitPopup.ShowPopup();
        CardGameManager.PlayButton();
    }

    public void HideExit()
    {
        exitPopup.HidePopup();
    }

    public void CloseGame()
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            NetworkManager.Singleton.IsHost &&
            !GamePlayManager.GameHasEnded)
        {
            GamePlayManager.instance?.ShowDisconnectUIClientRpc();
            StartCoroutine(HostShutdownAfterNotify());
            return;
        }

        DoLocalShutdownAndReturnHome();
    }

    private IEnumerator HostShutdownAfterNotify()
    {
        yield return null;
        yield return new WaitForSeconds(0.1f);// ~100ms buffer

        DoLocalShutdownAndReturnHome();

        // Host also tears down the lobby
        LobbyManager.Instance?.DeleteLobby();
    }

    private void DoLocalShutdownAndReturnHome()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        if (GamePlayManager.instance != null)
        {
            GamePlayManager.ResetGameHasEnded();
            GamePlayManager.instance.Cleanup();
            GamePlayManager.instance = null;
        }

        var multiplayerObj = FindObjectOfType<MultiplayerManager>();
        if (multiplayerObj != null)
        {
            multiplayerObj.Cleanup();
            Destroy(multiplayerObj.gameObject);
        }

        instance = null;
        UnityEngine.SceneManagement.SceneManager.LoadScene("HomeScene");
    }

    void OnTooltipsToggleChanged(bool on)
    {
        CardGameManager.SetShowTooltips(on);

        if (!on) GamePlayManager.instance?.HideTooltipOverlay();

        CardGameManager.PlayButton();
    }
}