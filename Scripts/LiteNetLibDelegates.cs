﻿using Cysharp.Threading.Tasks;
using LiteNetLib.Utils;
using System;

namespace LiteNetLibManager
{
    public delegate void MessageHandlerDelegate(LiteNetLibMessageHandler messageHandler);
    public delegate void RequestProceedResultDelegate<TResponse>(AckResponseCode responseCode, TResponse response, SerializerDelegate responseExtraSerializer = null)
        where TResponse : INetSerializable;
    public delegate UniTaskVoid RequestDelegate<TRequest, TResponse>(long connectionId, NetDataReader reader, TRequest request, RequestProceedResultDelegate<TResponse> responseProceedResult)
        where TRequest : INetSerializable, new()
        where TResponse : INetSerializable, new();
    public delegate UniTaskVoid ResponseDelegate<TResponse>(long connectionId, NetDataReader reader, AckResponseCode responseCode, TResponse response)
        where TResponse : INetSerializable, new();
    public delegate void ResponseDelegate(AckResponseCode responseCode, INetSerializable response);
    public delegate void SerializerDelegate(NetDataWriter writer);
    public delegate void NetFunctionDelegate();
    public delegate void NetFunctionDelegate<T1>(T1 param1);
    public delegate void NetFunctionDelegate<T1, T2>(T1 param1, T2 param2);
    public delegate void NetFunctionDelegate<T1, T2, T3>(T1 param1, T2 param2, T3 param3);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4>(T1 param1, T2 param2, T3 param3, T4 param4);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4, T5>(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4, T5, T6>(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7>(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8>(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9);
    public delegate void NetFunctionDelegate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10);
}
