﻿using LiteNetLib.Utils;
using LiteNetLibManager;
using StandardAssets.Characters.Physics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MultiplayerARPG
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(OpenCharacterController))]
    public class RigidBodyEntityMovement : BaseNetworkedGameEntityComponent<BaseGameEntity>, IEntityMovementComponent
    {
        protected static readonly RaycastHit[] findGroundRaycastHits = new RaycastHit[25];

        [Header("Movement AI")]
        [Range(0.01f, 1f)]
        public float stoppingDistance = 0.1f;

        [Header("Movement Settings")]
        public float jumpHeight = 2f;
        public ApplyJumpForceMode applyJumpForceMode = ApplyJumpForceMode.ApplyImmediately;
        public float applyJumpForceFixedDuration;
        public float backwardMoveSpeedRate = 0.75f;
        public float gravity = 9.81f;
        public float maxFallVelocity = 40f;
        public LayerMask platformLayerMask = 1;
        [Tooltip("Delay before character change from grounded state to airborne")]
        public float airborneDelay = 0.01f;
        public bool doNotChangeVelocityWhileAirborne;
        public float landedPauseMovementDuration = 0f;
        public float beforeCrawlingPauseMovementDuration = 0f;
        public float afterCrawlingPauseMovementDuration = 0f;
        [Range(0.1f, 1f)]
        public float underWaterThreshold = 0.75f;
        public bool autoSwimToSurface;

        [Header("Root Motion Settings")]
        public bool useRootMotionForMovement;
        public bool useRootMotionForAirMovement;
        public bool useRootMotionForJump;
        public bool useRootMotionForFall;
        public bool useRootMotionWhileNotMoving;
        public bool useRootMotionUnderWater;

        [Header("Networking Settings")]
        public float snapThreshold = 5.0f;

        public Animator CacheAnimator { get; private set; }
        public Rigidbody CacheRigidbody { get; private set; }
        public CapsuleCollider CacheCapsuleCollider { get; private set; }
        public OpenCharacterController CacheOpenCharacterController { get; private set; }
        public float StoppingDistance
        {
            get { return stoppingDistance; }
        }
        public MovementState MovementState { get; protected set; }
        public ExtraMovementState ExtraMovementState { get; protected set; }
        public DirectionVector2 Direction2D { get { return Vector2.down; } set { } }
        public float CurrentMoveSpeed { get; private set; }

        public Queue<Vector3> NavPaths { get; private set; }
        public bool HasNavPaths
        {
            get { return NavPaths != null && NavPaths.Count > 0; }
        }

        // Movement codes
        protected float airborneElapsed;
        protected bool isUnderWater;
        protected bool isJumping;
        protected bool applyingJumpForce;
        protected float applyJumpForceCountDown;
        protected Collider waterCollider;
        protected float yRotation;
        protected Transform groundedTransform;
        protected Vector3 groundedLocalPosition;
        protected Vector3 oldGroundedPosition;
        protected long acceptedPositionTimestamp;
        protected Vector3? clientTargetPosition;
        protected float? targetYRotation;
        protected float yRotateLerpTime;
        protected float yRotateLerpDuration;
        protected bool acceptedJump;
        protected bool sendingJump;
        protected float lastServerValidateTransformTime;
        protected float lastServerValidateTransformMoveSpeed;
        protected EntityMovementInput oldInput;
        protected EntityMovementInput currentInput;
        protected MovementState tempMovementState;
        protected ExtraMovementState tempExtraMovementState;
        protected Vector3 inputDirection;
        protected Vector3 moveDirection;
        protected float verticalVelocity;
        protected float? lagMoveSpeedRate;
        protected Vector3 velocityBeforeAirborne;
        protected CollisionFlags collisionFlags;
        protected float pauseMovementCountDown;
        protected bool previouslyGrounded;
        protected bool previouslyAirborne;
        protected ExtraMovementState previouslyExtraMovementState;
        protected bool isTeleporting;

        public override void EntityAwake()
        {
            // Prepare animator component
            CacheAnimator = GetComponent<Animator>();
            // Prepare rigidbody component
            CacheRigidbody = gameObject.GetOrAddComponent<Rigidbody>();
            // Prepare collider component
            CacheCapsuleCollider = gameObject.GetOrAddComponent<CapsuleCollider>();
            // Prepare open character controller
            float radius = CacheCapsuleCollider.radius;
            float height = CacheCapsuleCollider.height;
            Vector3 center = CacheCapsuleCollider.center;
            CacheOpenCharacterController = gameObject.GetOrAddComponent<OpenCharacterController>((comp) =>
            {
                comp.SetRadiusHeightAndCenter(radius, height, center, true, true);
            });
            CacheOpenCharacterController.collision += OnCharacterControllerCollision;
            // Disable unused component
            LiteNetLibTransform disablingComp = gameObject.GetComponent<LiteNetLibTransform>();
            if (disablingComp != null)
            {
                Logging.LogWarning("RigidBodyEntityMovement", "You can remove `LiteNetLibTransform` component from game entity, it's not being used anymore [" + name + "]");
                disablingComp.enabled = false;
            }
            // Setup
            StopMoveFunction();
        }

        public override void EntityStart()
        {
            yRotation = CacheTransform.eulerAngles.y;
            CacheOpenCharacterController.SetPosition(CacheTransform.position, true);
            verticalVelocity = 0;
        }

        public override void ComponentOnEnable()
        {
            CacheOpenCharacterController.enabled = true;
            CacheOpenCharacterController.SetPosition(CacheTransform.position, true);
            verticalVelocity = 0;
        }

        public override void ComponentOnDisable()
        {
            CacheOpenCharacterController.enabled = false;
        }

        public override void EntityOnDestroy()
        {
            base.EntityOnDestroy();
            CacheOpenCharacterController.collision -= OnCharacterControllerCollision;
        }

        public override void OnSetOwnerClient(bool isOwnerClient)
        {
            base.OnSetOwnerClient(isOwnerClient);
            clientTargetPosition = null;
            NavPaths = null;
        }

        private void OnAnimatorMove()
        {
            if (!CacheAnimator)
                return;

            if (useRootMotionWhileNotMoving &&
                !MovementState.Has(MovementState.Forward) &&
                !MovementState.Has(MovementState.Backward) &&
                !MovementState.Has(MovementState.Left) &&
                !MovementState.Has(MovementState.Right) &&
                !MovementState.Has(MovementState.IsJump))
            {
                // No movement, apply root motion position / rotation
                CacheAnimator.ApplyBuiltinRootMotion();
                return;
            }

            if (MovementState.Has(MovementState.IsGrounded) && useRootMotionForMovement)
                CacheAnimator.ApplyBuiltinRootMotion();
            if (!MovementState.Has(MovementState.IsGrounded) && useRootMotionForAirMovement)
                CacheAnimator.ApplyBuiltinRootMotion();
            if (MovementState.Has(MovementState.IsUnderWater) && useRootMotionUnderWater)
                CacheAnimator.ApplyBuiltinRootMotion();
        }

        public void StopMove()
        {
            if (Entity.MovementSecure == MovementSecure.ServerAuthoritative)
            {
                // Send movement input to server, then server will apply movement and sync transform to clients
                this.SetInputStop(currentInput);
            }
            StopMoveFunction();
        }

        private void StopMoveFunction()
        {
            NavPaths = null;
        }

        public void KeyMovement(Vector3 moveDirection, MovementState movementState)
        {
            if (!Entity.CanMove())
                return;
            if (this.CanPredictMovement())
            {
                // Always apply movement to owner client (it's client prediction for server auth movement)
                inputDirection = moveDirection;
                tempMovementState = movementState;
                if (inputDirection.sqrMagnitude > 0)
                    NavPaths = null;
                if (!isJumping && !applyingJumpForce)
                    isJumping = CacheOpenCharacterController.isGrounded && tempMovementState.Has(MovementState.IsJump);
            }
        }

        public void PointClickMovement(Vector3 position)
        {
            if (!Entity.CanMove())
                return;
            if (this.CanPredictMovement())
            {
                // Always apply movement to owner client (it's client prediction for server auth movement)
                SetMovePaths(position, true);
            }
        }

        public void SetExtraMovementState(ExtraMovementState extraMovementState)
        {
            if (!Entity.CanMove())
                return;
            if (this.CanPredictMovement())
            {
                // Always apply movement to owner client (it's client prediction for server auth movement)
                tempExtraMovementState = extraMovementState;
            }
        }

        public void SetLookRotation(Quaternion rotation)
        {
            if (!Entity.CanMove())
                return;
            if (this.CanPredictMovement())
            {
                // Always apply movement to owner client (it's client prediction for server auth movement)
                if (!HasNavPaths)
                    yRotation = rotation.eulerAngles.y;
            }
        }

        public Quaternion GetLookRotation()
        {
            return Quaternion.Euler(0f, yRotation, 0f);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (!IsServer)
            {
                Logging.LogWarning("RigidBodyEntityMovement", "Teleport function shouldn't be called at client [" + name + "]");
                return;
            }
            isTeleporting = true;
            OnTeleport(position, rotation.eulerAngles.y);
        }

        public bool FindGroundedPosition(Vector3 fromPosition, float findDistance, out Vector3 result)
        {
            return PhysicUtils.FindGroundedPosition(fromPosition, findGroundRaycastHits, findDistance, GameInstance.Singleton.GetGameEntityGroundDetectionLayerMask(), out result, CacheTransform);
        }

        public override void EntityUpdate()
        {
            UpdateMovement(Time.deltaTime);
            if (IsOwnerClient || (IsServer && Entity.MovementSecure == MovementSecure.ServerAuthoritative))
            {
                // Update movement state
                tempMovementState = moveDirection.sqrMagnitude > 0f ? tempMovementState : MovementState.None;
                if (isUnderWater)
                    tempMovementState |= MovementState.IsUnderWater;
                if (CacheOpenCharacterController.isGrounded || airborneElapsed < airborneDelay)
                    tempMovementState |= MovementState.IsGrounded;
                // Update movement state
                MovementState = tempMovementState;
                // Update extra movement state
                ExtraMovementState = this.ValidateExtraMovementState(MovementState, tempExtraMovementState);
            }
            else
            {
                // Update movement state
                if (HasNavPaths && !MovementState.Has(MovementState.Forward))
                    MovementState |= MovementState.Forward;
            }
        }

        private void WaterCheck()
        {
            if (waterCollider == null)
            {
                // Not in water
                isUnderWater = false;
                return;
            }
            float footToSurfaceDist = waterCollider.bounds.max.y - CacheCapsuleCollider.bounds.min.y;
            float currentThreshold = footToSurfaceDist / (CacheCapsuleCollider.bounds.max.y - CacheCapsuleCollider.bounds.min.y);
            isUnderWater = currentThreshold >= underWaterThreshold;
        }

        private void UpdateMovement(float deltaTime)
        {
            float tempSqrMagnitude;
            float tempPredictSqrMagnitude;
            float tempTargetDistance;
            float tempEntityMoveSpeed;
            float tempMaxMoveSpeed;
            Vector3 tempHorizontalMoveDirection;
            Vector3 tempMoveVelocity;
            Vector3 tempCurrentPosition;
            Vector3 tempTargetPosition;
            Vector3 tempPredictPosition;

            tempCurrentPosition = CacheTransform.position;
            tempMoveVelocity = Vector3.zero;
            moveDirection = Vector3.zero;
            tempTargetDistance = 0f;
            WaterCheck();

            bool isGrounded = CacheOpenCharacterController.isGrounded;
            bool isAirborne = !isGrounded && !isUnderWater && airborneElapsed >= airborneDelay;

            // Update airborne elasped
            if (CacheOpenCharacterController.isGrounded)
                airborneElapsed = 0f;
            else
                airborneElapsed += deltaTime;

            if (HasNavPaths)
            {
                // Set `tempTargetPosition` and `tempCurrentPosition`
                tempTargetPosition = NavPaths.Peek();
                moveDirection = (tempTargetPosition - tempCurrentPosition).normalized;
                tempTargetDistance = Vector3.Distance(tempTargetPosition.GetXZ(), tempCurrentPosition.GetXZ());
                if (!tempMovementState.Has(MovementState.Forward))
                    tempMovementState |= MovementState.Forward;
                if (tempTargetDistance < StoppingDistance)
                {
                    NavPaths.Dequeue();
                    if (!HasNavPaths)
                    {
                        StopMoveFunction();
                        moveDirection = Vector3.zero;
                    }
                }
                else
                {
                    // Turn character to destination
                    yRotation = Quaternion.LookRotation(moveDirection).eulerAngles.y;
                    targetYRotation = null;
                }
            }
            else if (clientTargetPosition.HasValue)
            {
                tempTargetPosition = clientTargetPosition.Value;
                moveDirection = (tempTargetPosition - tempCurrentPosition).normalized;
                tempTargetDistance = Vector3.Distance(tempTargetPosition, tempCurrentPosition);
                if (tempTargetDistance < 0.001f)
                {
                    clientTargetPosition = null;
                    StopMoveFunction();
                    moveDirection = Vector3.zero;
                }
            }
            else if (inputDirection.sqrMagnitude > 0f)
            {
                moveDirection = inputDirection.normalized;
                tempTargetPosition = tempCurrentPosition + moveDirection;
            }
            else
            {
                tempTargetPosition = tempCurrentPosition;
            }

            if (!Entity.CanMove())
            {
                moveDirection = Vector3.zero;
                isJumping = false;
                applyingJumpForce = false;
            }

            // Prepare movement speed
            tempEntityMoveSpeed = applyingJumpForce ? 0f : Entity.GetMoveSpeed();
            tempMaxMoveSpeed = tempEntityMoveSpeed;

            // Calculate vertical velocity by gravity
            if (!isGrounded && !isUnderWater)
            {
                if (!useRootMotionForFall)
                    verticalVelocity = Mathf.MoveTowards(verticalVelocity, -maxFallVelocity, gravity * deltaTime);
                else
                    verticalVelocity = 0f;
            }
            else
            {
                // Not falling set verical velocity to 0
                verticalVelocity = 0f;
            }

            // Jumping 
            if (acceptedJump || (pauseMovementCountDown <= 0f && isGrounded && isJumping && !CacheOpenCharacterController.startedSlide))
            {
                currentInput = this.SetInputJump(currentInput);
                sendingJump = true;
                airborneElapsed = airborneDelay;
                Entity.PlayJumpAnimation();
                applyingJumpForce = true;
                applyJumpForceCountDown = 0f;
                switch (applyJumpForceMode)
                {
                    case ApplyJumpForceMode.ApplyAfterFixedDuration:
                        applyJumpForceCountDown = applyJumpForceFixedDuration;
                        break;
                    case ApplyJumpForceMode.ApplyAfterJumpDuration:
                        if (Entity.Model is IJumppableModel)
                            applyJumpForceCountDown = (Entity.Model as IJumppableModel).GetJumpAnimationDuration();
                        break;
                }
            }

            if (applyingJumpForce)
            {
                applyJumpForceCountDown -= Time.deltaTime;
                if (applyJumpForceCountDown <= 0f)
                {
                    applyingJumpForce = false;
                    if (!useRootMotionForJump)
                        verticalVelocity = CalculateJumpVerticalSpeed();
                }
            }

            // Updating horizontal movement (WASD inputs)
            if (!isAirborne)
            {
                velocityBeforeAirborne = Vector3.zero;
            }
            if (pauseMovementCountDown <= 0f && moveDirection.sqrMagnitude > 0f && (!isAirborne || !doNotChangeVelocityWhileAirborne))
            {
                // Calculate only horizontal move direction
                tempHorizontalMoveDirection = moveDirection;
                tempHorizontalMoveDirection.y = 0;
                tempHorizontalMoveDirection.Normalize();

                // If character move backward
                if (Vector3.Angle(tempHorizontalMoveDirection, CacheTransform.forward) > 120)
                    tempMaxMoveSpeed *= backwardMoveSpeedRate;
                CurrentMoveSpeed = CalculateCurrentMoveSpeed(tempMaxMoveSpeed, deltaTime);

                // NOTE: `tempTargetPosition` and `tempCurrentPosition` were set above
                tempSqrMagnitude = (tempTargetPosition - tempCurrentPosition).sqrMagnitude;
                tempPredictPosition = tempCurrentPosition + (tempHorizontalMoveDirection * CurrentMoveSpeed * deltaTime);
                tempPredictSqrMagnitude = (tempPredictPosition - tempCurrentPosition).sqrMagnitude;
                if (HasNavPaths || clientTargetPosition.HasValue)
                {
                    // Check `tempSqrMagnitude` against the `tempPredictSqrMagnitude`
                    // if `tempPredictSqrMagnitude` is greater than `tempSqrMagnitude`,
                    // rigidbody will reaching target and character is moving pass it,
                    // so adjust move speed by distance and time (with physic formula: v=s/t)
                    if (tempPredictSqrMagnitude >= tempSqrMagnitude)
                        CurrentMoveSpeed *= tempTargetDistance / deltaTime / CurrentMoveSpeed;
                }
                tempMoveVelocity = tempHorizontalMoveDirection * CurrentMoveSpeed;
                velocityBeforeAirborne = tempMoveVelocity;
                // Set inputs
                currentInput = this.SetInputMovementState(currentInput, tempMovementState);
                if (HasNavPaths)
                {
                    currentInput = this.SetInputPosition(currentInput, tempTargetPosition);
                    currentInput = this.SetInputIsKeyMovement(currentInput, false);
                }
                else
                {
                    currentInput = this.SetInputPosition(currentInput, tempPredictPosition);
                    currentInput = this.SetInputIsKeyMovement(currentInput, true);
                }
            }
            // Moves by velocity before airborne
            if (isGrounded && previouslyAirborne)
            {
                pauseMovementCountDown = landedPauseMovementDuration;
            }
            else if (isGrounded && previouslyExtraMovementState != ExtraMovementState.IsCrawling && tempExtraMovementState == ExtraMovementState.IsCrawling)
            {
                pauseMovementCountDown = beforeCrawlingPauseMovementDuration;
            }
            else if (isGrounded && previouslyExtraMovementState == ExtraMovementState.IsCrawling && tempExtraMovementState != ExtraMovementState.IsCrawling)
            {
                pauseMovementCountDown = afterCrawlingPauseMovementDuration;
            }
            else if (isAirborne && doNotChangeVelocityWhileAirborne)
            {
                tempMoveVelocity = velocityBeforeAirborne;
            }
            else
            {
                if (pauseMovementCountDown > 0f)
                    pauseMovementCountDown -= deltaTime;
            }
            if (pauseMovementCountDown > 0f)
            {
                // Remove movement from movestate while pausing movement
                tempMovementState ^= MovementState.Forward | MovementState.Backward | MovementState.Right | MovementState.Right;
            }
            // Updating vertical movement (Fall, WASD inputs under water)
            if (isUnderWater)
            {
                CurrentMoveSpeed = CalculateCurrentMoveSpeed(tempMaxMoveSpeed, deltaTime);

                // Move up to surface while under water
                if (autoSwimToSurface || Mathf.Abs(moveDirection.y) > 0)
                {
                    if (autoSwimToSurface)
                        moveDirection.y = 1f;
                    tempTargetPosition = Vector3.up * (waterCollider.bounds.max.y - (CacheCapsuleCollider.bounds.size.y * underWaterThreshold));
                    tempCurrentPosition = Vector3.up * CacheTransform.position.y;
                    tempTargetDistance = Vector3.Distance(tempTargetPosition, tempCurrentPosition);
                    tempSqrMagnitude = (tempTargetPosition - tempCurrentPosition).sqrMagnitude;
                    tempPredictPosition = tempCurrentPosition + (Vector3.up * moveDirection.y * CurrentMoveSpeed * deltaTime);
                    tempPredictSqrMagnitude = (tempPredictPosition - tempCurrentPosition).sqrMagnitude;
                    // Check `tempSqrMagnitude` against the `tempPredictSqrMagnitude`
                    // if `tempPredictSqrMagnitude` is greater than `tempSqrMagnitude`,
                    // rigidbody will reaching target and character is moving pass it,
                    // so adjust move speed by distance and time (with physic formula: v=s/t)
                    if (tempPredictSqrMagnitude >= tempSqrMagnitude)
                        CurrentMoveSpeed *= tempTargetDistance / deltaTime / CurrentMoveSpeed;
                    // Swim up to surface
                    if (CurrentMoveSpeed < 0.01f)
                        moveDirection.y = 0f;
                    tempMoveVelocity.y = moveDirection.y * CurrentMoveSpeed;
                    if (!HasNavPaths)
                        currentInput = this.SetInputYPosition(currentInput, tempPredictPosition.y);
                }
            }
            else
            {
                // Update velocity while not under water
                tempMoveVelocity.y = verticalVelocity;
            }

            // Don't applies velocity while using root motion
            if ((isGrounded && useRootMotionForMovement) ||
                (isAirborne && useRootMotionForAirMovement) ||
                (isUnderWater && useRootMotionUnderWater))
            {
                tempMoveVelocity.x = 0;
                tempMoveVelocity.z = 0;
            }

            Vector3 platformMotion = Vector3.zero;
            if (isGrounded && !isUnderWater)
            {
                // Apply platform motion
                if (groundedTransform != null && deltaTime > 0.0f)
                {
                    Vector3 newGroundedPosition = groundedTransform.TransformPoint(groundedLocalPosition);
                    platformMotion = (newGroundedPosition - oldGroundedPosition) / deltaTime;
                    platformMotion.y = 0;
                    oldGroundedPosition = newGroundedPosition;
                }
            }

            collisionFlags = CacheOpenCharacterController.Move((tempMoveVelocity + platformMotion) * deltaTime);

            if (targetYRotation.HasValue)
            {
                yRotateLerpTime += deltaTime;
                float lerpTimeRate = yRotateLerpTime / yRotateLerpDuration;
                yRotation = Mathf.LerpAngle(yRotation, targetYRotation.Value, lerpTimeRate);
                if (lerpTimeRate >= 1f)
                    targetYRotation = null;
            }
            UpdateRotation();
            currentInput = this.SetInputRotation(currentInput, CacheTransform.rotation);
            isJumping = false;
            acceptedJump = false;
            previouslyGrounded = isGrounded;
            previouslyAirborne = isAirborne;
            previouslyExtraMovementState = tempExtraMovementState;
        }

        private float CalculateCurrentMoveSpeed(float maxMoveSpeed, float deltaTime)
        {
            // Adjust speed by rtt
            if (!IsServer && IsOwnerClient && Entity.MovementSecure == MovementSecure.ServerAuthoritative)
            {
                float rtt = 0.001f * Entity.Manager.Rtt;
                float acc = 1f / rtt * deltaTime * 0.5f;
                if (!lagMoveSpeedRate.HasValue)
                    lagMoveSpeedRate = 0f;
                if (lagMoveSpeedRate < 1f)
                    lagMoveSpeedRate += acc;
                if (lagMoveSpeedRate > 1f)
                    lagMoveSpeedRate = 1f;
                return maxMoveSpeed * lagMoveSpeedRate.Value;
            }
            // TODO: Adjust other's client move speed by rtt
            return maxMoveSpeed;
        }

        private void UpdateRotation()
        {
            CacheTransform.eulerAngles = new Vector3(0f, yRotation, 0f);
        }

        private void SetMovePaths(Vector3 position, bool useNavMesh)
        {
            if (useNavMesh)
            {
                NavMeshPath navPath = new NavMeshPath();
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(position, out navHit, 5f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(CacheTransform.position, navHit.position, NavMesh.AllAreas, navPath))
                {
                    NavPaths = new Queue<Vector3>(navPath.corners);
                    // Dequeue first path it's not require for future movement
                    NavPaths.Dequeue();
                }
            }
            else
            {
                // If not use nav mesh, just move to position by direction
                NavPaths = new Queue<Vector3>();
                NavPaths.Enqueue(position);
            }
        }

        private float CalculateJumpVerticalSpeed()
        {
            // From the jump height and gravity we deduce the upwards speed 
            // for the character to reach at the apex.
            return Mathf.Sqrt(2f * jumpHeight * gravity);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == PhysicLayers.Water)
            {
                // Enter water
                waterCollider = other;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == PhysicLayers.Water)
            {
                // Exit water
                waterCollider = null;
            }
        }

        private void OnCharacterControllerCollision(OpenCharacterController.CollisionInfo info)
        {
            if (CacheOpenCharacterController.isGrounded)
            {
                if (platformLayerMask == (platformLayerMask | (1 << info.gameObject.layer)))
                {
                    groundedTransform = info.collider.transform;
                    oldGroundedPosition = info.point;
                    groundedLocalPosition = groundedTransform.InverseTransformPoint(oldGroundedPosition);
                }
                else
                {
                    groundedTransform = null;
                }
            }
        }

        public bool WriteClientState(NetDataWriter writer, out bool shouldSendReliably)
        {
            shouldSendReliably = false;
            if (Entity.MovementSecure == MovementSecure.NotSecure && IsOwnerClient && !IsServer)
            {
                // Sync transform from owner client to server (except it's both owner client and server)
                if (sendingJump)
                {
                    shouldSendReliably = true;
                    MovementState |= MovementState.IsJump;
                }
                else
                {
                    MovementState &= ~MovementState.IsJump;
                }
                this.ClientWriteSyncTransform3D(writer);
                sendingJump = false;
                return true;
            }
            if (Entity.MovementSecure == MovementSecure.ServerAuthoritative && IsOwnerClient && !IsServer)
            {
                EntityMovementInputState inputState;
                if (this.DifferInputEnoughToSend(oldInput, currentInput, out inputState))
                {
                    currentInput = this.SetInputExtraMovementState(currentInput, tempExtraMovementState);
                    this.ClientWriteMovementInput3D(writer, inputState, currentInput.MovementState, currentInput.ExtraMovementState, currentInput.Position, currentInput.Rotation);
                    oldInput = currentInput;
                    currentInput = null;
                    return true;
                }
            }
            return false;
        }

        public bool WriteServerState(NetDataWriter writer, out bool shouldSendReliably)
        {
            shouldSendReliably = false;
            // Sync transform from server to all clients (include owner client)
            if (sendingJump)
            {
                shouldSendReliably = true;
                MovementState |= MovementState.IsJump;
            }
            else
            {
                MovementState &= ~MovementState.IsJump;
            }
            if (isTeleporting)
            {
                shouldSendReliably = true;
                MovementState |= MovementState.IsTeleport;
            }
            else
            {
                MovementState &= ~MovementState.IsTeleport;
            }
            this.ServerWriteSyncTransform3D(writer);
            sendingJump = false;
            isTeleporting = false;
            return true;
        }

        public void ReadClientStateAtServer(NetDataReader reader)
        {
            switch (Entity.MovementSecure)
            {
                case MovementSecure.NotSecure:
                    ReadSyncTransformAtServer(reader);
                    break;
                case MovementSecure.ServerAuthoritative:
                    ReadMovementInputAtServer(reader);
                    break;
            }
        }

        public void ReadServerStateAtClient(NetDataReader reader)
        {
            if (IsServer)
            {
                // Don't read and apply transform, because it was done at server
                return;
            }
            MovementState movementState;
            ExtraMovementState extraMovementState;
            Vector3 position;
            float yAngle;
            long timestamp;
            reader.ReadSyncTransformMessage3D(out movementState, out extraMovementState, out position, out yAngle, out timestamp);
            if (Mathf.Abs(timestamp - BaseGameNetworkManager.Singleton.ServerTimestamp) > System.TimeSpan.TicksPerMillisecond)
            {
                // Timestamp is a lot difference to server's timestamp, player might try to hack a game or packet may corrupted occurring, so skip it
                return;
            }
            if (acceptedPositionTimestamp < timestamp)
            {
                // Snap character to the position if character is too far from the position
                if (movementState.Has(MovementState.IsTeleport))
                {
                    OnTeleport(position, yAngle);
                }
                else if (Vector3.Distance(position, CacheTransform.position) >= snapThreshold)
                {
                    if (Entity.MovementSecure == MovementSecure.ServerAuthoritative || !IsOwnerClient)
                    {
                        yRotation = yAngle;
                        CacheOpenCharacterController.SetPosition(position, false);
                    }
                    MovementState = movementState;
                    ExtraMovementState = extraMovementState;
                }
                else if (!IsOwnerClient)
                {
                    targetYRotation = yAngle;
                    yRotateLerpTime = 0;
                    yRotateLerpDuration = Time.fixedDeltaTime;
                    clientTargetPosition = position;
                    MovementState = movementState;
                    ExtraMovementState = extraMovementState;
                }
                acceptedPositionTimestamp = timestamp;
            }
            if (!IsOwnerClient && movementState.Has(MovementState.IsJump))
                acceptedJump = true;
        }

        public void ReadMovementInputAtServer(NetDataReader reader)
        {
            if (IsOwnerClient)
            {
                // Don't read and apply inputs, because it was done (this is both owner client and server)
                return;
            }
            if (Entity.MovementSecure == MovementSecure.NotSecure)
            {
                // Movement handling at client, so don't read movement inputs from client (but have to read transform)
                return;
            }
            if (!Entity.CanMove())
                return;
            EntityMovementInputState inputState;
            MovementState movementState;
            ExtraMovementState extraMovementState;
            Vector3 position;
            float yAngle;
            long timestamp;
            reader.ReadMovementInputMessage3D(out inputState, out movementState, out extraMovementState, out position, out yAngle, out timestamp);
            if (Mathf.Abs(timestamp - BaseGameNetworkManager.Singleton.ServerTimestamp) > System.TimeSpan.TicksPerMillisecond)
            {
                // Timestamp is a lot difference to server's timestamp, player might try to hack a game or packet may corrupted occurring, so skip it
                return;
            }
            if (acceptedPositionTimestamp < timestamp)
            {
                if (!inputState.Has(EntityMovementInputState.IsStopped))
                {
                    NavPaths = null;
                    tempMovementState = movementState;
                    tempExtraMovementState = extraMovementState;
                    clientTargetPosition = null;
                    targetYRotation = null;
                    if (inputState.Has(EntityMovementInputState.PositionChanged))
                    {
                        if (inputState.Has(EntityMovementInputState.IsKeyMovement))
                        {
                            clientTargetPosition = position;
                        }
                        else
                        {
                            SetMovePaths(position, true);
                        }
                    }
                    if (inputState.Has(EntityMovementInputState.RotationChanged))
                    {
                        if (IsClient)
                        {
                            targetYRotation = yAngle;
                            yRotateLerpTime = 0;
                            yRotateLerpDuration = Time.fixedDeltaTime;
                        }
                        else
                        {
                            yRotation = yAngle;
                        }
                    }
                    isJumping = inputState.Has(EntityMovementInputState.IsJump);
                }
                else
                {
                    StopMoveFunction();
                }
                acceptedPositionTimestamp = timestamp;
            }
        }

        public void ReadSyncTransformAtServer(NetDataReader reader)
        {
            if (IsOwnerClient)
            {
                // Don't read and apply transform, because it was done (this is both owner client and server)
                return;
            }
            if (Entity.MovementSecure == MovementSecure.ServerAuthoritative)
            {
                // Movement handling at server, so don't read sync transform from client
                return;
            }
            MovementState movementState;
            ExtraMovementState extraMovementState;
            Vector3 position;
            float yAngle;
            long timestamp;
            reader.ReadSyncTransformMessage3D(out movementState, out extraMovementState, out position, out yAngle, out timestamp);
            if (Mathf.Abs(timestamp - BaseGameNetworkManager.Singleton.ServerTimestamp) > System.TimeSpan.TicksPerMillisecond)
            {
                // Timestamp is a lot difference to server's timestamp, player might try to hack a game or packet may corrupted occurring, so skip it
                return;
            }
            if (acceptedPositionTimestamp < timestamp)
            {
                yRotation = yAngle;
                MovementState = movementState;
                ExtraMovementState = extraMovementState;
                if (!IsClient)
                {
                    // If it's server only (not a host), set position follows the client immediately
                    float currentTime = Time.unscaledTime;
                    float t = currentTime - lastServerValidateTransformTime;
                    float v = Entity.GetMoveSpeed();
                    float s = (lastServerValidateTransformMoveSpeed * (t + 0.2f)) + (v * t); // +200ms as high ping buffer
                    if (s < 0.001f)
                        s = 0.001f;
                    Vector3 oldPos = CacheTransform.position;
                    Vector3 newPos = position;
                    float dist = Vector3.Distance(oldPos, newPos);
                    if (dist <= s)
                    {
                        // Allow to move to the position
                        CacheTransform.position = position;
                    }
                    else
                    {
                        // Client moves too fast, adjust it
                        Vector3 dir = (newPos - oldPos).normalized;
                        newPos = oldPos + (dir * s);
                        CacheTransform.position = newPos;
                        // And also adjust client's position
                        Teleport(newPos, Quaternion.Euler(0f, yRotation, 0f));
                    }
                    lastServerValidateTransformTime = currentTime;
                    lastServerValidateTransformMoveSpeed = v;
                }
                else
                {
                    // It's both server and client, translate position (it's a host so don't do speed hack validation)
                    if (Vector3.Distance(position, CacheTransform.position) > 0.01f)
                        SetMovePaths(position, false);
                }
                acceptedPositionTimestamp = timestamp;
            }
            if (movementState.Has(MovementState.IsJump))
                acceptedJump = true;
        }

        protected virtual void OnTeleport(Vector3 position, float yAngle)
        {
            airborneElapsed = 0;
            verticalVelocity = 0;
            clientTargetPosition = null;
            NavPaths = null;
            CacheOpenCharacterController.SetPosition(position, false);
            yRotation = yAngle;
        }

#if UNITY_EDITOR
        [ContextMenu("Applies Collider Settings To Controller")]
        public void AppliesColliderSettingsToController()
        {
            CapsuleCollider collider = gameObject.GetOrAddComponent<CapsuleCollider>();
            float radius = collider.radius;
            float height = collider.height;
            Vector3 center = collider.center;
            // Prepare open character controller
            OpenCharacterController controller = gameObject.GetOrAddComponent<OpenCharacterController>();
            controller.SetRadiusHeightAndCenter(radius, height, center, true, true);
        }
#endif
    }
}
