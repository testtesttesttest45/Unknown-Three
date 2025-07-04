using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using Unity.Netcode;


public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private TextMeshProUGUI winnerNameText;
    [SerializeField] private TextMeshProUGUI winnerLabelText;
    public static bool GameHasEnded = false;
    public static GameOverUI Instance;

    private void Awake()
    {
        GameHasEnded = false;
        Instance = this;
        mainMenuButton.onClick.AddListener(() => {
            StartCoroutine(ReturnToMainMenu());
        });
    }

    private IEnumerator ReturnToMainMenu()
    {
        SessionManager.CleanUpSession();
        yield return null; // Allow one frame for destruction
        Loader.Load(Loader.Scene.HomeScene);
    }

    private void Start()
    {
        gameOverPanel.SetActive(false);
        GameHasEnded = false;
    }

    public void ShowGameOver(string winnerName, int modelId)
    {
        DestroyAllHostFreezeDetectors();
        SoundManager.Instance?.StopBGM();
        GamePauseUI.ResetState();
        gameOverPanel.SetActive(true);

        if (GamePauseUI.InstanceShown)
        {
            GamePauseUI.Instance?.HidePause();
            GamePauseUI.Instance = null;
        }

        HostDisconnectUI disconnectUI = FindObjectOfType<HostDisconnectUI>(true);
        if (disconnectUI != null)
        {
            disconnectUI.Hide();
        }

        bool isSpecialDisconnect = winnerName == "The other player has disconnected";
        bool isDraw = string.IsNullOrEmpty(winnerName) && !isSpecialDisconnect;

        winnerLabelText.gameObject.SetActive(!(isDraw || isSpecialDisconnect));

        if (isSpecialDisconnect)
        {
            winnerNameText.text = "The other player has disconnected";
            SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.drawClip);
        }
        else if (isDraw)
        {
            winnerNameText.text = "Draw!";
            SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.drawClip);
        }
        else
        {

            winnerNameText.text = winnerName;

            // Check if local player won
            string localPlayerName = MultiplayerManager.Instance.GetPlayerName();
            Debug.Log($"[GameOverUI] winnerName: {winnerName}, localPlayerName: {localPlayerName}");
            Debug.Log($"[GameOverUI] victoryClip: {SoundManager.Instance?.victoryClip}, defeatClip: {SoundManager.Instance?.defeatClip}");

            if (winnerName == localPlayerName)
            {
                SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.victoryClip);
            }
            else
            {
                SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.defeatClip);
            }
        }

        mainMenuButton.Select();
    }


    

    public static void DestroyAllHostFreezeDetectors()
    {
        var detectors = GameObject.FindObjectsOfType<HostFreezeDetector>(true);
        foreach (var detector in detectors)
        {
            GameObject.Destroy(detector);
        }
    }

    public void ShowTeamGameOver(string winnerNames, int model1, int model2)
    {
        DestroyAllHostFreezeDetectors();
        SoundManager.Instance?.StopBGM();
        GamePauseUI.ResetState();
        gameOverPanel.SetActive(true);

        winnerNameText.text = winnerNames;

        string localPlayerName = MultiplayerManager.Instance.GetPlayerName();
        if (!string.IsNullOrEmpty(localPlayerName) && winnerNames.Contains(localPlayerName))
        {
            SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.victoryClip);
        }
        else
        {
            SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.defeatClip);
        }
    }

}
