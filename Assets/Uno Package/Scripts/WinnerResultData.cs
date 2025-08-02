using Unity.Netcode;

[System.Serializable]
public struct WinnerResultData : INetworkSerializable
{
    public string playerName;
    public int avatarIndex;
    public int totalPoints;
    public bool isUserPlayer;
    public SerializableCard[] cards;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref playerName);
        serializer.SerializeValue(ref avatarIndex);
        serializer.SerializeValue(ref totalPoints);
        serializer.SerializeValue(ref isUserPlayer);
        serializer.SerializeValue(ref cards);
    }
}
