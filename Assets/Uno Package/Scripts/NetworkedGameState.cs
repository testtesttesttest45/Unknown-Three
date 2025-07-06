//using Unity.Netcode;
//using System.Collections.Generic;
//using UnityEngine;

//// Make this a singleton and NetworkBehaviour!
//public class NetworkedGameState : NetworkBehaviour
//{
//    public static NetworkedGameState Instance { get; private set; }

//    public NetworkList<NetworkedCard> networkDeck;
//    public NetworkList<NetworkedCard> networkWastePile;
//    public List<NetworkList<NetworkedCard>> playerHands;

//    public override void OnNetworkSpawn()
//    {
//        Instance = this;
//        if (networkDeck == null) networkDeck = new NetworkList<NetworkedCard>();
//        if (networkWastePile == null) networkWastePile = new NetworkList<NetworkedCard>();
//        if (playerHands == null)
//        {
//            playerHands = new List<NetworkList<NetworkedCard>>();
//            for (int i = 0; i < MultiplayerManager.MAX_PLAYER_AMOUNT; i++)
//                playerHands.Add(new NetworkList<NetworkedCard>());
//        }
//    }
//}
