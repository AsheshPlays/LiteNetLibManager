﻿using System.Collections;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

namespace LiteNetLibManager
{
    public struct ServerSpawnObjectMessage : INetSerializable
    {
        public int hashAssetId;
        public uint objectId;
        public long connectionId;
        public Vector3 position;
        public Quaternion rotation;

        public void Deserialize(NetDataReader reader)
        {
            hashAssetId = reader.GetPackedInt();
            objectId = reader.GetPackedUInt();
            connectionId = reader.GetPackedLong();
            position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            rotation = Quaternion.Euler(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutPackedInt(hashAssetId);
            writer.PutPackedUInt(objectId);
            writer.PutPackedLong(connectionId);
            writer.Put(position.x);
            writer.Put(position.y);
            writer.Put(position.z);
            writer.Put(rotation.eulerAngles.x);
            writer.Put(rotation.eulerAngles.y);
            writer.Put(rotation.eulerAngles.z);
        }
    }
}
