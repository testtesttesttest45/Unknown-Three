using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConnectingUI : MonoBehaviour
{
    private void Start()
    {
        MultiplayerManager.Instance.OnTryingToJoinGame += KitchenGameMultiplayer_OnTryingToJoinGame;
        MultiplayerManager.Instance.OnFailedToJoinGame += KitchenCardGameManager_OnFailedToJoinGame;

        Hide();
    }

    private void KitchenCardGameManager_OnFailedToJoinGame(object sender, System.EventArgs e)
    {
        Hide();
    }

    private void KitchenGameMultiplayer_OnTryingToJoinGame(object sender, System.EventArgs e)
    {
        Show();
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        MultiplayerManager.Instance.OnTryingToJoinGame -= KitchenGameMultiplayer_OnTryingToJoinGame;
        MultiplayerManager.Instance.OnFailedToJoinGame -= KitchenCardGameManager_OnFailedToJoinGame;
    }

}