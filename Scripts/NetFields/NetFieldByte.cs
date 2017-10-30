﻿using LiteNetLib.Utils;

namespace LiteNetLibHighLevel
{
    public class NetFieldByte : LiteNetLibNetField<byte>
    {
        public override void Deserialize(NetDataReader reader)
        {
            Value = reader.GetByte();
        }

        public override void Serialize(NetDataWriter writer)
        {
            writer.Put(Value);
        }
    }
}
