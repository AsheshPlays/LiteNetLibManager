﻿using System.Collections;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

namespace LiteNetLibHighLevel
{
    [DisallowMultipleComponent]
    public class LiteNetLibTransform : LiteNetLibBehaviour
    {
        private struct TransformResult
        {
            public Vector3 position;
            public Quaternion rotation;
            public float timestamp;
        }

        private class TransformResultNetField : LiteNetLibNetField<TransformResult>
        {
            public override void Deserialize(NetDataReader reader)
            {
                var result = new TransformResult();
                result.position = new Vector3((float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f);
                result.rotation = Quaternion.Euler((float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f);
                result.timestamp = (float)reader.GetShort() * 0.01f;
                Value = result;
            }

            public override bool IsValueChanged(TransformResult newValue)
            {
                return !newValue.Equals(Value);
            }

            public override void Serialize(NetDataWriter writer)
            {
                writer.Put((short)(Value.position.x * 100));
                writer.Put((short)(Value.position.y * 100));
                writer.Put((short)(Value.position.z * 100));
                writer.Put((short)(Value.rotation.eulerAngles.x * 100));
                writer.Put((short)(Value.rotation.eulerAngles.y * 100));
                writer.Put((short)(Value.rotation.eulerAngles.z * 100));
                writer.Put((short)(Value.timestamp * 100));
            }
        }

        [Range(0, 1)]
        public float sendInterval = 0.1f;
        public bool canClientSendResult;
        public float snapThreshold = 5.0f;
        public Transform TempTransform { get; private set; }
        public Rigidbody TempRigidbody3D { get; private set; }
        public Rigidbody2D TempRigidbody2D { get; private set; }
        public CharacterController TempCharacterController { get; private set; }
        // This list stores results of transform. Needed for non-owner client interpolation
        private readonly List<TransformResult> interpResults = new List<TransformResult>();
        // Interpolation related variables
        private bool isInterpolating = false;
        private float syncElapsed = 0;
        private float interpStep = 0;
        private float lastClientTimestamp = 0;
        private float lastServerTimestamp = 0;
        private TransformResult startInterpResult;
        private TransformResult lastResult;
        private TransformResult syncResult;

        private void Awake()
        {
            TempTransform = GetComponent<Transform>();
            TempRigidbody3D = GetComponent<Rigidbody>();
            TempRigidbody2D = GetComponent<Rigidbody2D>();
            TempCharacterController = GetComponent<CharacterController>();

            RegisterNetFunction("ClientSendResult", new LiteNetLibFunction<TransformResultNetField>(ClientSendResultCallback));
            RegisterNetFunction("ServerSendResult", new LiteNetLibFunction<TransformResultNetField>(ServerSendResultCallback));
        }

        private void Start()
        {
            lastResult = new TransformResult();
            lastResult.position = TempTransform.position;
            lastResult.rotation = TempTransform.rotation;
            lastResult.timestamp = Time.time;
            syncResult = lastResult;
            interpResults.Add(lastResult);
        }

        private void ClientSendResult(TransformResult result)
        {
            // Don't request to set transform if not set "canClientSendResult" to TRUE
            if (!canClientSendResult || !IsLocalClient || IsServer)
                return;
            CallNetFunction("ClientSendResult", FunctionReceivers.Server, result);
        }

        private void ServerSendResult(TransformResult result)
        {
            CallNetFunction("ServerSendResult", FunctionReceivers.All, result);
        }

        private void ClientSendResultCallback(TransformResultNetField resultParam)
        {
            // Don't update transform follow client's request if not set "canClientSendResult" to TRUE or it's the server
            if (!canClientSendResult || IsLocalClient)
                return;
            var result = resultParam.Value;
            // Discard out of order results
            if (result.timestamp <= lastClientTimestamp)
                return;
            lastClientTimestamp = result.timestamp;
            // Adding results to the results list so they can be used in interpolation process
            result.timestamp = Time.time;
            interpResults.Add(result);
        }

        private void ServerSendResultCallback(TransformResultNetField resultParam)
        {
            // Update transform only non-owner client
            if (IsLocalClient || IsServer)
                return;
            var result = resultParam.Value;
            // Discard out of order results
            if (result.timestamp <= lastServerTimestamp)
                return;
            lastServerTimestamp = result.timestamp;
            // Adding results to the results list so they can be used in interpolation process
            result.timestamp = Time.time;
            interpResults.Add(result);
        }

        private void FixedUpdate()
        {
            // Sending transform to all clients
            if (IsServer)
            {
                if (syncElapsed >= sendInterval)
                {
                    // Send transform to clients only when there are changes on transform
                    if (ShouldSyncResult())
                        ServerSendResult(syncResult);
                    syncElapsed = 0;
                }
                syncElapsed += Time.fixedDeltaTime;
                // Interpolate transform that receives from clients
                if (canClientSendResult && !IsLocalClient)
                    Interpolate();
            }
            // Sending client transform result to server
            else if (canClientSendResult && IsLocalClient)
            {
                if (syncElapsed >= sendInterval)
                {
                    // Send transform to server only when there are changes on transform
                    if (ShouldSyncResult())
                        ClientSendResult(syncResult);
                    syncElapsed = 0;
                }
                syncElapsed += Time.fixedDeltaTime;
            }
            // Interpolating results for non-owner client objects on clients
            else if (!canClientSendResult || !IsLocalClient)
                Interpolate();
        }

        private void Interpolate()
        {
            // There should be at least two records in the results list to interpolate between them
            // And continue interpolating when there is one record left
            if (interpResults.Count == 0)
                isInterpolating = false;

            if (interpResults.Count >= 2)
                isInterpolating = true;

            if (isInterpolating)
            {
                var lastInterpResult = interpResults[interpResults.Count - 1];
                if (!ShouldSnap(lastInterpResult.position))
                {
                    if (interpStep == 0)
                        startInterpResult = lastResult;

                    float step = 1f / sendInterval;

                    lastResult.position = Vector3.Lerp(startInterpResult.position, interpResults[0].position, interpStep);
                    lastResult.rotation = Quaternion.Slerp(startInterpResult.rotation, interpResults[0].rotation, interpStep);

                    if (TempRigidbody3D != null)
                        InterpolateRigibody3D();
                    else if (TempRigidbody2D != null)
                        InterpolateRigibody2D();
                    else if (TempCharacterController != null)
                        InterpolateCharacterController();
                    else
                        InterpolateTransform();

                    interpStep += step * Time.fixedDeltaTime;
                    if (interpStep >= 1)
                    {
                        interpStep = 0;
                        interpResults.RemoveAt(0);
                    }
                }
                else
                {
                    lastResult.position = lastInterpResult.position;
                    lastResult.rotation = lastInterpResult.rotation;
                    Snap();
                    interpResults.Clear();
                    interpStep = 0;
                }
            }
        }

        private bool ShouldSyncResult()
        {
            if (syncResult.position != TempTransform.position || syncResult.rotation != TempTransform.rotation)
            {
                syncResult.position = TempTransform.position;
                syncResult.rotation = TempTransform.rotation;
                syncResult.timestamp = Time.time;
                return true;
            }
            return false;
        }

        private bool ShouldSnap(Vector3 targetPosition)
        {
            var dist = 0f;
            if (TempRigidbody3D != null)
                dist = (TempRigidbody3D.position - targetPosition).magnitude;
            else if (TempRigidbody2D != null)
                dist = (TempRigidbody2D.position - new Vector2(targetPosition.x, targetPosition.y)).magnitude;
            else if (TempCharacterController != null)
                dist = (TempTransform.position - targetPosition).magnitude;
            return dist > snapThreshold;
        }

        private void Snap()
        {
            if (TempRigidbody3D != null)
            {
                TempRigidbody3D.position = lastResult.position;
                TempRigidbody3D.rotation = lastResult.rotation;
            }
            else if (TempRigidbody2D != null)
            {
                TempRigidbody2D.position = lastResult.position;
                TempRigidbody2D.rotation = lastResult.rotation.eulerAngles.z;
            }
            else if (TempCharacterController != null)
            {
                TempTransform.position = lastResult.position;
                TempTransform.rotation = lastResult.rotation;
            }
            else
            {
                TempTransform.position = lastResult.position;
                TempTransform.rotation = lastResult.rotation;
            }
        }

        private void InterpolateRigibody3D()
        {
            TempRigidbody3D.MovePosition(lastResult.position);
            TempRigidbody3D.MoveRotation(lastResult.rotation);
        }

        private void InterpolateRigibody2D()
        {
            TempRigidbody2D.MovePosition(lastResult.position);
            TempRigidbody2D.MoveRotation(lastResult.rotation.eulerAngles.z);
        }

        private void InterpolateCharacterController()
        {
            TempCharacterController.Move(lastResult.position - TempTransform.position);
            TempTransform.rotation = lastResult.rotation;
        }

        private void InterpolateTransform()
        {
            TempTransform.position = lastResult.position;
            TempTransform.rotation = lastResult.rotation;
        }
    }
}
