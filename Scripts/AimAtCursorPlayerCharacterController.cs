using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{
    public class AimAtCursorPlayerCharacterController : BasePlayerCharacterController
    {
        protected bool isLeftHandAttacking;
        protected bool isSprinting;
        protected BaseCharacterEntity targetCharacter;
        protected BasePlayerCharacterEntity targetPlayer;
        protected BaseMonsterCharacterEntity targetMonster;
        protected NpcEntity targetNpc;
        protected ItemDropEntity targetItemDrop;
        protected BuildingEntity targetBuilding;
        protected VehicleEntity targetVehicle;
        protected HarvestableEntity targetHarvestable;

        public NearbyEntityDetector ActivatableEntityDetector { get; protected set; }
        public NearbyEntityDetector ItemDropEntityDetector { get; protected set; }
        protected RaycastHit[] raycasts = new RaycastHit[512];
        protected RaycastHit2D[] raycasts2D = new RaycastHit2D[512];

        protected override void Awake()
        {
            base.Awake();
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
            // This entity detector will be find item drop entities to use when pressed pickup key
            tempGameObject = new GameObject("_ItemDropEntityDetector");
            ItemDropEntityDetector = tempGameObject.AddComponent<NearbyEntityDetector>();
            ItemDropEntityDetector.detectingRadius = CurrentGameInstance.pickUpItemDistance;
            ItemDropEntityDetector.findItemDrop = true;
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
            if (!PlayerCharacterEntity || !PlayerCharacterEntity.IsOwnerClient)
                return;

            base.Update();

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
                        PlayerCharacterEntity.RequestNpcActivate(targetNpc.ObjectId);
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
                        PlayerCharacterEntity.RequestEnterVehicle(targetVehicle.ObjectId);
                    }
                    else
                    {
                        // Enter warp, For some warp portals that `warpImmediatelyWhenEnter` is FALSE
                        PlayerCharacterEntity.RequestEnterWarp();
                    }
                }
                // Pick up nearby items
                if (InputManager.GetButtonDown("PickUpItem"))
                {
                    targetItemDrop = null;
                    if (ItemDropEntityDetector.itemDrops.Count > 0)
                        targetItemDrop = ItemDropEntityDetector.itemDrops[0];
                    if (targetItemDrop != null)
                        PlayerCharacterEntity.RequestPickupItem(targetItemDrop.ObjectId);
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
                    PlayerCharacterEntity.RequestExitVehicle();
                }
                if (InputManager.GetButtonDown("SwitchEquipWeaponSet"))
                {
                    // Switch equip weapon set
                    PlayerCharacterEntity.RequestSwitchEquipWeaponSet((byte)(PlayerCharacterEntity.EquipWeaponSet + 1));
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
            // Update inputs
            UpdateLookInput();
            UpdateWASDInput();
            // Set sprinting state
            PlayerCharacterEntity.SetExtraMovement(isSprinting ? ExtraMovementState.IsSprinting : ExtraMovementState.None);
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

            // Attack when player pressed attack button
            if (!CacheUISceneGameplay.IsPointerOverUIObject() &&
                !UICharacterHotkeys.UsingHotkey &&
                (InputManager.GetButton("Fire1") || InputManager.GetButton("Attack") ||
                InputManager.GetButtonUp("Fire1") || InputManager.GetButtonUp("Attack")))
                UpdateFireInput();

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
                if (PlayerCharacterEntity.RequestAttack(isLeftHandAttacking))
                    isLeftHandAttacking = !isLeftHandAttacking;
            }
            else if (InputManager.GetButtonUp("Fire1") || InputManager.GetButtonUp("Attack"))
            {
                ConfirmBuild();
            }
        }

        protected void UpdateLookInput()
        {
            bool isMobile = InputManager.useMobileInputOnNonMobile || Application.isMobilePlatform;
            Vector2 lookDirection;
            if (isMobile)
            {
                // Turn character by joystick
                lookDirection = new Vector2(InputManager.GetAxis("Mouse X", false), InputManager.GetAxis("Mouse Y", false));
            }
            else
            {
                // Turn character follow cursor
                lookDirection = (InputManager.MousePosition() - CacheGameplayCameraControls.CacheCamera.WorldToScreenPoint(MovementTransform.position)).normalized;
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
                    float rotY = (Quaternion.LookRotation(
                        new Vector3(lookDirection.x, 0, lookDirection.y)).eulerAngles.y +
                        CacheGameplayCameraControls.CacheCameraTransform.eulerAngles.y);
                    PlayerCharacterEntity.SetLookRotation(Quaternion.Euler(0, rotY, 0));
                }
            }

            if (ConstructingBuildingEntity)
            {
                FindAndSetBuildingAreaFromMousePosition();
            }
        }

        protected void ReloadAmmo()
        {
            // Reload ammo at server
            if (!PlayerCharacterEntity.EquipWeapons.rightHand.IsAmmoFull())
                PlayerCharacterEntity.RequestReload(false);
            else if (!PlayerCharacterEntity.EquipWeapons.leftHand.IsAmmoFull())
                PlayerCharacterEntity.RequestReload(true);
        }

        public override void UseHotkey(int hotkeyIndex, Vector3? aimPosition)
        {
            if (hotkeyIndex < 0 || hotkeyIndex >= PlayerCharacterEntity.Hotkeys.Count)
                return;

            CancelBuild();
            buildingItemIndex = -1;
            ConstructingBuildingEntity = null;
            ClearQueueUsingSkill();

            CharacterHotkey hotkey = PlayerCharacterEntity.Hotkeys[hotkeyIndex];
            switch (hotkey.type)
            {
                case HotkeyType.Skill:
                    UseSkill(hotkey.relateId, aimPosition);
                    break;
                case HotkeyType.Item:
                    UseItem(hotkey.relateId, aimPosition);
                    break;
            }
        }

        protected void UseSkill(string id, Vector3? aimPosition)
        {
            BaseSkill skill = null;
            short skillLevel = 0;

            if (!GameInstance.Skills.TryGetValue(BaseGameData.MakeDataId(id), out skill) || skill == null ||
                !PlayerCharacterEntity.GetCaches().Skills.TryGetValue(skill, out skillLevel))
                return;
            
            bool isAttackSkill = skill.IsAttack();
            if (!aimPosition.HasValue)
            {
                if (PlayerCharacterEntity.RequestUseSkill(skill.DataId, isLeftHandAttacking) && isAttackSkill)
                {
                    // Requested to use attack skill then change attacking hand
                    isLeftHandAttacking = !isLeftHandAttacking;
                }
            }
            else
            {
                if (PlayerCharacterEntity.RequestUseSkill(skill.DataId, isLeftHandAttacking, aimPosition.Value) && isAttackSkill)
                {
                    // Requested to use attack skill then change attacking hand
                    isLeftHandAttacking = !isLeftHandAttacking;
                }
            }
        }

        protected void UseItem(string id, Vector3? aimPosition)
        {
            InventoryType inventoryType;
            int itemIndex;
            byte equipWeaponSet;
            CharacterItem characterItem;
            if (PlayerCharacterEntity.IsEquipped(
                id,
                out inventoryType,
                out itemIndex,
                out equipWeaponSet,
                out characterItem))
            {
                PlayerCharacterEntity.RequestUnEquipItem(inventoryType, (short)itemIndex, equipWeaponSet);
                return;
            }

            if (itemIndex < 0)
                return;

            BaseItem item = characterItem.GetItem();
            if (item == null)
                return;

            if (item.IsEquipment())
            {
                PlayerCharacterEntity.RequestEquipItem((short)itemIndex);
            }
            else if (item.IsUsable())
            {
                if (item.IsSkill())
                {
                    bool isAttackSkill = (item as ISkillItem).UsingSkill.IsAttack();
                    if (!aimPosition.HasValue)
                    {
                        if (PlayerCharacterEntity.RequestUseSkillItem((short)itemIndex, isLeftHandAttacking) && isAttackSkill)
                        {
                            // Requested to use attack skill then change attacking hand
                            isLeftHandAttacking = !isLeftHandAttacking;
                        }
                    }
                    else
                    {
                        if (PlayerCharacterEntity.RequestUseSkillItem((short)itemIndex, isLeftHandAttacking, aimPosition.Value) && isAttackSkill)
                        {
                            // Requested to use attack skill then change attacking hand
                            isLeftHandAttacking = !isLeftHandAttacking;
                        }
                    }
                }
                else
                {
                    PlayerCharacterEntity.RequestUseItem((short)itemIndex);
                }
            }
            else if (item.IsBuilding())
            {
                buildingItemIndex = itemIndex;
                ConstructingBuildingEntity = Instantiate((item as IBuildingItem).BuildingEntity);
                ConstructingBuildingEntity.SetupAsBuildMode();
                ConstructingBuildingEntity.CacheTransform.parent = null;
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

        public void FindAndSetBuildingAreaFromMousePosition()
        {
            int tempCount = 0;
            Vector3 tempVector3;
            // TODO: Test and implement mobile version
            switch (CurrentGameInstance.DimensionType)
            {
                case DimensionType.Dimension3D:
                    tempCount = PhysicUtils.SortedRaycastNonAlloc3D(CacheGameplayCameraControls.CacheCamera.ScreenPointToRay(InputManager.MousePosition()), raycasts, 100f, CurrentGameInstance.GetBuildLayerMask());
                    break;
                case DimensionType.Dimension2D:
                    tempVector3 = CacheGameplayCameraControls.CacheCamera.ScreenToWorldPoint(InputManager.MousePosition());
                    tempCount = PhysicUtils.SortedLinecastNonAlloc2D(tempVector3, tempVector3, raycasts2D, CurrentGameInstance.GetBuildLayerMask());
                    break;
            }
            LoopSetBuildingArea(tempCount);
        }

        /// <summary>
        /// Return true if found building area
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        private bool LoopSetBuildingArea(int count)
        {
            BuildingArea buildingArea;
            Transform tempTransform;
            Vector3 tempVector3;
            Vector3 tempOffset;
            for (int tempCounter = 0; tempCounter < count; ++tempCounter)
            {
                tempTransform = GetRaycastTransform(tempCounter);
                tempVector3 = GetRaycastPoint(tempCounter);
                tempOffset = tempVector3 - MovementTransform.position;
                tempVector3 =  MovementTransform.position + Vector3.ClampMagnitude(tempOffset, CurrentGameInstance.buildDistance);

                buildingArea = tempTransform.GetComponent<BuildingArea>();
                if (buildingArea == null ||
                    (buildingArea.Entity && buildingArea.GetObjectId() == ConstructingBuildingEntity.ObjectId) ||
                    !ConstructingBuildingEntity.buildingTypes.Contains(buildingArea.buildingType))
                {
                    // Skip because this area is not allowed to build the building that you are going to build
                    continue;
                }

                ConstructingBuildingEntity.BuildingArea = buildingArea;
                ConstructingBuildingEntity.CacheTransform.position = tempVector3;
                return true;
            }
            return false;
        }

        #region Pick functions
        public Transform GetRaycastTransform(int index)
        {
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                return raycasts[index].transform;
            return raycasts2D[index].transform;
        }

        public bool GetRaycastIsTrigger(int index)
        {
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                return raycasts[index].collider.isTrigger;
            return raycasts2D[index].collider.isTrigger;
        }

        public Vector3 GetRaycastPoint(int index)
        {
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                return raycasts[index].point;
            return raycasts2D[index].point;
        }
        #endregion
    }
}
