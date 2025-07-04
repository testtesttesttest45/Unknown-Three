using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class GameStartCountdownUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private GameTimer gameTimer;
    [SerializeField] private Animator animator;
    private Player player;
    public static GameStartCountdownUI Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void StartCountdown()
    {
        gameObject.SetActive(true);
        StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        int count = 3;
        SoundManager.Instance?.PlayGlobalSound(SoundManager.Instance.countdownClip);

        while (count > 0)
        {
            if (this == null || !gameObject.activeInHierarchy)
                yield break;

            countdownText.text = count.ToString();
            if (animator != null) animator.SetTrigger("NumberPopup");
            yield return new WaitForSeconds(1f);
            count--;
        }

        if (this == null || !gameObject.activeInHierarchy)
            yield break;

        countdownText.text = "GO!";
        if (animator != null) animator.SetTrigger("NumberPopup");

        Player localPlayer = null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
        

        yield return new WaitForSeconds(1f);
        gameObject.SetActive(false);

        if (gameTimer == null || player == null)
            yield break;

        gameTimer.isTimerRunning = true;
        localPlayer.isGameStarted = true;
        Bot.GameHasStarted = true;
    }

    public void InjectDependencies(GameTimer timer, Player movement)
    {
        gameTimer = timer;
        player = movement;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        if (Instance == this)
            Instance = null;
    }

}
