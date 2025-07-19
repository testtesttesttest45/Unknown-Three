using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Skip : NetworkBehaviour
{
    public static Skip Instance;

    void Awake()
    {
        Instance = this;
    }

    public void TriggerSkip()
    {
        int skippedLocalIndex = GamePlayManager.instance.currentPlayerIndex;

        GamePlayManager.instance.NextPlayerIndex();
        int skippedGlobalIndex = GamePlayManager.instance.GetGlobalIndexFromLocal(GamePlayManager.instance.currentPlayerIndex);

        ShowSkippedPlayerClientRpc(skippedGlobalIndex);

        GamePlayManager.instance.NextPlayerIndex();

        GamePlayManager.instance.StartPlayerTurnForAllClientRpc(
            GamePlayManager.instance.GetGlobalIndexFromLocal(GamePlayManager.instance.currentPlayerIndex)
        );
    }

    [ClientRpc]
    void ShowSkippedPlayerClientRpc(int globalIndex)
    {
        int localIndex = GamePlayManager.instance.GetLocalIndexFromGlobal(globalIndex);
        if (localIndex >= 0 && localIndex < GamePlayManager.instance.players.Count)
        {
            GamePlayManager.instance.players[localIndex].ShowMessage("Turn Skipped", false, 2f);
        }
    }
}
