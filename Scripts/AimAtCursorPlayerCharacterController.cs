using Insthync.CameraAndInput;
using UnityEngine;

namespace MultiplayerARPG
{
    public class AimAtCursorPlayerCharacterController : BasePlayerCharacterController
    {
        [Header("Camera Controls Prefabs")]
        [SerializeField]
        protected FollowCameraControls gameplayCameraPrefab;
        [SerializeField]
        protected FollowCameraControls minimapCameraPrefab;

        [Header("Controller Settings")]
        [SerializeField]
        protected bool doNotTurnToPointingEntity;
        [SerializeField]
        protected bool setAimPositionToRaycastHitPoint;

        [Header("Building Settings")]
        [SerializeField]
        protected bool buildGridSnap;
        [SerializeField]
        protected Vector3 buildGridOffsets = Vector3.zero;
        [SerializeField]
        protected float buildGridSize = 4f;
        [SerializeField]
        protected bool buildRotationSnap;
        [SerializeField]
        protected float buildRotateAngle = 45f;
        [SerializeField]
        protected float buildRotateSpeed = 200f;

        [Header("Entity Activating Settings")]
        [SerializeField]
        [Tooltip("If this value is `0`, this value will be set as `GameInstance` -> `conversationDistance`")]
        protected float distanceToActivateByActivateKey = 0f;
        [SerializeField]
        [Tooltip("If this value is `0`, this value will be set as `GameInstance` -> `pickUpItemDistance`")]
        protected float distanceToActivateByPickupKey = 0f;

        #region Events
        /// <summary>
        /// RelateId (string), AimPosition (AimPosition)
        /// </summary>
        public event System.Action<string, AimPosition> onBeforeUseSkillHotkey;
        /// <summary>
        /// RelateId (string), AimPosition (AimPosition)
        /// </summary>
        public event System.Action<string, AimPosition> onAfterUseSkillHotkey;
        /// <summary>
        /// RelateId (string), AimPosition (AimPosition)
        /// </summary>
        public event System.Action<string, AimPosition> onBeforeUseItemHotkey;
        /// <summary>
        /// RelateId (string), AimPosition (AimPosition)
        /// </summary>
        public event System.Action<string, AimPosition> onAfterUseItemHotkey;
        #endregion

        public byte HotkeyEquipWeaponSet { get; set; }
        public NearbyEntityDetector ActivatableEntityDetector { get; protected set; }
        public NearbyEntityDetector ItemDropEntityDetector { get; protected set; }
        public IGameplayCameraController CacheGameplayCameraController { get; protected set; }
        public IMinimapCameraController CacheMinimapCameraController { get; protected set; }

        // Input & control states variables
        protected bool _isLeftHandAttacking;
        protected bool _isSprinting;
        protected bool _isWalking;
        protected IPhysicFunctions _physicFunctions;
        protected Vector3 _aimTargetPosition;
        protected bool _attackPreventedWhileCursorOverUI;

        protected override void Awake()
        {
            base.Awake();
            // Initial physic functions
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                _physicFunctions = new PhysicFunctions(512);
            else
                _physicFunctions = new PhysicFunctions2D(512);
            // Initial gameplay camera controller
            CacheGameplayCameraController = gameObject.GetOrAddComponent<IGameplayCameraController, DefaultGameplayCameraController>((obj) =>
            {
                DefaultGameplayCameraController castedObj = obj as DefaultGameplayCameraController;
                castedObj.SetData(gameplayCameraPrefab);
            });
            CacheGameplayCameraController.Init();
            // Initial minimap camera controller
            CacheMinimapCameraController = gameObject.GetOrAddComponent<IMinimapCameraController, DefaultMinimapCameraController>((obj) =>
            {
                DefaultMinimapCameraController castedObj = obj as DefaultMinimapCameraController;
                castedObj.SetData(minimapCameraPrefab);
            });
            CacheMinimapCameraController.Init();
            // Initial build aim controller
            BuildAimController = gameObject.GetOrAddComponent<IBuildAimController, DefaultBuildAimController>((obj) =>
            {
                DefaultBuildAimController castedObj = obj as DefaultBuildAimController;
                castedObj.SetData(buildGridSnap, buildGridOffsets, buildGridSize, buildRotationSnap, buildRotateAngle, buildRotateSpeed);
            });
            BuildAimController.Init();
            // Initial area skill aim controller
            AreaSkillAimController = gameObject.GetOrAddComponent<IAreaSkillAimController, DefaultAreaSkillAimController>();

            // Setup activate distance
            if (distanceToActivateByActivateKey <= 0f)
                distanceToActivateByActivateKey = GameInstance.Singleton.conversationDistance;
            if (distanceToActivateByPickupKey <= 0f)
                distanceToActivateByPickupKey = GameInstance.Singleton.pickUpItemDistance;

            // This entity detector will be find entities to use when pressed activate key
            GameObject tempGameObject = new GameObject("_ActivatingEntityDetector");
            ActivatableEntityDetector = tempGameObject.AddComponent<NearbyEntityDetector>();
            ActivatableEntityDetector.detectingRadius = distanceToActivateByActivateKey;
            ActivatableEntityDetector.findActivatableEntity = true;
            ActivatableEntityDetector.findHoldActivatableEntity = true;
            // This entity detector will be find item drop entities to use when pressed pickup key
            tempGameObject = new GameObject("_ItemDropEntityDetector");
            ItemDropEntityDetector = tempGameObject.AddComponent<NearbyEntityDetector>();
            ItemDropEntityDetector.detectingRadius = distanceToActivateByPickupKey;
            ItemDropEntityDetector.findPickupActivatableEntity = true;
        }

        protected override void Desetup(BasePlayerCharacterEntity characterEntity)
        {
            base.Desetup(characterEntity);
            CacheGameplayCameraController.Desetup(characterEntity);
            CacheMinimapCameraController.Desetup(characterEntity);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ActivatableEntityDetector != null)
                Destroy(ActivatableEntityDetector.gameObject);
            if (ItemDropEntityDetector != null)
                Destroy(ItemDropEntityDetector.gameObject);
        }

        protected override void Update()
        {
            if (!PlayingCharacterEntity || !PlayingCharacterEntity.IsOwnerClient)
                return;

            CacheGameplayCameraController.FollowingEntityTransform = CameraTargetTransform;
            CacheMinimapCameraController.FollowingEntityTransform = CameraTargetTransform;
            CacheMinimapCameraController.FollowingGameplayCameraTransform = CacheGameplayCameraController.CameraTransform;

            UpdateInput();
        }

        protected void UpdateInput()
        {
            if (GenericUtils.IsFocusInputField() || PlayingCharacterEntity.IsDead())
            {
                PlayingCharacterEntity.KeyMovement(Vector3.zero, MovementState.None);
                return;
            }

            // If it's building something, don't allow to activate NPC/Warp/Pickup Item
            if (!ConstructingBuildingEntity)
            {
                // Activate nearby npcs / players / activable buildings
                if (InputManager.GetButtonDown("Activate"))
                {
                    if (ActivatableEntityDetector.activatableEntities.Count > 0)
                    {
                        IActivatableEntity activatable;
                        for (int i = 0; i < ActivatableEntityDetector.activatableEntities.Count; ++i)
                        {
                            activatable = ActivatableEntityDetector.activatableEntities[i];
                            if (activatable.CanActivate())
                            {
                                activatable.OnActivate();
                                break;
                            }
                        }
                    }
                }
                // Pick up nearby items
                if (InputManager.GetButtonDown("PickUpItem"))
                {
                    if (ItemDropEntityDetector.pickupActivatableEntities.Count > 0)
                    {
                        IPickupActivatableEntity activatable;
                        for (int i = 0; i < ItemDropEntityDetector.pickupActivatableEntities.Count; ++i)
                        {
                            activatable = ItemDropEntityDetector.pickupActivatableEntities[i];
                            if (activatable.CanPickupActivate())
                            {
                                activatable.OnPickupActivate();
                                break;
                            }
                        }
                    }
                }
                // Reload
                if (InputManager.GetButtonDown("Reload"))
                {
                    // Reload ammo when press the button
                    ReloadAmmo();
                }
                if (InputManager.GetButtonDown("ExitVehicle"))
                {
                    // Exit vehicle
                    PlayingCharacterEntity.CallCmdExitVehicle();
                }
                if (InputManager.GetButtonDown("SwitchEquipWeaponSet"))
                {
                    // Switch equip weapon set
                    GameInstance.ClientInventoryHandlers.RequestSwitchEquipWeaponSet(new RequestSwitchEquipWeaponSetMessage()
                    {
                        equipWeaponSet = (byte)(PlayingCharacterEntity.EquipWeaponSet + 1),
                    }, ClientInventoryActions.ResponseSwitchEquipWeaponSet);
                }
                // Auto reload
                if (PlayingCharacterEntity.EquipWeapons.rightHand.IsAmmoEmpty() ||
                    PlayingCharacterEntity.EquipWeapons.leftHand.IsAmmoEmpty())
                {
                    // Reload ammo when empty and not press any keys
                    ReloadAmmo();
                }
            }

            UpdateLookInput();
            UpdateWASDInput();
        }

        protected void UpdateWASDInput()
        {
            // If mobile platforms, don't receive input raw to make it smooth
            bool raw = !InputManager.IsUseMobileInput();
            Vector3 moveDirection = GetMoveDirection(InputManager.GetAxis("Horizontal", raw), InputManager.GetAxis("Vertical", raw));
            moveDirection.Normalize();

            // Get fire type
            FireType fireType;
            IWeaponItem leftHandItem = PlayingCharacterEntity.EquipWeapons.GetLeftHandWeaponItem();
            IWeaponItem rightHandItem = PlayingCharacterEntity.EquipWeapons.GetRightHandWeaponItem();
            if (_isLeftHandAttacking && leftHandItem != null)
            {
                fireType = leftHandItem.FireType;
            }
            else if (!_isLeftHandAttacking && rightHandItem != null)
            {
                fireType = rightHandItem.FireType;
            }
            else
            {
                fireType = GameInstance.Singleton.DefaultWeaponItem.FireType;
            }

            if (moveDirection.sqrMagnitude > 0f)
            {
                // Character start moving, so hide npc dialog
                HideNpcDialog();
            }

            // Attack when player pressed attack button
            if (!UICharacterHotkeys.UsingHotkey)
            {
                // NOTE: With this controller, single fire will do the same as automatic
                if (InputManager.GetButtonDown("Fire1") || InputManager.GetButtonDown("Attack"))
                {
                    bool isPointerOverUIObject = UISceneGameplay.IsPointerOverUIObject();
                    // If pointer over ui, start avoid attack inputs to prevent character attack while pointer over ui
                    if (isPointerOverUIObject)
                        _attackPreventedWhileCursorOverUI = true;

                    if (!_attackPreventedWhileCursorOverUI)
                    {
                        switch (fireType)
                        {
                            case FireType.SingleFire:
                            case FireType.Automatic:
                                break;
                            case FireType.FireOnRelease:
                                WeaponCharge();
                                break;
                        }
                    }
                }
                else if (InputManager.GetButton("Fire1") || InputManager.GetButton("Attack"))
                {
                    if (!_attackPreventedWhileCursorOverUI)
                    {
                        switch (fireType)
                        {
                            case FireType.SingleFire:
                            case FireType.Automatic:
                                Attack();
                                break;
                            case FireType.FireOnRelease:
                                break;
                        }
                    }
                }
                else if (InputManager.GetButtonUp("Fire1") || InputManager.GetButtonUp("Attack"))
                {
                    if (!_attackPreventedWhileCursorOverUI)
                    {
                        switch (fireType)
                        {
                            case FireType.SingleFire:
                            case FireType.Automatic:
                                break;
                            case FireType.FireOnRelease:
                                Attack();
                                break;
                        }
                    }

                    bool isPointerOverUIObject = UISceneGameplay.IsPointerOverUIObject();
                    // No pointer over ui and all attack key released, stop avoid attack inputs
                    if (_attackPreventedWhileCursorOverUI && !isPointerOverUIObject)
                        _attackPreventedWhileCursorOverUI = false;
                }
            }

            // Always forward
            MovementState movementState = MovementState.None;
            if (PlayingCharacterEntity.MovementState.Has(MovementState.IsUnderWater))
            {
                if (InputManager.GetButton("SwimUp"))
                {
                    movementState |= MovementState.Up;
                }
                else if (InputManager.GetButton("SwimDown"))
                {
                    movementState |= MovementState.Down;
                }
                _isSprinting = false;
                _isWalking = false;
            }
            else if (PlayingCharacterEntity.MovementState.Has(MovementState.IsGrounded))
            {
                if (InputManager.GetButtonDown("Sprint"))
                {
                    // Toggles sprint state
                    _isSprinting = !_isSprinting;
                    _isWalking = false;
                }
                else if (InputManager.GetButtonDown("Walk"))
                {
                    // Toggles sprint state
                    _isWalking = !_isWalking;
                    _isSprinting = false;
                }
                if (InputManager.GetButtonDown("Jump"))
                {
                    movementState |= MovementState.IsJump;
                }
            }
            movementState |= GameplayUtils.GetStraightlyMovementStateByDirection(moveDirection, MovementTransform.forward);
            PlayingCharacterEntity.KeyMovement(moveDirection, movementState);

            // Set extra movement state
            if (_isSprinting)
                PlayingCharacterEntity.SetExtraMovementState(ExtraMovementState.IsSprinting);
            else if (_isWalking)
                PlayingCharacterEntity.SetExtraMovementState(ExtraMovementState.IsWalking);
            else
                PlayingCharacterEntity.SetExtraMovementState(ExtraMovementState.None);
        }

        protected void Attack()
        {
            if (ConstructingBuildingEntity)
                return;
            PlayingCharacterEntity.SetTargetEntity(SelectedGameEntity);
            // Switching right/left/right/left...
            if (PlayingCharacterEntity.Attack(ref _isLeftHandAttacking))
                _isLeftHandAttacking = !_isLeftHandAttacking;
        }

        protected void WeaponCharge()
        {
            if (ConstructingBuildingEntity)
                return;
            // Switching right/left/right/left...
            if (PlayingCharacterEntity.StartCharge(ref _isLeftHandAttacking))
                _isLeftHandAttacking = !_isLeftHandAttacking;
        }

        protected void UpdateLookInput()
        {
            bool foundTargetEntity = false;
            bool isMobile = InputManager.IsUseMobileInput();
            Vector2 lookDirection;
            if (isMobile)
            {
                // Turn character by joystick
                lookDirection = new Vector2(InputManager.GetAxis("Mouse X", false), InputManager.GetAxis("Mouse Y", false));
                Transform tempTransform;
                IGameEntity tempGameEntity;
                Vector3 tempTargetPosition;
                int pickedCount;
                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                    pickedCount = _physicFunctions.Raycast(PlayingCharacterEntity.MeleeDamageTransform.position, lookDirection, 100f, Physics.DefaultRaycastLayers);
                else
                    pickedCount = _physicFunctions.Raycast(PlayingCharacterEntity.MeleeDamageTransform.position, new Vector3(lookDirection.x, 0, lookDirection.y), 100f, Physics.DefaultRaycastLayers);
                for (int i = pickedCount - 1; i >= 0; --i)
                {
                    _aimTargetPosition = _physicFunctions.GetRaycastPoint(i);
                    tempTransform = _physicFunctions.GetRaycastTransform(i);
                    tempGameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (!tempGameEntity.IsNull())
                    {
                        foundTargetEntity = true;
                        UISceneGameplay.SetTargetEntity(tempGameEntity.Entity);
                        SelectedEntity = tempGameEntity.Entity;
                        if (tempGameEntity.Entity != PlayingCharacterEntity.Entity)
                        {
                            // Turn to pointing entity, so find pointing target position and set look direction
                            if (!doNotTurnToPointingEntity)
                            {
                                // Find target position
                                if (tempGameEntity is IDamageableEntity damageable)
                                    tempTargetPosition = damageable.OpponentAimTransform.position;
                                else
                                    tempTargetPosition = tempGameEntity.GetTransform().position;
                                // Set look direction
                                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                    lookDirection = (tempTargetPosition - EntityTransform.position).normalized;
                                else
                                    lookDirection = (XZ(tempTargetPosition) - XZ(EntityTransform.position)).normalized;
                            }
                        }
                        break;
                    }
                }
            }
            else
            {
                // Turn character follow cursor
                lookDirection = (InputManager.MousePosition() - new Vector3(Screen.width, Screen.height) * 0.5f).normalized;
                // Pick on object by mouse position
                Transform tempTransform;
                IGameEntity tempGameEntity;
                Vector3 tempTargetPosition;
                int pickedCount = _physicFunctions.RaycastPickObjects(CacheGameplayCameraController.Camera, InputManager.MousePosition(), Physics.DefaultRaycastLayers, 100f, out _);
                for (int i = pickedCount - 1; i >= 0; --i)
                {
                    _aimTargetPosition = _physicFunctions.GetRaycastPoint(i);
                    tempTransform = _physicFunctions.GetRaycastTransform(i);
                    tempGameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (!tempGameEntity.IsNull())
                    {
                        foundTargetEntity = true;
                        UISceneGameplay.SetTargetEntity(tempGameEntity.Entity);
                        SelectedEntity = tempGameEntity.Entity;
                        if (tempGameEntity.Entity != PlayingCharacterEntity.Entity)
                        {
                            // Turn to pointing entity, so find pointing target position and set look direction
                            if (!doNotTurnToPointingEntity)
                            {
                                // Find target position
                                if (tempGameEntity is IDamageableEntity damageable)
                                    tempTargetPosition = damageable.OpponentAimTransform.position;
                                else
                                    tempTargetPosition = tempGameEntity.GetTransform().position;
                                // Set look direction
                                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                    lookDirection = (tempTargetPosition - EntityTransform.position).normalized;
                                else
                                    lookDirection = (XZ(tempTargetPosition) - XZ(EntityTransform.position)).normalized;
                            }
                        }
                        break;
                    }
                }
            }
            if (!foundTargetEntity)
            {
                UISceneGameplay.SetTargetEntity(null);
                SelectedEntity = null;
            }

            // Set aim position
            if (setAimPositionToRaycastHitPoint)
            {
                PlayingCharacterEntity.AimPosition = PlayingCharacterEntity.GetAttackAimPosition(ref _isLeftHandAttacking, _aimTargetPosition);
                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension3D)
                {
                    Quaternion aimRotation = Quaternion.LookRotation(PlayingCharacterEntity.AimPosition.direction);
                    PlayingCharacterEntity.Pitch = aimRotation.eulerAngles.x;
                }
            }
            else
            {
                PlayingCharacterEntity.AimPosition = PlayingCharacterEntity.GetAttackAimPosition(ref _isLeftHandAttacking);
            }

            // Turn character
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                {
                    PlayingCharacterEntity.SetLookRotation(Quaternion.LookRotation(lookDirection), false);
                }
                else
                {
                    PlayingCharacterEntity.SetLookRotation(Quaternion.LookRotation(new Vector3(lookDirection.x, 0, lookDirection.y)), false);
                }
            }
        }

        protected void ReloadAmmo()
        {
            // Reload ammo at server
            if (!PlayingCharacterEntity.EquipWeapons.rightHand.IsAmmoFull())
                PlayingCharacterEntity.Reload(false);
            else if (!PlayingCharacterEntity.EquipWeapons.leftHand.IsAmmoFull())
                PlayingCharacterEntity.Reload(true);
        }

        public override bool UseHotkey(HotkeyType type, string relateId, AimPosition aimPosition)
        {
            ClearQueueUsingSkill();
            bool beingUsed = false;
            switch (type)
            {
                case HotkeyType.Skill:
                    if (onBeforeUseSkillHotkey != null)
                        onBeforeUseSkillHotkey.Invoke(relateId, aimPosition);
                    beingUsed = UseSkill(relateId, aimPosition);
                    if (onAfterUseSkillHotkey != null)
                        onAfterUseSkillHotkey.Invoke(relateId, aimPosition);
                    break;
                case HotkeyType.Item:
                    HotkeyEquipWeaponSet = PlayingCharacterEntity.EquipWeaponSet;
                    if (onBeforeUseItemHotkey != null)
                        onBeforeUseItemHotkey.Invoke(relateId, aimPosition);
                    beingUsed = UseItem(relateId, aimPosition);
                    if (onAfterUseItemHotkey != null)
                        onAfterUseItemHotkey.Invoke(relateId, aimPosition);
                    break;
                case HotkeyType.GuildSkill:
                    beingUsed = UseGuildSkill(relateId);
                    break;
            }
            return beingUsed;
        }

        protected bool UseSkill(string id, AimPosition aimPosition)
        {
            int dataId = BaseGameData.MakeDataId(id);
            if (!GameInstance.Skills.TryGetValue(dataId, out BaseSkill skill) || skill == null ||
                !PlayingCharacterEntity.GetCaches().Skills.TryGetValue(skill, out _))
                return false;
            bool isAttackSkill = skill.IsAttack;
            if (PlayingCharacterEntity.UseSkill(skill.DataId, _isLeftHandAttacking, SelectedGameEntityObjectId, aimPosition) && isAttackSkill)
            {
                _isLeftHandAttacking = !_isLeftHandAttacking;
                return true;
            }
            return false;
        }

        protected bool UseItem(string id, AimPosition aimPosition)
        {
            int itemIndex;
            int dataId = BaseGameData.MakeDataId(id);
            if (GameInstance.Items.TryGetValue(dataId, out BaseItem item))
            {
                itemIndex = GameInstance.PlayingCharacterEntity.IndexOfNonEquipItem(dataId);
            }
            else
            {
                if (PlayingCharacterEntity.IsEquipped(
                    id,
                    out InventoryType inventoryType,
                    out itemIndex,
                    out byte equipWeaponSet,
                    out CharacterItem characterItem))
                {
                    GameInstance.ClientInventoryHandlers.RequestUnEquipItem(
                        inventoryType,
                        itemIndex,
                        equipWeaponSet,
                        -1,
                        ClientInventoryActions.ResponseUnEquipArmor,
                        ClientInventoryActions.ResponseUnEquipWeapon);
                    return true;
                }
                item = characterItem.GetItem();
            }

            if (itemIndex < 0)
                return false;

            if (item == null)
                return false;

            if (item.IsEquipment())
            {
                GameInstance.ClientInventoryHandlers.RequestEquipItem(
                        PlayingCharacterEntity,
                        itemIndex,
                        HotkeyEquipWeaponSet,
                        ClientInventoryActions.ResponseEquipArmor,
                        ClientInventoryActions.ResponseEquipWeapon);
                return true;
            }
            else if (item.IsSkill())
            {
                bool isAttackSkill = (item as ISkillItem).SkillData.IsAttack;
                if (PlayingCharacterEntity.UseSkillItem(itemIndex, _isLeftHandAttacking, SelectedGameEntityObjectId, aimPosition) && isAttackSkill)
                {
                    _isLeftHandAttacking = !_isLeftHandAttacking;
                    return true;
                }
            }
            else if (item.IsBuilding())
            {
                _buildingItemIndex = itemIndex;
                ShowConstructBuildingDialog();
                return true;
            }
            else if (item.IsUsable())
            {
                return PlayingCharacterEntity.CallCmdUseItem(itemIndex);
            }

            return false;
        }

        protected bool UseGuildSkill(string id)
        {
            if (GameInstance.JoinedGuild == null)
                return false;
            int dataId = BaseGameData.MakeDataId(id);
            return PlayingCharacterEntity.CallCmdUseGuildSkill(dataId);
        }

        public Vector3 GetMoveDirection(float horizontalInput, float verticalInput)
        {
            Vector3 moveDirection = Vector3.zero;
            switch (CurrentGameInstance.DimensionType)
            {
                case DimensionType.Dimension3D:
                    Vector3 forward = CacheGameplayCameraController.CameraTransform.forward;
                    Vector3 right = CacheGameplayCameraController.CameraTransform.right;
                    forward.y = 0f;
                    right.y = 0f;
                    forward.Normalize();
                    right.Normalize();
                    moveDirection += forward * verticalInput;
                    moveDirection += right * horizontalInput;
                    // normalize input if it exceeds 1 in combined length:
                    if (moveDirection.sqrMagnitude > 1f)
                        moveDirection.Normalize();
                    break;
                case DimensionType.Dimension2D:
                    moveDirection = new Vector2(horizontalInput, verticalInput);
                    break;
            }
            return moveDirection;
        }

        protected Vector2 XZ(Vector3 vector3)
        {
            return new Vector2(vector3.x, vector3.z);
        }

        public override bool ShouldShowActivateButtons()
        {
            if (ActivatableEntityDetector.activatableEntities.Count > 0)
            {
                IActivatableEntity activatable;
                for (int i = 0; i < ActivatableEntityDetector.activatableEntities.Count; ++i)
                {
                    activatable = ActivatableEntityDetector.activatableEntities[i];
                    if (activatable.CanActivate())
                        return true;
                }
            }
            return false;
        }

        public override bool ShouldShowHoldActivateButtons()
        {
            if (ActivatableEntityDetector.holdActivatableEntities.Count > 0)
            {
                IHoldActivatableEntity activatable;
                for (int i = 0; i < ActivatableEntityDetector.holdActivatableEntities.Count; ++i)
                {
                    activatable = ActivatableEntityDetector.holdActivatableEntities[i];
                    if (activatable.CanHoldActivate())
                        return true;
                }
            }
            return false;
        }

        public override bool ShouldShowPickUpButtons()
        {
            if (ItemDropEntityDetector.pickupActivatableEntities.Count > 0)
            {
                IPickupActivatableEntity activatable;
                for (int i = 0; i < ItemDropEntityDetector.pickupActivatableEntities.Count; ++i)
                {
                    activatable = ItemDropEntityDetector.pickupActivatableEntities[i];
                    if (activatable.CanPickupActivate())
                        return true;
                }
            }
            return false;
        }
    }
}
