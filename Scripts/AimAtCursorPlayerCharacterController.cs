using System.Collections;
using System.Collections.Generic;
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
            if (GenericUtils.IsFocusInputField())
                return;

            if (PlayerCharacterEntity.IsDead())
                return;

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
                        PlayerCharacterEntity.CallServerNpcActivate(targetNpc.ObjectId);
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
            PlayerCharacterEntity.SetExtraMovement(isSprinting ? ExtraMovementState.IsSprinting : ExtraMovementState.None);
            PlayerCharacterEntity.AimPosition = PlayerCharacterEntity.GetDefaultAttackAimPosition(ref isLeftHandAttacking);
        }

        protected void UpdateWASDInput()
        {
            // If mobile platforms, don't receive input raw to make it smooth
            bool raw = !InputManager.useMobileInputOnNonMobile && !Application.isMobilePlatform;
            Vector3 moveDirection = GetMoveDirection(InputManager.GetAxis("Horizontal", raw), InputManager.GetAxis("Vertical", raw));
            moveDirection.Normalize();

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
            if (!avoidAttackWhileCursorOverUI && !UICharacterHotkeys.UsingHotkey &&
                (InputManager.GetButton("Fire1") || InputManager.GetButton("Attack") ||
                InputManager.GetButtonUp("Fire1") || InputManager.GetButtonUp("Attack")))
                UpdateFireInput();

            // No pointer over ui and all attack key released, stop avoid attack inputs
            if (avoidAttackWhileCursorOverUI && !isPointerOverUIObject &&
                !InputManager.GetButton("Fire1") && !InputManager.GetButton("Attack"))
                avoidAttackWhileCursorOverUI = false;

            // Always forward
            MovementState movementState = Vector3.Angle(moveDirection, MovementTransform.forward) < 120 ?
                MovementState.Forward : MovementState.Backward;
            if (InputManager.GetButtonDown("Jump"))
                movementState |= MovementState.IsJump;
            PlayerCharacterEntity.KeyMovement(moveDirection, movementState);
        }

        protected void UpdateFireInput()
        {
            if (!ConstructingBuildingEntity)
            {
                if (PlayerCharacterEntity.CallServerAttack(isLeftHandAttacking))
                    isLeftHandAttacking = !isLeftHandAttacking;
            }
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
                for (int i = 0; i < pickedCount; ++i)
                {
                    tempTransform = physicFunctions.GetRaycastTransform(i);
                    tempGameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (tempGameEntity != null)
                    {
                        foundTargetEntity = true;
                        CacheUISceneGameplay.SetTargetEntity(tempGameEntity.Entity);
                        PlayerCharacterEntity.SetTargetEntity(tempGameEntity.Entity);
                        SelectedEntity = tempGameEntity.Entity;
                        if (tempGameEntity.Entity != PlayerCharacterEntity.Entity)
                        {
                            if (tempGameEntity is IDamageableEntity)
                                tempTargetPosition = (tempGameEntity as IDamageableEntity).OpponentAimTransform.position;
                            else
                                tempTargetPosition = tempGameEntity.GetTransform().position;
                            if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                lookDirection = (tempTargetPosition - CacheTransform.position).normalized;
                            else
                                lookDirection = (XZ(tempTargetPosition) - XZ(CacheTransform.position)).normalized;
                        }
                        break;
                    }
                    else
                    {
                        if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                            lookDirection = (physicFunctions.GetRaycastPoint(i) - CacheTransform.position).normalized;
                        else
                            lookDirection = (XZ(physicFunctions.GetRaycastPoint(i)) - XZ(CacheTransform.position)).normalized;
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
                for (int i = 0; i < pickedCount; ++i)
                {
                    tempTransform = physicFunctions.GetRaycastTransform(i);
                    tempGameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (tempGameEntity != null)
                    {
                        foundTargetEntity = true;
                        CacheUISceneGameplay.SetTargetEntity(tempGameEntity.Entity);
                        PlayerCharacterEntity.SetTargetEntity(tempGameEntity.Entity);
                        SelectedEntity = tempGameEntity.Entity;
                        if (tempGameEntity.Entity != PlayerCharacterEntity.Entity)
                        {
                            if (tempGameEntity is IDamageableEntity)
                                tempTargetPosition = (tempGameEntity as IDamageableEntity).OpponentAimTransform.position;
                            else
                                tempTargetPosition = tempGameEntity.GetTransform().position;
                            if (!doNotTurnToPointingEntity)
                            {
                                if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                    lookDirection = (tempTargetPosition - CacheTransform.position).normalized;
                                else
                                    lookDirection = (XZ(tempTargetPosition) - XZ(CacheTransform.position)).normalized;
                            }
                        }
                        break;
                    }
                    else
                    {
                        if (!doNotTurnToPointingEntity)
                        {
                            if (GameInstance.Singleton.DimensionType == DimensionType.Dimension2D)
                                lookDirection = (physicFunctions.GetRaycastPoint(i) - CacheTransform.position).normalized;
                            else
                                lookDirection = (XZ(physicFunctions.GetRaycastPoint(i)) - XZ(CacheTransform.position)).normalized;
                        }
                    }
                }
            }
            if (!foundTargetEntity)
            {
                CacheUISceneGameplay.SetTargetEntity(null);
                PlayerCharacterEntity.SetTargetEntity(null);
                SelectedEntity = null;
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
                PlayerCharacterEntity.CallServerReload(false);
            else if (!PlayerCharacterEntity.EquipWeapons.leftHand.IsAmmoFull())
                PlayerCharacterEntity.CallServerReload(true);
        }

        public override void UseHotkey(HotkeyType type, string relateId, Vector3? aimPosition)
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

        protected void UseSkill(string id, Vector3? aimPosition)
        {
            BaseSkill skill;
            short skillLevel;

            if (!GameInstance.Skills.TryGetValue(BaseGameData.MakeDataId(id), out skill) || skill == null ||
                !PlayerCharacterEntity.GetCaches().Skills.TryGetValue(skill, out skillLevel))
                return;

            bool isAttackSkill = skill.IsAttack();
            AimPosition skillAimPosition = AimPosition.Create(aimPosition);
            if (PlayerCharacterEntity.CallServerUseSkill(skill.DataId, isLeftHandAttacking, skillAimPosition) && isAttackSkill)
            {
                isLeftHandAttacking = !isLeftHandAttacking;
            }
        }

        protected void UseItem(string id, Vector3? aimPosition)
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
                bool isAttackSkill = (item as ISkillItem).UsingSkill.IsAttack();
                AimPosition skillAimPosition = AimPosition.Create(aimPosition);
                if (PlayerCharacterEntity.CallServerUseSkillItem((short)itemIndex, isLeftHandAttacking, skillAimPosition) && isAttackSkill)
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

        public override Vector3? UpdateBuildAimControls(Vector2 aimAxes, BuildingEntity prefab)
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
            }
            ConstructingBuildingEntity.Rotation = Quaternion.Euler(buildingAngles);
            // Find position to place building
            if (InputManager.useMobileInputOnNonMobile || Application.isMobilePlatform)
                FindAndSetBuildingAreaByAxes(aimAxes);
            else
                FindAndSetBuildingAreaByMousePosition();
            return ConstructingBuildingEntity.Position;
        }

        public override void FinishBuildAimControls(bool isCancel)
        {
            if (isCancel)
                CancelBuild();
        }

        public void FindAndSetBuildingAreaByAxes(Vector2 aimAxes)
        {
            Vector3 raycastPosition = CacheTransform.position + (GameplayUtils.GetDirectionByAxes(CacheGameplayCameraTransform, aimAxes.x, aimAxes.y) * ConstructingBuildingEntity.BuildDistance);
            LoopSetBuildingArea(physicFunctions.RaycastDown(raycastPosition, CurrentGameInstance.GetBuildLayerMask()), raycastPosition);
        }

        public void FindAndSetBuildingAreaByMousePosition()
        {
            Vector3 worldPosition2D;
            LoopSetBuildingArea(physicFunctions.RaycastPickObjects(CacheGameplayCamera, InputManager.MousePosition(), CurrentGameInstance.GetBuildLayerMask(), 100f, out worldPosition2D), worldPosition2D);
        }

        /// <summary>
        /// Return true if found building area
        /// </summary>
        /// <param name="count"></param>
        /// <param name="raycastPosition"></param>
        /// <returns></returns>
        protected bool LoopSetBuildingArea(int count, Vector3 raycastPosition)
        {
            IGameEntity gameEntity;
            BuildingArea buildingArea;
            Transform tempTransform;
            Vector3 tempVector3;
            for (int tempCounter = 0; tempCounter < count; ++tempCounter)
            {
                tempTransform = physicFunctions.GetRaycastTransform(tempCounter);
                tempVector3 = GameplayUtils.ClampPosition(CacheTransform.position, physicFunctions.GetRaycastPoint(tempCounter), ConstructingBuildingEntity.BuildDistance);
                if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                    tempVector3.y = physicFunctions.GetRaycastPoint(tempCounter).y;

                buildingArea = tempTransform.GetComponent<BuildingArea>();
                if (buildingArea == null)
                {
                    gameEntity = tempTransform.GetComponent<IGameEntity>();
                    if (gameEntity == null || gameEntity.Entity != ConstructingBuildingEntity)
                    {
                        // Hit something and it is not part of constructing building entity, assume that it is ground
                        ConstructingBuildingEntity.BuildingArea = null;
                        ConstructingBuildingEntity.Position = GetBuildingPlacePosition(tempVector3);
                        break;
                    }
                    continue;
                }

                if (buildingArea.IsPartOfBuildingEntity(ConstructingBuildingEntity) ||
                    !ConstructingBuildingEntity.BuildingTypes.Contains(buildingArea.buildingType))
                {
                    // Skip because this area is not allowed to build the building that you are going to build
                    continue;
                }

                ConstructingBuildingEntity.BuildingArea = buildingArea;
                ConstructingBuildingEntity.Position = GetBuildingPlacePosition(tempVector3);
                return true;
            }
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
            {
                ConstructingBuildingEntity.BuildingArea = null;
                ConstructingBuildingEntity.Position = GetBuildingPlacePosition(raycastPosition);
            }
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
