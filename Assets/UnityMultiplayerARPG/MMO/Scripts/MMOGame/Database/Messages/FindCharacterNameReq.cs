using LiteNetLib.Utils;

namespace MultiplayerARPG.MMO
{
    public struct FindCharacterNameReq : INetSerializable
    {
        public string CharacterName { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            CharacterName = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(CharacterName);
        }
    }
}