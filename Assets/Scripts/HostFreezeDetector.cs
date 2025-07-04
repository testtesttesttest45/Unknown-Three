using UnityEngine;
using Unity.Netcode;

public class HostFreezeDetector : MonoBehaviour
{
    private float lastTimeValue = -1f;
    private float lastUpdateTime;
    private float freezeDuration;

    private float pauseThreshold = 1f;
    private float disconnectThreshold = 5f;

    private GameTimer gameTimer;
    private bool gameOverTriggered = false;

    void Start()
    {
        gameOverTriggered = false;

        if (!NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
        {
            Destroy(this);
            return;
        }

        lastUpdateTime = Time.realtimeSinceStartup;
        gameTimer = FindObjectOfType<GameTimer>();
    }


    void OnEnable()
    {
        Debug.Log("🔄 HostFreezeDetector enabled - resetting timer");

        gameOverTriggered = false;
        lastTimeValue = -1f;
        lastUpdateTime = Time.realtimeSinceStartup;
        freezeDuration = 0f;
    }


    void Update()
    {
        if (!NetworkManager.Singleton.IsClient || GameOverUI.GameHasEnded || !GameManager.Instance.IsGamePlaying())
            return;

        if (GameOverUI.GameHasEnded)
        {
            Debug.Log("GameOver detected, disabling HostFreezeDetector.");
            this.enabled = false;
            return;
        }


        if (gameTimer == null)
        {
            gameTimer = FindObjectOfType<GameTimer>();
            return;
        }

        float currentTime = gameTimer.networkRemainingTime.Value;

        if (currentTime <= 0f)
            return;

        if (Mathf.Approximately(currentTime, lastTimeValue))
        {
            freezeDuration = Time.realtimeSinceStartup - lastUpdateTime;

            if (freezeDuration >= pauseThreshold && !GameOverUI.GameHasEnded && !GamePauseUI.InstanceShown && GamePauseUI.Instance != null)
            {
                Debug.Log("Triggering buffered GamePauseUI");
                GamePauseUI.Instance.ShowPauseBuffered(1f);
            }


            if (!gameOverTriggered && freezeDuration > disconnectThreshold - 0.05f)
            {
                gameOverTriggered = true;
                Debug.LogWarning($"Host frozen for {freezeDuration:F2}s — triggering GameOver.");
                GamePauseUI.Instance?.HidePause();
                TriggerGameOverAsDraw();
            }
        }
        else
        {
            lastTimeValue = currentTime;
            lastUpdateTime = Time.realtimeSinceStartup;

            if (GamePauseUI.InstanceShown)
                GamePauseUI.Instance?.HidePause();
        }
    }


    private void TriggerGameOverAsDraw()
    {
        var gameOverUI = FindObjectOfType<GameOverUI>(true);
        if (gameOverUI == null)
        {
            Debug.LogError("GameOverUI not found — can't trigger game over.");
            return;
        }

        if (GameOverUI.GameHasEnded)
        {
            Debug.Log("GameOver already shown, skipping.");
            return;
        }

        Debug.Log("Triggering GameOver from HostFreezeDetector");
        gameOverUI.ShowGameOver("The other player has disconnected", -1);
    }


}