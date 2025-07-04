using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System;
using System.Collections.Generic;

public class Bot : NetworkBehaviour
{
    public static bool GameHasStarted = false;
    
    public NetworkVariable<ulong> BotClientId = new NetworkVariable<ulong>(
    0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);


    private void Awake()
    {

    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        var playerData = MultiplayerManager.Instance.GetPlayerDataFromClientId(BotClientId.Value);

    }

    private void Start()
    {
    }

    private void Update()
    {

    }

}
