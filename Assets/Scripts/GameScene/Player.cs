using UnityEngine;
using System.Collections;
using Unity.Netcode;
using UnityEngine.UI;
using Unity.Netcode.Components;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class Player : NetworkBehaviour
{
    
    public Animator animator;


    public GameTimer gameTimer;

   
    public bool isGameStarted = false;

    

    public NetworkVariable<bool> networkGameStarted = new NetworkVariable<bool>(
    false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);


    private void Awake()
    {
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkGameStarted.OnValueChanged += (oldVal, newVal) => { };

    }
    
    void Start()
    {
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 1;
        GameManager.Instance.OnStateChanged += CardGameManager_OnStateChanged;
        gameTimer = FindObjectOfType<GameTimer>();

        var canvas = GameObject.Find("Screen Canvas").transform;

        Transform countdownUITransform = canvas.Find("GameStartCountdownUI");
        int insertIndex = countdownUITransform != null ? countdownUITransform.GetSiblingIndex() : canvas.childCount;


        if (IsOwner)
        {
            var countdownUIComponent = GameStartCountdownUI.Instance;
            if (countdownUIComponent != null)
                countdownUIComponent.InjectDependencies(gameTimer, this);
            else
                Debug.LogError("❌ Couldn't find GameStartCountdownUI during Start()");

        }
    }

    void Update()
    {


    }

    private void CardGameManager_OnStateChanged(object sender, EventArgs e)
    {
        if (GameManager.Instance.IsGamePlaying())
        {
            if (IsServer)
            {
                networkGameStarted.Value = true;
                gameTimer.StartTimer();
            }
            isGameStarted = true;
        }
    }
}