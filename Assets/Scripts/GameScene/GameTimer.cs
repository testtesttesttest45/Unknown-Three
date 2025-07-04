using TMPro;
using UnityEngine;
using System.Collections;
using Unity.Netcode;
using System.Collections.Generic;

public class GameTimer : NetworkBehaviour
{
    public float totalTime = 20f;
    public TextMeshProUGUI timerText;

    [SerializeField] private GameOverUI gameOverUI;

    public NetworkVariable<float> networkRemainingTime = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool isTimerRunning = false;


    //void Start()
    //{
    //    timerText = GetComponent<TextMeshProUGUI>();
    //    UpdateTimerDisplay(networkRemainingTime.Value);
    //}

    public static GameTimer Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        networkRemainingTime.OnValueChanged += OnNetworkTimeChanged;
    }

    private void OnNetworkTimeChanged(float oldTime, float newTime)
    {
        UpdateTimerDisplay(newTime);
    }


    void Start()
    {
        timerText = GetComponent<TextMeshProUGUI>();

        if (IsServer)
        {
            networkRemainingTime.Value = totalTime;
        }

        UpdateTimerDisplay(networkRemainingTime.Value);
    }

    void Update()
    {
        if (IsServer && isTimerRunning && networkRemainingTime.Value > 0f)
        {
            float newTime = networkRemainingTime.Value - Time.deltaTime;

            if (newTime <= 0f)
            {
                newTime = 0f;
                isTimerRunning = false;

                StopTimer();
            }

            networkRemainingTime.Value = newTime;
        }

        UpdateTimerDisplay(networkRemainingTime.Value);
    }

    public void StartTimer()
    {
        if (IsServer)
        {
            networkRemainingTime.Value = totalTime;
            isTimerRunning = true;
        }
    }

    public void StopTimer()
    {
        if (!IsServer) return;

        if (isTimerRunning)
        {
            isTimerRunning = false;
        }

        // ShowGameOverClientRpc();
    }


    void UpdateTimerDisplay(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        timerText.text = string.Format("{0:0}:{1:00}", minutes, seconds);
    }

   
    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
        networkRemainingTime.OnValueChanged -= OnNetworkTimeChanged;
    }



}