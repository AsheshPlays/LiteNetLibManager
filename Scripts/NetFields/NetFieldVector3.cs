﻿using LiteNetLib.Utils;
using UnityEngine;

namespace LiteNetLibManager
{
    public class NetFieldVector3 : LiteNetLibNetField<Vector3>
    {
        public override void Deserialize(NetDataReader reader)
        {
            Value = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }

        public override void Serialize(NetDataWriter writer)
        {
            writer.Put(Value.x);
            writer.Put(Value.y);
            writer.Put(Value.z);
        }

        public override bool IsValueChanged(Vector3 newValue)
        {
            return !newValue.Equals(Value);
        }
    }
}
