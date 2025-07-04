using UnityEngine;
using System.Collections;

public class GamePauseUI : MonoBehaviour
{
    private static GamePauseUI instance;
    private bool isHostPaused;

    public static GamePauseUI Instance
    {
        get => instance;
        set => instance = value;
    }

    public static bool InstanceShown => instance != null && instance.isHostPaused;

    [SerializeField] private GameObject pausePanel;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        pausePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        ResetState();
    }

    public static void ResetState()
    {
        if (instance != null)
        {
            instance.isHostPaused = false;
            instance.pausePanel.SetActive(false);
            instance = null;
        }
    }

    public void ShowPause()
    {
        if (isHostPaused) return;
        Debug.Log("Showing pause panel");
        gameObject.SetActive(true);
        isHostPaused = true;
        pausePanel.SetActive(true);
    }

    private Coroutine pauseBufferCoroutine;

    public void ShowPauseBuffered(float bufferTime = 1f)
    {
        if (GameOverUI.GameHasEnded || this == null) return;

        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);

        if (pauseBufferCoroutine != null)
            StopCoroutine(pauseBufferCoroutine);

        pauseBufferCoroutine = StartCoroutine(BufferPause(bufferTime));
    }

    private IEnumerator BufferPause(float bufferTime)
    {
        yield return new WaitForSecondsRealtime(bufferTime);

        if (GameOverUI.GameHasEnded || this == null) yield break;

        if (!isHostPaused)
        {
            Debug.Log("Showing buffered pause panel");
            gameObject.SetActive(true);
            isHostPaused = true;
            pausePanel.SetActive(true);
        }
    }

    public void HidePause()
    {
        if (pauseBufferCoroutine != null)
        {
            StopCoroutine(pauseBufferCoroutine);
            pauseBufferCoroutine = null;
        }

        isHostPaused = false;
        pausePanel.SetActive(false);
        gameObject.SetActive(false);
    }



}
