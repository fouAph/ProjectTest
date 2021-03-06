using LiteNetLib.Utils;
using System.Collections.Generic;

namespace MultiplayerARPG.MMO
{
    public struct CharactersResp : INetSerializable
    {
        public List<PlayerCharacterData> List { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            List = reader.GetList<PlayerCharacterData>();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutList(List);
        }
    }
}