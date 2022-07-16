﻿using LiteNetLib.Utils;
using LiteNetLibManager;
using UnityEngine;

namespace MultiplayerARPG
{
    public interface IEntityMovement
    {
        BaseGameEntity Entity { get; }
        float StoppingDistance { get; }
        MovementState MovementState { get; }
        ExtraMovementState ExtraMovementState { get; }
        DirectionVector2 Direction2D { get; set; }
        float CurrentMoveSpeed { get; }
        void StopMove();
        void KeyMovement(Vector3 moveDirection, MovementState movementState);
        void PointClickMovement(Vector3 position);
        void SetExtraMovementState(ExtraMovementState extraMovementState);
        void SetLookRotation(Quaternion rotation);
        Quaternion GetLookRotation();
        void Teleport(Vector3 position, Quaternion rotation);
        bool FindGroundedPosition(Vector3 fromPosition, float findDistance, out Vector3 result);
    }

    public interface IEntityMovementComponent : IEntityMovement, IGameEntityComponent
    {
        /// <summary>
        /// Return `TRUE` if it have something written
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="shouldSendReliably"></param>
        /// <returns></returns>
        bool WriteClientState(NetDataWriter writer, out bool shouldSendReliably);
        /// <summary>
        /// Return `TRUE` if it have something written
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="shouldSendReliably"></param>
        /// <returns></returns>
        bool WriteServerState(NetDataWriter writer, out bool shouldSendReliably);
        void ReadClientStateAtServer(NetDataReader reader);
        void ReadServerStateAtClient(NetDataReader reader);
    }
}
