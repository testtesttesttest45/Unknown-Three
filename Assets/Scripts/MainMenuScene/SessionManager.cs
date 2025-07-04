using UnityEngine;
using Unity.Netcode;

public static class SessionManager
{
    public static void CleanUpSession()
    {
        // shutdown network if not running
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        // ensure singletons are destroyed
        if (NetworkManager.Singleton != null)
            Object.Destroy(NetworkManager.Singleton.gameObject);

        if (MultiplayerManager.Instance != null)
            Object.Destroy(MultiplayerManager.Instance.gameObject);

        if (LobbyManager.Instance != null)
            Object.Destroy(LobbyManager.Instance.gameObject);

        // reset all static or global flags and UI references
        GameOverUI.GameHasEnded = false;
        Bot.GameHasStarted = false;
        GamePauseUI.Instance?.HidePause();
        GamePauseUI.Instance = null;
    }
}

// how to use? simply set the static event to null
// public static event EventHandler OnAnyCut;
//new public static void ResetStaticData()
//    {
//        OnAnyCut = null;
//    }
// call: CuttingCounter.ResetStaticData();
// can be called in the Awake of a script attached to a gameobject on mainmenuscene