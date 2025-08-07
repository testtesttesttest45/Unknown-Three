using Unity.Netcode;

[System.Serializable]
public struct CardRevealInfo : INetworkSerializable
{
    public int cardIndex;
    public CardType type;
    public CardValue value;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref cardIndex);
        serializer.SerializeValue(ref type);
        serializer.SerializeValue(ref value);
    }
}
