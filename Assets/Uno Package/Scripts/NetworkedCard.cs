//using Unity.Netcode;

//public enum CardType { Other, Red, Yellow, Green, Blue }
//public enum CardValue
//{
//    Zero, One, Two, Three, Four, Five, Six, Seven, Eight, Nine,
//    Skip, Reverse, DrawTwo, Wild, DrawFour
//}

//[System.Serializable]
//public struct NetworkedCard : INetworkSerializable, System.IEquatable<NetworkedCard>
//{
//    public CardType type;
//    public CardValue value;

//    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
//    {
//        serializer.SerializeValue(ref type);
//        serializer.SerializeValue(ref value);
//    }
//    public bool Equals(NetworkedCard other) => type == other.type && value == other.value;
//}
