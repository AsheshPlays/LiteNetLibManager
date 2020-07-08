﻿using System;
using System.Reflection;
using LiteNetLib;
using LiteNetLib.Utils;

namespace LiteNetLibManager
{
    public enum FunctionReceivers : byte
    {
        Target,
        All,
        Server,
    }

    public class LiteNetLibFunction : LiteNetLibElement
    {
        public readonly Type[] ParameterTypes;
        public readonly object[] Parameters;
        public bool CanCallByEveryone { get; set; }
        private NetFunctionDelegate callback;

        protected LiteNetLibFunction() : this(0)
        {
        }

        protected LiteNetLibFunction(int parameterCount)
        {
            Parameters = new object[parameterCount];
            ParameterTypes = new Type[parameterCount];
        }

        protected LiteNetLibFunction(Type[] parameterTypes)
        {
            Parameters = new object[parameterTypes.Length];
            ParameterTypes = parameterTypes;
        }

        public LiteNetLibFunction(NetFunctionDelegate callback) : this()
        {
            this.callback = callback;
        }

        protected override bool ValidateBeforeAccess()
        {
            return Behaviour != null && (IsServer || IsOwnerClient || CanCallByEveryone);
        }

        internal virtual void HookCallback()
        {
            callback.Invoke();
        }

        protected void ServerSendCall(long connectionId, DeliveryMethod deliveryMethod)
        {
            SendingConnectionId = connectionId;
            Manager.ServerSendPacket(connectionId, deliveryMethod, LiteNetLibGameManager.GameMsgTypes.CallFunction, (writer) => SerializeForSend(writer));
        }

        protected void ClientSendCall(DeliveryMethod deliveryMethod, FunctionReceivers receivers, long targetConnectionId)
        {
            Manager.ClientSendPacket(deliveryMethod, LiteNetLibGameManager.GameMsgTypes.CallFunction, (writer) => SerializeForClient(writer, receivers, targetConnectionId));
        }

        protected void SendCall(DeliveryMethod deliveryMethod, FunctionReceivers receivers, long targetConnectionId)
        {
            LiteNetLibGameManager manager = Manager;

            if (manager.IsServer)
            {
                switch (receivers)
                {
                    case FunctionReceivers.Target:
                        if (Identity.IsSubscribedOrOwning(targetConnectionId) && manager.ContainsConnectionId(targetConnectionId))
                        {
                            // Send function call message from server to target client by target connection Id
                            ServerSendCall(targetConnectionId, deliveryMethod);
                        }
                        break;
                    case FunctionReceivers.All:
                        foreach (long connectionId in manager.GetConnectionIds())
                        {
                            if (Manager.ClientConnectionId == connectionId)
                            {
                                // This is host's networking oject, so hook callback immediately
                                // Don't have to send message to the client, because it is currently run as both server and client
                                HookCallback();
                            }
                            else if (Identity.IsSubscribedOrOwning(connectionId))
                            {
                                // Send message to subscribing clients
                                ServerSendCall(connectionId, deliveryMethod);
                            }
                        }
                        if (!Manager.IsClientConnected)
                        {
                            // It's not a host(client+host), it's just a server
                            // So hook callback immediately to do the function at server
                            HookCallback();
                        }
                        break;
                    case FunctionReceivers.Server:
                        // Call server function at server
                        // So hook callback immediately to do the function at server
                        HookCallback();
                        break;
                }
            }
            else if (manager.IsClientConnected)
            {
                // Client send net function call to server
                // Then the server will hook callback or forward message to other clients
                ClientSendCall(deliveryMethod, receivers, targetConnectionId);
            }
        }

        public void SetParameters(params object[] parameterValues)
        {
            for (int i = 0; i < Parameters.Length; ++i)
            {
                if (i >= parameterValues.Length)
                    break;
                Parameters[i] = parameterValues[i];
            }
        }

        public void Call(DeliveryMethod deliveryMethod, FunctionReceivers receivers, params object[] parameterValues)
        {
            if (!ValidateBeforeAccess())
                return;

            SetParameters(parameterValues);
            SendCall(deliveryMethod, receivers, ConnectionId);
        }

        public void Call(long connectionId, params object[] parameterValues)
        {
            if (!ValidateBeforeAccess())
                return;

            SetParameters(parameterValues);
            SendCall(DeliveryMethod.ReliableOrdered, FunctionReceivers.Target, connectionId);
        }

        public void CallWithoutParametersSet(DeliveryMethod deliveryMethod, FunctionReceivers receivers)
        {
            if (!ValidateBeforeAccess())
                return;

            SendCall(deliveryMethod, receivers, ConnectionId);
        }

        public void CallWithoutParametersSet(long connectionId)
        {
            if (!ValidateBeforeAccess())
                return;

            SendCall(DeliveryMethod.ReliableOrdered, FunctionReceivers.Target, connectionId);
        }

        protected void SerializeForClient(NetDataWriter writer, FunctionReceivers receivers, long connectionId)
        {
            writer.Put((byte)receivers);
            if (receivers == FunctionReceivers.Target)
                writer.PutPackedLong(connectionId);
            SerializeForSend(writer);
        }

        protected void SerializeForSend(NetDataWriter writer)
        {
            LiteNetLibElementInfo.SerializeInfo(GetInfo(), writer);
            SerializeParameters(writer);
        }

        public void DeserializeParameters(NetDataReader reader)
        {
            if (Parameters == null || Parameters.Length == 0)
                return;
            for (int i = 0; i < Parameters.Length; ++i)
            {
                if (ParameterTypes[i].IsArray)
                    Parameters[i] = reader.GetArray(ParameterTypes[i].GetElementType());
                else
                    Parameters[i] = reader.GetValue(ParameterTypes[i]);
            }
        }

        public void SerializeParameters(NetDataWriter writer)
        {
            if (Parameters == null || Parameters.Length == 0)
                return;
            for (int i = 0; i < Parameters.Length; ++i)
            {
                if (ParameterTypes[i].IsArray)
                    writer.PutArray(ParameterTypes[i].GetElementType(), Parameters[i]);
                else
                    writer.PutValue(ParameterTypes[i], Parameters[i]);
            }
        }
    }

    #region Implement for multiple parameter usages
    public class LiteNetLibFunctionDynamic : LiteNetLibFunction
    {
        /// <summary>
        /// The class which contain the function
        /// </summary>
        private object instance;
        
        private MethodInfo callback;

        public LiteNetLibFunctionDynamic(Type[] parameterTypes, object instance, MethodInfo callback) : base(parameterTypes)
        {
            this.instance = instance;
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback.Invoke(instance, Parameters);
        }
    }

    public class LiteNetLibFunction<T1> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1> callback;

        protected LiteNetLibFunction() : base(1)
        {
            ParameterTypes[0] = typeof(T1);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0]);
        }
    }

    public class LiteNetLibFunction<T1, T2> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2> callback;

        protected LiteNetLibFunction() : base(2)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3> callback;

        protected LiteNetLibFunction() : base(3)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4> callback;

        protected LiteNetLibFunction() : base(4)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4, T5> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4, T5> callback;

        protected LiteNetLibFunction() : base(5)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
            ParameterTypes[4] = typeof(T5);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4, T5> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3], (T5)Parameters[4]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4, T5, T6> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4, T5, T6> callback;

        protected LiteNetLibFunction() : base(6)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
            ParameterTypes[4] = typeof(T5);
            ParameterTypes[5] = typeof(T6);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4, T5, T6> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3], (T5)Parameters[4], (T6)Parameters[5]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4, T5, T6, T7> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7> callback;

        protected LiteNetLibFunction() : base(7)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
            ParameterTypes[4] = typeof(T5);
            ParameterTypes[5] = typeof(T6);
            ParameterTypes[6] = typeof(T7);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3], (T5)Parameters[4], (T6)Parameters[5], (T7)Parameters[6]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4, T5, T6, T7, T8> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8> callback;

        protected LiteNetLibFunction() : base(8)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
            ParameterTypes[4] = typeof(T5);
            ParameterTypes[5] = typeof(T6);
            ParameterTypes[6] = typeof(T7);
            ParameterTypes[7] = typeof(T8);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3], (T5)Parameters[4], (T6)Parameters[5], (T7)Parameters[6], (T8)Parameters[7]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8, T9> callback;

        protected LiteNetLibFunction() : base(9)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
            ParameterTypes[4] = typeof(T5);
            ParameterTypes[5] = typeof(T6);
            ParameterTypes[6] = typeof(T7);
            ParameterTypes[7] = typeof(T8);
            ParameterTypes[8] = typeof(T9);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8, T9> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3], (T5)Parameters[4], (T6)Parameters[5], (T7)Parameters[6], (T8)Parameters[7], (T9)Parameters[8]);
        }
    }

    public class LiteNetLibFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : LiteNetLibFunction
    {
        private NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> callback;

        protected LiteNetLibFunction() : base(10)
        {
            ParameterTypes[0] = typeof(T1);
            ParameterTypes[1] = typeof(T2);
            ParameterTypes[2] = typeof(T3);
            ParameterTypes[3] = typeof(T4);
            ParameterTypes[4] = typeof(T5);
            ParameterTypes[5] = typeof(T6);
            ParameterTypes[6] = typeof(T7);
            ParameterTypes[7] = typeof(T8);
            ParameterTypes[8] = typeof(T9);
            ParameterTypes[9] = typeof(T10);
        }

        public LiteNetLibFunction(NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> callback) : this()
        {
            this.callback = callback;
        }

        internal override sealed void HookCallback()
        {
            callback((T1)Parameters[0], (T2)Parameters[1], (T3)Parameters[2], (T4)Parameters[3], (T5)Parameters[4], (T6)Parameters[5], (T7)Parameters[6], (T8)Parameters[7], (T9)Parameters[8], (T10)Parameters[9]);
        }
    }
    #endregion
}
