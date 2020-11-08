﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace LiteNetLibManager
{
    public abstract partial class LiteNetLibSyncList : LiteNetLibElement
    {
        public partial struct Operation
        {
            public const byte Add = 0;
            public const byte Clear = 1;
            public const byte Insert = 2;
            public const byte RemoveAt = 3;
            public const byte Set = 4;
            public const byte Dirty = 5;
            public const byte RemoveFirst = 6;
            public const byte RemoveLast = 7;
            public const byte AddRangeStart = 8;
            public const byte AddRangeItem = 9;
            public const byte AddRangeEnd = 10;

            public Operation(byte value)
            {
                Value = value;
            }

            public byte Value { get; private set; }

            public static implicit operator byte(Operation operation)
            {
                return operation.Value;
            }

            public static implicit operator Operation(byte value)
            {
                return new Operation(value);
            }
        }

        public delegate void OnChanged(Operation op, int itemIndex);

        [Tooltip("If this is TRUE, this will update to owner client only")]
        public bool forOwnerOnly;
        public OnChanged onOperation;

        public abstract int Count { get; }
        public abstract Type GetFieldType();
        public abstract void SendOperation(Operation operation, int index);
        public abstract void SendOperation(long connectionId, Operation operation, int index);
        public abstract void DeserializeOperation(NetDataReader reader);
        public abstract void SerializeOperation(NetDataWriter writer, Operation operation, int index);

        protected override bool ValidateBeforeAccess()
        {
            return Behaviour != null && IsServer;
        }

        internal override sealed void Setup(LiteNetLibBehaviour behaviour, int elementId)
        {
            base.Setup(behaviour, elementId);
            if (Count > 0 && onOperation != null)
            {
                onOperation.Invoke(Operation.AddRangeStart, 0);
                onOperation.Invoke(Operation.AddRangeEnd, Count - 1);
            }
        }
    }

    public class LiteNetLibSyncList<TType> : LiteNetLibSyncList, IList<TType>
    {
        protected readonly List<TType> list = new List<TType>();

        public TType this[int index]
        {
            get { return list[index]; }
            set
            {
                list[index] = value;
                SendOperation(Operation.Set, index);
            }
        }

        public override sealed int Count
        {
            get { return list.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public TType Get(int index)
        {
            return this[index];
        }

        public void Set(int index, TType value)
        {
            this[index] = value;
        }

        public void Add(TType item)
        {
            list.Add(item);
            SendOperation(Operation.Add, list.Count - 1);
        }

        public void AddRange(IEnumerable<TType> collection)
        {
            SendOperation(Operation.AddRangeStart, list.Count - 1);
            foreach (TType item in collection)
            {
                list.Add(item);
                SendOperation(Operation.AddRangeItem, list.Count - 1);
            }
            SendOperation(Operation.AddRangeEnd, list.Count - 1);
        }

        public void Insert(int index, TType item)
        {
            list.Insert(index, item);
            SendOperation(Operation.Insert, index);
        }

        public bool Contains(TType item)
        {
            return list.Contains(item);
        }

        public int IndexOf(TType item)
        {
            return list.IndexOf(item);
        }

        public bool Remove(TType value)
        {
            int index = IndexOf(value);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            if (index == 0)
            {
                list.RemoveAt(index);
                SendOperation(Operation.RemoveFirst, 0);
            }
            else if (index == list.Count - 1)
            {
                list.RemoveAt(index);
                SendOperation(Operation.RemoveLast, index);
            }
            else
            {
                list.RemoveAt(index);
                SendOperation(Operation.RemoveAt, index);
            }
        }

        public void Clear()
        {
            list.Clear();
            SendOperation(Operation.Clear, -1);
        }

        public void CopyTo(TType[] array, int arrayIndex)
        {
            list.CopyTo(array, arrayIndex);
        }

        public IEnumerator<TType> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return list.GetEnumerator();
        }

        public void Dirty(int index)
        {
            SendOperation(Operation.Dirty, index);
        }

        public override sealed Type GetFieldType()
        {
            return typeof(TType);
        }

        public override sealed void SendOperation(Operation operation, int index)
        {
            OnOperation(operation, index);

            if (!ValidateBeforeAccess())
                return;

            if (forOwnerOnly)
            {
                if (Manager.ContainsConnectionId(ConnectionId))
                    SendOperation(ConnectionId, operation, index);
            }
            else
            {
                foreach (long connectionId in Manager.GetConnectionIds())
                {
                    if (Identity.IsSubscribedOrOwning(connectionId))
                        SendOperation(connectionId, operation, index);
                }
            }
        }

        public override sealed void SendOperation(long connectionId, Operation operation, int index)
        {
            if (!ValidateBeforeAccess())
            {
                Logging.LogError(LogTag, "Error while send operation, behaviour is empty or not the server");
                return;
            }

            SendingConnectionId = connectionId;
            Manager.ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.OperateSyncList, (writer) => SerializeForSendOperation(writer, operation, index));
        }

        protected void SerializeForSendOperation(NetDataWriter writer, Operation operation, int index)
        {
            LiteNetLibElementInfo.SerializeInfo(GetInfo(), writer);
            SerializeOperation(writer, operation, index);
        }

        public override sealed void DeserializeOperation(NetDataReader reader)
        {
            Operation operation = reader.GetByte();
            int index = -1;
            TType item;
            switch (operation)
            {
                case Operation.Add:
                    index = list.Count;
                    item = DeserializeValueForAddOrInsert(index, reader);
                    list.Add(item);
                    break;
                case Operation.AddRangeStart:
                case Operation.AddRangeEnd:
                    index = list.Count - 1;
                    break;
                case Operation.AddRangeItem:
                    index = list.Count;
                    item = DeserializeValueForAddOrInsert(index, reader);
                    list.Add(item);
                    break;
                case Operation.Insert:
                    index = reader.GetInt();
                    item = DeserializeValueForAddOrInsert(index, reader);
                    list.Insert(index, item);
                    break;
                case Operation.Set:
                case Operation.Dirty:
                    index = reader.GetInt();
                    item = DeserializeValueForSetOrDirty(index, reader);
                    list[index] = item;
                    break;
                case Operation.RemoveAt:
                    index = reader.GetInt();
                    list.RemoveAt(index);
                    break;
                case Operation.RemoveFirst:
                    index = 0;
                    list.RemoveAt(index);
                    break;
                case Operation.RemoveLast:
                    index = list.Count - 1;
                    list.RemoveAt(index);
                    break;
                case Operation.Clear:
                    list.Clear();
                    break;
                default:
                    index = reader.GetInt();
                    item = DeserializeValueForCustomDirty(index, operation, reader);
                    list[index] = item;
                    break;
            }
            OnOperation(operation, index);
        }

        public override sealed void SerializeOperation(NetDataWriter writer, Operation operation, int index)
        {
            writer.Put((byte)operation);
            switch (operation)
            {
                case Operation.Add:
                case Operation.AddRangeItem:
                    SerializeValueForAddOrInsert(index, writer, list[index]);
                    break;
                case Operation.Insert:
                    writer.Put(index);
                    SerializeValueForAddOrInsert(index, writer, list[index]);
                    break;
                case Operation.Set:
                case Operation.Dirty:
                    writer.Put(index);
                    SerializeValueForSetOrDirty(index, writer, list[index]);
                    break;
                case Operation.RemoveAt:
                    writer.Put(index);
                    break;
                case Operation.RemoveFirst:
                case Operation.RemoveLast:
                case Operation.Clear:
                case Operation.AddRangeStart:
                case Operation.AddRangeEnd:
                    break;
                default:
                    writer.Put(index);
                    SerializeValueForCustomDirty(index, operation, writer, list[index]);
                    break;
            }
        }

        protected virtual TType DeserializeValue(NetDataReader reader)
        {
            return reader.GetValue<TType>();
        }

        protected virtual void SerializeValue(NetDataWriter writer, TType value)
        {
            writer.PutValue(value);
        }

        protected virtual TType DeserializeValueForAddOrInsert(int index, NetDataReader reader)
        {
            return DeserializeValue(reader);
        }

        protected virtual void SerializeValueForAddOrInsert(int index, NetDataWriter writer, TType value)
        {
            SerializeValue(writer, value);
        }

        protected virtual TType DeserializeValueForSetOrDirty(int index, NetDataReader reader)
        {
            return DeserializeValue(reader);
        }

        protected virtual void SerializeValueForSetOrDirty(int index, NetDataWriter writer, TType value)
        {
            SerializeValue(writer, value);
        }

        protected virtual void SerializeValueForCustomDirty(int index, byte customOperation, NetDataWriter writer, TType value)
        {
            SerializeValue(writer, value);
        }

        protected virtual TType DeserializeValueForCustomDirty(int index, byte customOperation, NetDataReader reader)
        {
            return DeserializeValue(reader);
        }

        protected void OnOperation(Operation operation, int index)
        {
            if (operation.Value == Operation.AddRangeItem)
                return;

            if (onOperation != null)
                onOperation.Invoke(operation, index);
        }
    }

    [Serializable]
    public class LiteNetLibSyncListWithElement<TType> : LiteNetLibSyncList<TType>
        where TType : INetSerializableWithElement, new()
    {
        protected override TType DeserializeValue(NetDataReader reader)
        {
            return reader.GetValue<TType>(this);
        }

        protected override void SerializeValue(NetDataWriter writer, TType value)
        {
            writer.PutValue(this, value);
        }
    }

    #region Implement for general usages and serializable
    // Generics

    [Serializable]
    public class SyncListBool : LiteNetLibSyncList<bool>
    {
    }

    [Serializable]
    public class SyncListByte : LiteNetLibSyncList<byte>
    {
    }

    [Serializable]
    public class SyncListChar : LiteNetLibSyncList<char>
    {
    }

    [Serializable]
    public class SyncListDouble : LiteNetLibSyncList<double>
    {
    }

    [Serializable]
    public class SyncListFloat : LiteNetLibSyncList<float>
    {
    }

    [Serializable]
    public class SyncListInt : LiteNetLibSyncList<int>
    {
    }

    [Serializable]
    public class SyncListLong : LiteNetLibSyncList<long>
    {
    }

    [Serializable]
    public class SyncListSByte : LiteNetLibSyncList<sbyte>
    {
    }

    [Serializable]
    public class SyncListShort : LiteNetLibSyncList<short>
    {
    }

    [Serializable]
    public class SyncListString : LiteNetLibSyncList<string>
    {
    }

    [Serializable]
    public class SyncListUInt : LiteNetLibSyncList<uint>
    {
    }

    [Serializable]
    public class SyncListULong : LiteNetLibSyncList<ulong>
    {
    }

    [Serializable]
    public class SyncListUShort : LiteNetLibSyncList<ushort>
    {
    }

    // Unity

    [Serializable]
    public class SyncListColor : LiteNetLibSyncList<Color>
    {
    }

    [Serializable]
    public class SyncListQuaternion : LiteNetLibSyncList<Quaternion>
    {
    }

    [Serializable]
    public class SyncListVector2 : LiteNetLibSyncList<Vector2>
    {
    }

    [Serializable]
    public class SyncListVector2Int : LiteNetLibSyncList<Vector2Int>
    {
    }

    [Serializable]
    public class SyncListVector3 : LiteNetLibSyncList<Vector3>
    {
    }

    [Serializable]
    public class SyncListVector3Int : LiteNetLibSyncList<Vector3Int>
    {
    }

    [Serializable]
    public class SyncListVector4 : LiteNetLibSyncList<Vector4>
    {
    }

    [Serializable]
    [Obsolete("SyncList<Int,Short,Long,UInt,UShort,ULong> already packed. So you don't have to use this class")]
    public class SyncListPackedUShort : LiteNetLibSyncList<PackedUShort>
    {
    }

    [Serializable]
    [Obsolete("SyncList<Int,Short,Long,UInt,UShort,ULong> already packed. So you don't have to use this class")]
    public class SyncListPackedUInt : LiteNetLibSyncList<PackedUInt>
    {
    }

    [Serializable]
    [Obsolete("SyncList<Int,Short,Long,UInt,UShort,ULong> already packed. So you don't have to use this class")]
    public class SyncListPackedULong : LiteNetLibSyncList<PackedULong>
    {
    }

    [Serializable]
    [Obsolete("SyncList<Int,Short,Long,UInt,UShort,ULong> already packed. So you don't have to use this class")]
    public class SyncListPackedShort : LiteNetLibSyncList<PackedShort>
    {
    }

    [Serializable]
    [Obsolete("SyncList<Int,Short,Long,UInt,UShort,ULong> already packed. So you don't have to use this class")]
    public class SyncListPackedInt : LiteNetLibSyncList<PackedInt>
    {
    }

    [Serializable]
    [Obsolete("SyncList<Int,Short,Long,UInt,UShort,ULong> already packed. So you don't have to use this class")]
    public class SyncListPackedLong : LiteNetLibSyncList<PackedLong>
    {
    }

    [Serializable]
    public class SyncListDirectionVector2 : LiteNetLibSyncList<DirectionVector2>
    {
    }

    [Serializable]
    public class SyncListDirectionVector3 : LiteNetLibSyncList<DirectionVector3>
    {
    }
    #endregion
}
