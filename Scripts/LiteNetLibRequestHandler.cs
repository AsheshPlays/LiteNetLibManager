﻿using System;
using LiteNetLib.Utils;

namespace LiteNetLibManager
{
    public abstract class LiteNetLibRequestHandler
    {
        internal abstract void InvokeRequest(long connectionId, NetDataReader reader, out AckResponseCode responseCode, out INetSerializable response, out Action<NetDataWriter> extraSerializer);
        internal abstract void InvokeResponse(long connectionId, NetDataReader reader, AckResponseCode responseCode);
        internal abstract bool IsRequestTypeValid(Type type);
    }

    public class LiteNetLibRequestHandler<TRequest, TResponse> : LiteNetLibRequestHandler
        where TRequest : INetSerializable, new()
        where TResponse : INetSerializable, new()
    {
        private RequestDelegate<TRequest, TResponse> requestDelegate;
        private ResponseDelegate<TResponse> responseDelegate;

        public LiteNetLibRequestHandler(
            RequestDelegate<TRequest, TResponse> requestDelegate,
            ResponseDelegate<TResponse> responseDelegate)
        {
            this.requestDelegate = requestDelegate;
            this.responseDelegate = responseDelegate;
        }

        internal override void InvokeRequest(long connectionId, NetDataReader reader, out AckResponseCode responseCode, out INetSerializable outResponse, out Action<NetDataWriter> extraSerializer)
        {
            TRequest request = new TRequest();
            if (reader != null)
                request.Deserialize(reader);
            TResponse response;
            requestDelegate.Invoke(connectionId, reader, request, out responseCode, out response, out extraSerializer);
            outResponse = response;
        }

        internal override void InvokeResponse(long connectionId, NetDataReader reader, AckResponseCode responseCode)
        {
            TResponse response = new TResponse();
            if (reader != null)
                response.Deserialize(reader);
            responseDelegate.Invoke(connectionId, reader, responseCode, response);
        }

        internal override bool IsRequestTypeValid(Type type)
        {
            return typeof(TRequest) == type;
        }
    }
}
