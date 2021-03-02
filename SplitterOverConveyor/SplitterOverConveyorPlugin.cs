using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SplitterOverConveyor
{
    [BepInPlugin(GUID, "Splitter Over Conveyor", "2.0.0")]
    public class SplitterOverConveyorPlugin : BaseUnityPlugin
    {
        public const string GUID = "KingEnderBrine.SplitterOverConveyor";

        internal static SplitterOverConveyorPlugin Instance { get; private set; }
        internal static ManualLogSource InstanceLogger => Instance ? Instance.Logger : null;

        private static readonly AdjacentBuilding[] splitterAdjacentBuildings = new AdjacentBuilding[4];
        private static readonly Dictionary<int, int> requiredBelts = new Dictionary<int, int>();

        private void Awake()
        {
            Instance = this;

            new ILHook(AccessTools.Method(typeof(PlayerAction_Build), nameof(PlayerAction_Build.CheckBuildConditions)), CheckBuildConditionsIL).Apply();
            new ILHook(AccessTools.Method(typeof(PlayerAction_Build), nameof(PlayerAction_Build.CreatePrebuilds)), CreatePrebuildsIL).Apply();
        }

        private static void CheckBuildConditionsIL(ILContext il)
        {
            var c = new ILCursor(il);
            
            c.GotoNext(MoveType.After, x => x.MatchStloc(159));//159 - layerMask local var
            var layerMaskIndex = c.Index;

            c.GotoNext(x => x.MatchCallOrCallvirt(AccessTools.Method(typeof(Physics), "CheckBox", new[] { typeof(Vector3), typeof(Vector3), typeof(Quaternion), typeof(int), typeof(QueryTriggerInteraction) })));

            ILLabel afterIfLabel = null;
            c.GotoNext(x => x.MatchBrfalse(out afterIfLabel));

            ILLabel exitConditionCheckLabel = null;
            c.GotoNext(x => x.MatchBr(out exitConditionCheckLabel));

            c.Index = layerMaskIndex;

            c.Emit(OpCodes.Ldloc_3);//Will be used later in condition
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc_3);
            c.Emit(OpCodes.Ldloc, 154);//154 - buildCollider local var
            c.Emit(OpCodes.Ldloc, 159);//159 - layerMask local var
            c.Emit(OpCodes.Call, AccessTools.Method(typeof(SplitterOverConveyorPlugin), nameof(CheckSplitterCollides)));

            //Unnecessary complicated branching because I don't want to do local variables
            //With local variable it still would be branching, but without `Dup`s and `Pop`s
            c.Emit(OpCodes.Dup);
            var outerBranchLabel = il.DefineLabel();
            c.Emit(OpCodes.Ldc_I4_M1);
            c.Emit(OpCodes.Beq, outerBranchLabel);//Store to later fill operand with label
            
            var innerBranchLabel = il.DefineLabel();
            c.Emit(OpCodes.Dup);
            c.Emit(OpCodes.Brtrue, innerBranchLabel);

            c.Emit(OpCodes.Pop);//Pop leftover int from bracnhing
            c.Emit(OpCodes.Pop);
            c.Emit(OpCodes.Br, afterIfLabel);//If condition is ok skip another check

            c.Emit(OpCodes.Stfld, AccessTools.Field(typeof(BuildPreview), "condition"));
            innerBranchLabel.Target = c.Prev;
            c.Emit(OpCodes.Br, exitConditionCheckLabel);//If condition is not ok go to exit

            c.Emit(OpCodes.Pop);//Pop leftover from bracnhing
            outerBranchLabel.Target = c.Prev;
            c.Emit(OpCodes.Pop);
        }

        private static void CreatePrebuildsIL(ILContext il)
        {
            var c = new ILCursor(il);

            c.GotoNext(MoveType.After, x => x.MatchStfld(AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.objId))));

            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloca, 3);
            c.Emit(OpCodes.Ldloc, 1);
            c.Emit(OpCodes.Call, AccessTools.Method(typeof(SplitterOverConveyorPlugin), nameof(FillSplitterPrebuildData)));
        }

        private static void FillSplitterPrebuildData(PlayerAction_Build playerAction, ref PrebuildData prebuildData, BuildPreview buildPreview)
        {
            if (!playerAction.ObjectIsSplitter(buildPreview.objId))
            {
                return;
            }
            var middleBeltExists = playerAction.castObjId > 0;
            if (middleBeltExists && !playerAction.ObjectIsBelt(playerAction.castObjId))
            {
                return;
            }
            
            GetAdjacentBuildingsNonAlloc(playerAction, buildPreview, splitterAdjacentBuildings);
            if (middleBeltExists)
            {
                playerAction.DoDestructObject(playerAction.castObjId, out _);
            }

            for (var i = 0; i < 4; i++)
            {
                var adjacentBuilding = splitterAdjacentBuildings[i];
                if (adjacentBuilding.entityData.isNull)
                {
                    continue;
                }
                if (!adjacentBuilding.validBelt)
                {
                    continue;
                }
                var connectionPrebuildData = new PrebuildData
                {
                    protoId = adjacentBuilding.entityData.protoId,
                    modelIndex = adjacentBuilding.entityData.modelIndex,
                    pos = playerAction.previewPose.position + playerAction.previewPose.rotation * adjacentBuilding.slotPose.position,
                    pos2 = Vector3.zero,
                    rot = prebuildData.rot * adjacentBuilding.slotPose.rotation,
                    rot2 = Quaternion.identity,
                };
            
                var objId = -playerAction.factory.AddPrebuildDataWithComponents(connectionPrebuildData);
                //TODO: 0 is not always output for belt, change slot. (should be fine for newly placed belts, needs testing)
                InstanceLogger.LogWarning($"Slot {adjacentBuilding.splitterSlot} {adjacentBuilding.beltSlot}");
                playerAction.factory.WriteObjectConn(buildPreview.objId, adjacentBuilding.splitterSlot, !adjacentBuilding.isOutput, objId, adjacentBuilding.isOutput ? 0 : 1);

                //playerAction.factory.WriteObjectConn(adjacentBuilding.entityData.id, adjacentBuilding.beltSlot, adjacentBuilding.isOutput, objId, adjacentBuilding.isOutput ? 1 : 0);
            }
        }

        private static void GetAdjacentBuildingsNonAlloc(PlayerAction_Build playerAction, BuildPreview buildPreview, AdjacentBuilding[] splitterAdjacentBuildings)
        {
            var slotPoses = buildPreview.desc.slotPoses;
            for (var i = 0; i < 4; i++)
            {
                var slotPose = slotPoses[i];
                var snappedPos = playerAction.planetAux.Snap(playerAction.previewPose.position + playerAction.previewPose.rotation * (slotPose.position + slotPose.forward * PlanetGrid.kAltGrid), false, false);
                var count = playerAction.nearcdLogic.GetBuildingsInAreaNonAlloc(snappedPos, 0.1F, playerAction._tmp_ids);
                var entityData = EntityData.Null;
                var validBelt = false;
                var isOutput = false;
                var isBelt = false;
                var beltSlot = -1;

                var objId = 0;

                if (count == 1 && playerAction._tmp_ids[0] > 0)
                {
                    objId = playerAction._tmp_ids[0];
                    
                }
                if (count == 2 && buildPreview.objId != 0)
                {
                    if (Mathf.Abs(playerAction._tmp_ids[0]) == Mathf.Abs(buildPreview.objId))
                    {
                        objId = playerAction._tmp_ids[1];
                    }
                    else if (Mathf.Abs(playerAction._tmp_ids[1]) == Mathf.Abs(buildPreview.objId))
                    {
                        objId = playerAction._tmp_ids[0];
                    }
                }
                if (objId != 0)
                {
                    //TODO objId can be < 0 which will throw an exception
                    entityData = playerAction.factory.GetEntityData(objId);
                    isBelt = playerAction.ObjectIsBelt(entityData.id);
                    if (isBelt)
                    {
                        ValidateBelt(playerAction, slotPose, entityData, out validBelt, out isOutput, out beltSlot);
                    }
                }

                splitterAdjacentBuildings[i] = new AdjacentBuilding
                {
                    splitterSlot = i,
                    entityData = entityData,
                    slotPose = slotPose,
                    isOutput = isOutput,
                    validBelt = validBelt,
                    isBelt = isBelt,
                    beltSlot = beltSlot
                };
            }
        }

        private static void ValidateBelt(PlayerAction_Build playerAction, Pose slotPose, EntityData entityData, out bool validBelt, out bool isOutput, out int beltSlot)
        {
            //TODO: detect which belt slot to use
            beltSlot = -1;
            isOutput = false;
            validBelt = false;

            var slotDirection = playerAction.previewPose.rotation * slotPose.forward;

            var belt = playerAction.factory.cargoTraffic.beltPool[entityData.beltId];
            var cargoPath = playerAction.factory.cargoTraffic.GetCargoPath(belt.segPathId);
            var beltInputRotation = cargoPath.pointRot[belt.segIndex];
            var beltOutputRotation = cargoPath.pointRot[belt.segIndex + belt.segLength - 1];

            var beltIsStraight = Vector3.Dot(beltInputRotation * Vector3.forward, beltOutputRotation * Vector3.forward) > 0.5;

            if (beltIsStraight)
            {
                var dot = Vector3.Dot(beltInputRotation * Vector3.forward, slotDirection);
                if (Math.Abs(dot) > 0.5F)
                {
                    validBelt = true;
                    isOutput = dot > 0.5F;
                    return;
                }
                return;
            }

            if (Vector3.Dot(beltInputRotation * Vector3.forward, slotDirection) > 0.5F)
            {
                validBelt = true;
                isOutput = true;
                return;
            }
            if (Vector3.Dot(beltOutputRotation * Vector3.forward, slotDirection) < -0.5F)
            {
                validBelt = true;
                isOutput = false;
                return;
            }
        }


        private static int CheckSplitterCollides(PlayerAction_Build playerAction, BuildPreview buildPreview, ColliderData buildCollider, int layerMask)
        {
            if (playerAction.castObjId < 0)
            {
                return -1;
            }
            if (!buildPreview.desc.isSplitter)
            {
                return -1;
            }

            var middleBeltExists = playerAction.castObjId > 0;

            if (middleBeltExists && !playerAction.ObjectIsBelt(playerAction.castObjId))
            {
                return -1;
            }
            var castEntityData = playerAction.factory.GetEntityData(playerAction.castObjId);

            var overlapCount = Physics.OverlapBoxNonAlloc(buildCollider.pos, buildCollider.ext, playerAction._tmp_cols, buildCollider.q, layerMask, QueryTriggerInteraction.Collide);
            if (overlapCount > 4 + (middleBeltExists ? 1 : 0))
            {
                return (int)EBuildCondition.Collide;
            }

            GetAdjacentBuildingsNonAlloc(playerAction, buildPreview, splitterAdjacentBuildings);

            var colliderIds = new int[overlapCount];
            for (var i = 0; i < overlapCount; i++)
            {
                colliderIds[i] = playerAction.nearcdLogic.FindColliderId(playerAction._tmp_cols[i]);
            }

            for (var i = 0; i < overlapCount; i++)
            {
                if (splitterAdjacentBuildings.Any(el => el.entityData.colliderId == colliderIds[i]))
                {
                    continue;
                }
                if (middleBeltExists && colliderIds[i] == castEntityData.colliderId)
                {
                    continue;
                }
                return (int)EBuildCondition.Collide;
            }

            requiredBelts.Clear();
            for (var i = 0; i < splitterAdjacentBuildings.Length; i++)
            {
                var adjacentBuilding = splitterAdjacentBuildings[i];
                if (adjacentBuilding.entityData.isNull)
                {
                    continue;
                }

                if (!adjacentBuilding.isBelt && colliderIds.Contains(adjacentBuilding.entityData.colliderId))
                {
                    return (int)EBuildCondition.Collide;
                }

                if (!adjacentBuilding.validBelt)
                {
                    return (int)EBuildCondition.BeltCannotConnectToBuilding;
                }

                if (requiredBelts.TryGetValue(adjacentBuilding.entityData.protoId, out var count))
                {
                    requiredBelts[adjacentBuilding.entityData.protoId] = count;
                } 
                else
                {
                    requiredBelts[adjacentBuilding.entityData.protoId] = 1;
                }
            }

            if (middleBeltExists)
            {
                if (requiredBelts.TryGetValue(castEntityData.protoId, out var count))
                {
                    requiredBelts[castEntityData.protoId] = count - 1;
                }
            }

            foreach (var row in requiredBelts)
            {
                if (playerAction.player.package.GetItemCount(row.Key) < row.Value)
                {
                    return (int)EBuildCondition.NotEnoughItem;
                }
            }

            return 0;
        }
    }
}
