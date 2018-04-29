﻿using System.Collections;
using System.Collections.Generic;
using LiteNetLib.Utils;

namespace LiteNetLibHighLevel
{
    public class ServerSceneChangeMessage : ILiteNetLibMessage
    {
        public string serverSceneName;

        public void Deserialize(NetDataReader reader)
        {
            serverSceneName = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(serverSceneName);
        }
    }
}
