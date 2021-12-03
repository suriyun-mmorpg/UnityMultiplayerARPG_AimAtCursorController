using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{
    public class AimAtCursorPlayerCharacterController : BasePlayerCharacterController
    {
        public const float BUILDING_CONSTRUCTING_GROUND_FINDING_DISTANCE = 100f;

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

        protected bool isLeftHandAttacking;
        protected bool isSprinting;
        protected bool isWalking;
        protected IPhysicFunctions physicFunctions;
        protected BaseCharacterEntity targetCharacter;
        protected BasePlayerCharacterEntity targetPlayer;
        protected BaseMonsterCharacterEntity targetMonster;
        protected NpcEntity targetNpc;
        protected ItemDropEntity targetItemDrop;
        protected BuildingEntity targetBuilding;
        protected VehicleEntity targetVehicle;
        protected WarpPortalEntity targetWarpPortal;
        protected HarvestableEntity targetHarvestable;
        protected ItemsContainerEntity targetItemsContainer;
        protected Vector3 aimTargetPosition;

        public FollowCameraControls CacheGameplayCameraControls { get; protected set; }
        public FollowCameraControls CacheMinimapCameraControls { get; protected set; }
        public Camera CacheGameplayCamera { get { return CacheGameplayCameraControls.CacheCamera; } }
        public Camera CacheMiniMapCamera { get { return CacheMinimapCameraControls.CacheCamera; } }
        public Transform CacheGameplayCameraTransform { get { return CacheGameplayCameraControls.CacheCameraTransform; } }
        public Transform CacheMiniMapCameraTransform { get { return CacheMinimapCameraControls.CacheCameraTransform; } }
        public NearbyEntityDetector ActivatableEntityDetector { get; protected set; }
        public NearbyEntityDetector ItemDropEntityDetector { get; protected set; }
        protected bool avoidAttackWhileCursorOverUI;
        protected float buildYRotate;

        protected override void Awake()
        {
            base.Awake();
            if (gameplayCameraPrefab != null)
                CacheGameplayCameraControls = Instantiate(gameplayCameraPrefab);
            if (minimapCameraPrefab != null)
                CacheMinimapCameraControls = Instantiate(minimapCameraPrefab);
            // This entity detector will be find entities to use when pressed activate key
            GameObject tempGameObject = new GameObject("_ActivatingEntityDetector");
            ActivatableEntityDetector = tempGameObject.AddComponent<NearbyEntityDetector>();
            ActivatableEntityDetector.detectingRadius = CurrentGameInstance.conversationDistance;
            ActivatableEntityDetector.findPlayer = true;
            ActivatableEntityDetector.findOnlyAlivePlayers = true;
            ActivatableEntityDetector.findNpc = true;
            ActivatableEntityDetector.findBuilding = true;
            ActivatableEntityDetector.findOnlyAliveBuildings = true;
            ActivatableEntityDetector.findOnlyActivatableBuildings = true;
            ActivatableEntityDetector.findVehicle = true;
            ActivatableEntityDetector.findWarpPortal = true;
            // This entity detector will be find item drop entities to use when pressed pickup key
            tempGameObject = new GameObject("_ItemDropEntityDetector");
            ItemDropEntityDetector = tempGameObject.AddComponent<NearbyEntityDetector>();
            ItemDropEntityDetector.detectingRadius = CurrentGameInstance.pickUpItemDistance;
            ItemDropEntityDetector.findItemDrop = true;
            ItemDropEntityDetector.findItemsContainer = true;
            // Initial physic functions
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                physicFunctions = new PhysicFunctions(512);
            else
                physicFunctions = new PhysicFunctions2D(512);
        }

        protected override void Desetup(BasePlayerCharacterEntity characterEntity)
        {
            base.Desetup(characterEntity);

            if (CacheGameplayCameraControls != null)
                CacheGameplayCameraControls.target = null;

            if (CacheMinimapCameraControls != null)
                CacheMinimapCameraControls.target = null;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (CacheGameplayCameraControls != null)
                Destroy(CacheGameplayCameraControls.gameObject);
            if (CacheMinimapCameraControls != null)
                Destroy(CacheMinimapCameraControls.gameObject);
            if (ActivatableEntityDetector != null)
                Destroy(ActivatableEntityDetector.gameObject);
            if (ItemDropEntityDetector != null)
                Destroy(ItemDropEntityDetector.gameObject);
        }

        protected override void Update()
        {
            if (!PlayerCharacterEntity || !PlayerCharacterEntity.IsOwnerClient)
                return;

            if (CacheGameplayCameraControls != null)
                CacheGameplayCameraControls.target = CameraTargetTransform;

            if (CacheMinimapCameraControls != null)
                CacheMinimapCameraControls.target = CameraTargetTransform;

            UpdateInput();
        }

        protected void UpdateInput()
        {
            if (GenericUtils.IsFocusInputField() || PlayerCharacterEntity.IsDead())
            {
                PlayerCharacterEntity.KeyMovement(Vector3.zero, MovementState.None);
                return;
            }

            // If it's building something, don't allow to activate NPC/Warp/Pickup Item
            if (!ConstructingBuildingEntity)
            {
                // Activate nearby npcs / players / activable buildings
                if (InputManager.GetButtonDown("Activate"))
                {
                    targetPlayer = null;
                    if (ActivatableEntityDetector.players.Count > 0)
                        targetPlayer = ActivatableEntityDetector.players[0];
                    targetNpc = null;
                    if (ActivatableEntityDetector.npcs.Count > 0)
                        targetNpc = ActivatableEntityDetector.npcs[0];
                    targetBuilding = null;
                    if (ActivatableEntityDetector.buildings.Count > 0)
                        targetBuilding = ActivatableEntityDetector.buildings[0];
                    targetVehicle = null;
                    if (ActivatableEntityDetector.vehicles.Count > 0)
                        targetVehicle = ActivatableEntityDetector.vehicles[0];
                    targetWarpPortal = null;
                    if (ActivatableEntityDetector.warpPortals.Count > 0)
                        targetWarpPortal = ActivatableEntityDetector.warpPortals[0];
                    targetItemsContainer = null;
                    if (ItemDropEntityDetector.itemsContainers.Count > 0)
                        targetItemsContainer = ItemDropEntityDetector.itemsContainers[0];
                    // Priority Player -> Npc -> Buildings
                    if (targetPlayer && CacheUISceneGameplay)
                    {
                        // Show dealing, invitation menu
                        SelectedEntity = targetPlayer;
                        CacheUISceneGameplay.SetActivePlayerCharacter(targetPlayer);
                    }
                    else if (targetNpc)
                    {
                        // Talk to NPC
                        SelectedEntity = targetNpc;
                        PlayerCharacterEntity.NpcAction.CallServerNpcActivate(targetNpc.ObjectId);
                    }
                    else if (targetBuilding)
                    {
                        // Use building
                        SelectedEntity = targetBuilding;
                        ActivateBuilding(targetBuilding);
                    }
                    else if (targetVehicle)
                    {
                        // Enter vehicle
                        PlayerCharacterEntity.CallServerEnterVehicle(targetVehicle.ObjectId);
                    }
                    else if (targetWarpPortal)
                    {
                        // Enter warp, For some warp portals that `warpImmediatelyWhenEnter` is FALSE
                        PlayerCharacterEntity.CallServerEnterWarp(targetWarpPortal.ObjectId);
                    }
                    else if (targetItemsContainer)
                    {
                        // Show items
                        ShowItemsContainerDialog(targetItemsContainer);
                    }
                }
                // Pick up nearby items
                if (InputManager.GetButtonDown("PickUpItem"))
                {
                    targetItemDrop = null;
                    if (ItemDropEntityDetector.itemDrops.Count > 0)
                        targetItemDrop = ItemDropEntityDetector.itemDrops[0];
                    if (targetItemDrop != null)
                        PlayerCharacterEntity.CallServerPickupItem(targetItemDrop.ObjectId);
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
                    PlayerCharacterEntity.CallServerExitVehicle();
                }
                if (InputManager.GetButtonDown("SwitchEquipWeaponSet"))
                {
                    // Switch equip weapon set
                    GameInstance.ClientInventoryHandlers.RequestSwitchEquipWeaponSet(new RequestSwitchEquipWeaponSetMessage()
                    {
                        equipWeaponSet = (byte)(PlayerCharacterEntity.EquipWeaponSet + 1),
                    }, ClientInventoryActions.ResponseSwitchEquipWeaponSet);
                }
                if (InputManager.GetButtonDown("Sprint"))
                {
                    // Toggles sprint state
                    isSprinting = !isSprinting;
                    isWalking = false;
                }
                else if (InputManager.GetButtonDown("Walk"))
                {
                    // Toggles sprint state
                    isWalking = !isWalking;
                    isSprinting = false;
                }
                // Auto reload
                if (PlayerCharacterEntity.EquipWeapons.rightHand.IsAmmoEmpty() ||
                    PlayerCharacterEntity.EquipWeapons.leftHand.IsAmmoEmpty())
                {
                    // Reload ammo when empty and not press any keys
                    ReloadAmmo();
                }
            }

            UpdateLookInput();
            UpdateWASDInput();
            // Set extra movement state
            if (isSprinting)
                PlayerCharacterEntity.SetExtraMovementState(ExtraMovementState.IsSprinting);
            else if (isWalking)
                PlayerCharacterEntity.SetExtraMovementState(ExtraMovementState.IsWalking);
            else
                PlayerCharacterEntity.SetExtraMovementState(ExtraMovementState.None);
        }

        protected void UpdateWASDInput()
        {
            // If mobile platforms, don't receive input raw to make it smooth
            bool raw = !InputManager.useMobileInputOnNonMobile && !Application.isMobilePlatform;
            Vector3 moveDirection = GetMoveDirection(InputManager.GetAxis("Horizontal", raw), InputManager.GetAxis("Vertical", raw));
            moveDirection.Normalize();

            // Get fire type
            FireType fireType = FireType.SingleFire;
            IWeaponItem leftHandItem = PlayerCharacterEntity.EquipWeapons.GetLeftHandWeaponItem();
            IWeaponItem rightHandItem = PlayerCharacterEntity.EquipWeapons.GetRightHandWeaponItem();
            if (isLeftHandAttacking && leftHandItem != null)
            {
                fireType = leftHandItem.FireType;
            }
            else if (!isLeftHandAttacking && rightHandItem != null)
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

            bool isPointerOverUIObject = CacheUISceneGameplay.IsPointerOverUIObject();
            // If pointer over ui, start avoid attack inputs
            // to prevent character attack while pointer over ui
            if (isPointerOverUIObject)
                avoidAttackWhileCursorOverUI = true;

            // Attack when player pressed attack button
            if (!avoidAttackWhileCursorOverUI && !UICharacterHotkeys.UsingHotkey)
            {
                // NOTE: With this controller, single fire will do the same as automatic
                if (InputManager.GetButtonDown("Fire1") || InputManager.GetButtonDown("Attack"))
                {
                    switch (fireType)
                    {
                        case FireType.SingleFire:
                        case FireType.Automatic:
                            Attack();
                            break;
                        case FireType.FireOnRelease:
                            WeaponCharge();
                            break;
                    }
                }
                else if (InputManager.GetButton("Fire1") || InputManager.GetButtonUp("Attack"))
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
                else if (InputManager.GetButtonUp("Fire1") || InputManager.GetButtonUp("Attack"))
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
            }

            // No pointer over ui and all attack key released, stop avoid attack inputs
            if (avoidAttackWhileCursorOverUI && !isPointerOverUIObject &&
                !InputManager.GetButton("Fire1") && !InputManager.GetButton("Attack"))
                avoidAttackWhileCursorOverUI = false;

            // Always forward
            MovementState movementState = MovementState.None;
            if (InputManager.GetButtonDown("Jump"))
                movementState |= MovementState.IsJump;
            movementState |= GameplayUtils.GetStraightlyMovementStateByDirection(moveDirection, MovementTransform.forward);
            PlayerCharacterEntity.KeyMovement(moveDirection, movementState);
        }

        protected void Attack()
        {
            if (ConstructingBuildingEntity)
                return;
            PlayerCharacterEntity.SetTargetEntity(SelectedEntity);
            if (PlayerCharacterEntity.Attack(isLeftHandAttacking))
                isLeftHandAttacking = !isLeftHandAttacking;
        }

        protected void WeaponCharge()
        {
            if (ConstructingBuildingEntity)
                return;
            PlayerCharacterEntity.StartCharge(isLeftHandAttacking);
        }

        protected void UpdateLookInput()
        {
            bool foundTargetEntity = false;
            bool isMobile = InputManager.useMobileInputOnNonMobile || Application.isMobilePlatform;
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
                    pickedCount = physicFunctions.Raycast(PlayerCharacterEntity.MeleeDamageTransform.position, lookDirection, 100f, Physics.DefaultRaycastLayers);
                else
                    pickedCount = physicFunctions.Raycast(PlayerCharacterEntity.MeleeDamageTransform.position, new Vector3(lookDirection.x, 0, lookDirection.y), 100f, Physics.DefaultRaycastLayers);
                for (int i = pickedCount - 1; i >= 0; --i)
                {
                    aimTargetPosition = physicFunctions.GetRaycastPoint(i);
                    tempTransform = physicFunctions.GetRaycastTransform(i);
                    tempGameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (tempGameEntity != null)
                    {
                        foundTargetEntity = true;
                        CacheUISceneGameplay.SetTargetEntity(tempGameEntity.Entity);
                        SelectedEntity = tempGameEntity.Entity;
                        if (tempGameEntity.Entity != PlayerCharacterEntity.Entity)
                        {
                            // Turn to pointing entity, so find pointing target position and set look direction
                            if (!doNotTurnToPointingEntity)
                            {
                                // Find target position
                                if (tempGameEntity is IDamageableEntity)
                                    tempTargetPosition = (tempGameEntity as IDamageableEntity).OpponentAimTransform.position;
                                else
                                    tempTargetPosition = tempGameEntity.GetTransform().position;
                                // Set look direction
                                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                    lookDirection = (tempTargetPosition - CacheTransform.position).normalized;
                                else
                                    lookDirection = (XZ(tempTargetPosition) - XZ(CacheTransform.position)).normalized;
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
                int pickedCount = physicFunctions.RaycastPickObjects(CacheGameplayCamera, InputManager.MousePosition(), Physics.DefaultRaycastLayers, 100f, out _);
                for (int i = pickedCount - 1; i >= 0; --i)
                {
                    aimTargetPosition = physicFunctions.GetRaycastPoint(i);
                    tempTransform = physicFunctions.GetRaycastTransform(i);
                    tempGameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (tempGameEntity != null)
                    {
                        foundTargetEntity = true;
                        CacheUISceneGameplay.SetTargetEntity(tempGameEntity.Entity);
                        SelectedEntity = tempGameEntity.Entity;
                        if (tempGameEntity.Entity != PlayerCharacterEntity.Entity)
                        {
                            // Turn to pointing entity, so find pointing target position and set look direction
                            if (!doNotTurnToPointingEntity)
                            {
                                // Find target position
                                if (tempGameEntity is IDamageableEntity)
                                    tempTargetPosition = (tempGameEntity as IDamageableEntity).OpponentAimTransform.position;
                                else
                                    tempTargetPosition = tempGameEntity.GetTransform().position;
                                // Set look direction
                                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                    lookDirection = (tempTargetPosition - CacheTransform.position).normalized;
                                else
                                    lookDirection = (XZ(tempTargetPosition) - XZ(CacheTransform.position)).normalized;
                            }
                        }
                        break;
                    }
                }
            }
            if (!foundTargetEntity)
            {
                CacheUISceneGameplay.SetTargetEntity(null);
                SelectedEntity = null;
            }

            // Set aim position
            if (setAimPositionToRaycastHitPoint)
            {
                PlayerCharacterEntity.AimPosition = PlayerCharacterEntity.GetAttackAimPosition(ref isLeftHandAttacking, aimTargetPosition);
                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension3D)
                {
                    Quaternion aimRotation = Quaternion.LookRotation(PlayerCharacterEntity.AimPosition.direction);
                    PlayerCharacterEntity.Pitch = aimRotation.eulerAngles.x;
                }
            }
            else
            {
                PlayerCharacterEntity.AimPosition = PlayerCharacterEntity.GetAttackAimPosition(ref isLeftHandAttacking);
            }

            // Turn character
            if (lookDirection.sqrMagnitude > 0f)
            {
                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                {
                    PlayerCharacterEntity.SetLookRotation(Quaternion.LookRotation(lookDirection));
                }
                else
                {
                    PlayerCharacterEntity.SetLookRotation(Quaternion.LookRotation(new Vector3(lookDirection.x, 0, lookDirection.y)));
                }
            }
        }

        protected void ReloadAmmo()
        {
            // Reload ammo at server
            if (!PlayerCharacterEntity.EquipWeapons.rightHand.IsAmmoFull())
                PlayerCharacterEntity.Reload(false);
            else if (!PlayerCharacterEntity.EquipWeapons.leftHand.IsAmmoFull())
                PlayerCharacterEntity.Reload(true);
        }

        public override void UseHotkey(HotkeyType type, string relateId, AimPosition aimPosition)
        {
            ClearQueueUsingSkill();
            switch (type)
            {
                case HotkeyType.Skill:
                    UseSkill(relateId, aimPosition);
                    break;
                case HotkeyType.Item:
                    UseItem(relateId, aimPosition);
                    break;
            }
        }

        protected void UseSkill(string id, AimPosition aimPosition)
        {
            BaseSkill skill;
            if (!GameInstance.Skills.TryGetValue(BaseGameData.MakeDataId(id), out skill) || skill == null ||
                !PlayerCharacterEntity.GetCaches().Skills.TryGetValue(skill, out _))
                return;

            bool isAttackSkill = skill.IsAttack;
            if (PlayerCharacterEntity.UseSkill(skill.DataId, isLeftHandAttacking, SelectedEntityObjectId, aimPosition) && isAttackSkill)
            {
                isLeftHandAttacking = !isLeftHandAttacking;
            }
        }

        protected void UseItem(string id, AimPosition aimPosition)
        {
            int itemIndex;
            BaseItem item;
            int dataId = BaseGameData.MakeDataId(id);
            if (GameInstance.Items.ContainsKey(dataId))
            {
                item = GameInstance.Items[dataId];
                itemIndex = OwningCharacter.IndexOfNonEquipItem(dataId);
            }
            else
            {
                InventoryType inventoryType;
                byte equipWeaponSet;
                CharacterItem characterItem;
                if (PlayerCharacterEntity.IsEquipped(
                    id,
                    out inventoryType,
                    out itemIndex,
                    out equipWeaponSet,
                    out characterItem))
                {
                    GameInstance.ClientInventoryHandlers.RequestUnEquipItem(
                        inventoryType,
                        (short)itemIndex,
                        equipWeaponSet,
                        -1,
                        ClientInventoryActions.ResponseUnEquipArmor,
                        ClientInventoryActions.ResponseUnEquipWeapon);
                    return;
                }
                item = characterItem.GetItem();
            }

            if (itemIndex < 0)
                return;

            if (item == null)
                return;

            if (item.IsEquipment())
            {
                GameInstance.ClientInventoryHandlers.RequestEquipItem(
                        PlayerCharacterEntity,
                        (short)itemIndex,
                        ClientInventoryActions.ResponseEquipArmor,
                        ClientInventoryActions.ResponseEquipWeapon);
            }
            else if (item.IsSkill())
            {
                bool isAttackSkill = (item as ISkillItem).UsingSkill.IsAttack;
                if (PlayerCharacterEntity.UseSkillItem((short)itemIndex, isLeftHandAttacking, SelectedEntityObjectId, aimPosition) && isAttackSkill)
                {
                    isLeftHandAttacking = !isLeftHandAttacking;
                }
            }
            else if (item.IsBuilding())
            {
                buildingItemIndex = itemIndex;
                ShowConstructBuildingDialog();
            }
            else if (item.IsUsable())
            {
                PlayerCharacterEntity.CallServerUseItem((short)itemIndex);
            }
        }

        public Vector3 GetMoveDirection(float horizontalInput, float verticalInput)
        {
            Vector3 moveDirection = Vector3.zero;
            switch (CurrentGameInstance.DimensionType)
            {
                case DimensionType.Dimension3D:
                    Vector3 forward = CacheGameplayCameraControls.CacheCameraTransform.forward;
                    Vector3 right = CacheGameplayCameraControls.CacheCameraTransform.right;
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

        public override AimPosition UpdateBuildAimControls(Vector2 aimAxes, BuildingEntity prefab)
        {
            // Instantiate constructing building
            if (ConstructingBuildingEntity == null)
            {
                InstantiateConstructingBuilding(prefab);
                buildYRotate = 0f;
            }
            // Rotate by keys
            Vector3 buildingAngles = Vector3.zero;
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
            {
                if (buildRotationSnap)
                {
                    if (InputManager.GetButtonDown("RotateLeft"))
                        buildYRotate -= buildRotateAngle;
                    if (InputManager.GetButtonDown("RotateRight"))
                        buildYRotate += buildRotateAngle;
                    // Make Y rotation set to 0, 90, 180
                    buildingAngles.y = buildYRotate = Mathf.Round(buildYRotate / buildRotateAngle) * buildRotateAngle;
                }
                else
                {
                    float deltaTime = Time.deltaTime;
                    if (InputManager.GetButton("RotateLeft"))
                        buildYRotate -= buildRotateSpeed * deltaTime;
                    if (InputManager.GetButton("RotateRight"))
                        buildYRotate += buildRotateSpeed * deltaTime;
                    // Rotate by set angles
                    buildingAngles.y = buildYRotate;
                }
                ConstructingBuildingEntity.BuildYRotation = buildYRotate;
            }
            ConstructingBuildingEntity.Rotation = Quaternion.Euler(buildingAngles);
            // Find position to place building
            if (InputManager.useMobileInputOnNonMobile || Application.isMobilePlatform)
                FindAndSetBuildingAreaByAxes(aimAxes);
            else
                FindAndSetBuildingAreaByMousePosition();
            return AimPosition.CreatePosition(ConstructingBuildingEntity.Position);
        }

        public override void FinishBuildAimControls(bool isCancel)
        {
            if (isCancel)
                CancelBuild();
        }

        public void FindAndSetBuildingAreaByAxes(Vector2 aimAxes)
        {
            Vector3 raycastPosition = CacheTransform.position + (GameplayUtils.GetDirectionByAxes(CacheGameplayCameraTransform, aimAxes.x, aimAxes.y) * ConstructingBuildingEntity.BuildDistance);
            LoopSetBuildingArea(physicFunctions.RaycastDown(raycastPosition, CurrentGameInstance.GetBuildLayerMask()));
        }

        public void FindAndSetBuildingAreaByMousePosition()
        {
            LoopSetBuildingArea(physicFunctions.RaycastPickObjects(CacheGameplayCamera, InputManager.MousePosition(), CurrentGameInstance.GetBuildLayerMask(), Vector3.Distance(CacheGameplayCameraTransform.position, MovementTransform.position) + ConstructingBuildingEntity.BuildDistance, out _));
        }

        /// <summary>
        /// Return true if found building area
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        protected bool LoopSetBuildingArea(int count)
        {
            ConstructingBuildingEntity.BuildingArea = null;
            ConstructingBuildingEntity.HitSurface = false;
            BuildingEntity buildingEntity;
            BuildingArea buildingArea;
            Transform tempTransform;
            Vector3 tempRaycastPoint;
            Vector3 snappedPosition = GetBuildingPlacePosition(ConstructingBuildingEntity.Position);
            for (int tempCounter = 0; tempCounter < count; ++tempCounter)
            {
                tempTransform = physicFunctions.GetRaycastTransform(tempCounter);
                if (ConstructingBuildingEntity.CacheTransform.root == tempTransform.root)
                {
                    // Hit collider which is part of constructing building entity, skip it
                    continue;
                }

                tempRaycastPoint = physicFunctions.GetRaycastPoint(tempCounter);
                snappedPosition = GetBuildingPlacePosition(tempRaycastPoint);

                if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                {
                    // Find ground position from upper position
                    bool hitAimmingObject = false;
                    Vector3 raycastOrigin = tempRaycastPoint + Vector3.up * BUILDING_CONSTRUCTING_GROUND_FINDING_DISTANCE * 0.5f;
                    RaycastHit[] groundHits = Physics.RaycastAll(raycastOrigin, Vector3.down, BUILDING_CONSTRUCTING_GROUND_FINDING_DISTANCE, CurrentGameInstance.GetBuildLayerMask());
                    for (int j = 0; j < groundHits.Length; ++j)
                    {
                        if (groundHits[j].transform == tempTransform)
                        {
                            tempRaycastPoint = groundHits[j].point;
                            snappedPosition = GetBuildingPlacePosition(tempRaycastPoint);
                            ConstructingBuildingEntity.Position = GetBuildingPlacePosition(snappedPosition);
                            hitAimmingObject = true;
                            break;
                        }
                    }
                    if (!hitAimmingObject)
                        continue;
                }
                else
                {
                    ConstructingBuildingEntity.Position = GetBuildingPlacePosition(snappedPosition);
                }

                buildingEntity = tempTransform.root.GetComponent<BuildingEntity>();
                buildingArea = tempTransform.GetComponent<BuildingArea>();
                if ((buildingArea == null || !ConstructingBuildingEntity.BuildingTypes.Contains(buildingArea.buildingType))
                    && buildingEntity == null)
                {
                    // Hit surface which is not building area or building entity
                    ConstructingBuildingEntity.BuildingArea = null;
                    ConstructingBuildingEntity.HitSurface = true;
                    break;
                }

                if (buildingArea == null || !ConstructingBuildingEntity.BuildingTypes.Contains(buildingArea.buildingType))
                {
                    // Skip because this area is not allowed to build the building that you are going to build
                    continue;
                }

                ConstructingBuildingEntity.BuildingArea = buildingArea;
                ConstructingBuildingEntity.HitSurface = true;
                return true;
            }
            ConstructingBuildingEntity.Position = GetBuildingPlacePosition(snappedPosition);
            return false;
        }

        protected Vector3 GetBuildingPlacePosition(Vector3 position)
        {
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
            {
                if (buildGridSnap)
                    position = new Vector3(Mathf.Round(position.x / buildGridSize) * buildGridSize, position.y, Mathf.Round(position.z / buildGridSize) * buildGridSize) + buildGridOffsets;
            }
            else
            {
                if (buildGridSnap)
                    position = new Vector3(Mathf.Round(position.x / buildGridSize) * buildGridSize, Mathf.Round(position.y / buildGridSize) * buildGridSize) + buildGridOffsets;
            }
            return position;
        }

        protected Vector2 XZ(Vector3 vector3)
        {
            return new Vector2(vector3.x, vector3.z);
        }
    }
}
