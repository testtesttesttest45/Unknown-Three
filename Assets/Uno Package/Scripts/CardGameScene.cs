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

}