﻿using System;
using LiteNetLib.Utils;

namespace LiteNetLibHighLevel
{
    [System.Serializable]
    public class SyncFieldInt : LiteNetLibSyncFieldBase<int>
    {
        public override bool IsValueChanged(int newValue)
        {
            return newValue != value;
        }

        public override void Deserialize(NetDataReader reader)
        {
            value = reader.GetInt();
        }

        public override void Serialize(NetDataWriter writer)
        {
            writer.Put(value);
        }
    }
}
