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
    [BepInPlugin(GUID, "Splitter Over Conveyor", "1.1.0")]
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
                    pos = adjacentBuilding.slotPos,
                    pos2 = Vector3.zero,
                    rot = prebuildData.rot,
                    rot2 = Quaternion.identity,
                };
            
                var objId = -playerAction.factory.AddPrebuildDataWithComponents(connectionPrebuildData);
                //First slot in belts is always output
                playerAction.factory.WriteObjectConn(buildPreview.objId, adjacentBuilding.slot, !adjacentBuilding.isOutput, objId, adjacentBuilding.isOutput ? 0 : 1);
                playerAction.factory.WriteObjectConn(adjacentBuilding.entityData.id, adjacentBuilding.isOutput ? 0 : 1, adjacentBuilding.isOutput, objId, adjacentBuilding.isOutput ? 1 : 0);
            }
        }

        private static void GetAdjacentBuildingsNonAlloc(PlayerAction_Build playerAction, BuildPreview buildPreview, AdjacentBuilding[] splitterAdjacentBuildings)
        {
            var slotPoses = buildPreview.desc.slotPoses;
            for (var i = 0; i < 4; i++)
            {
                var slotPose = slotPoses[i];
                var snappedPos = playerAction.planetAux.Snap(playerAction.previewPose.position + playerAction.previewPose.rotation * (slotPose.position + slotPose.forward * 1.3F), false, false);
                var count = playerAction.nearcdLogic.GetBuildingsInAreaNonAlloc(snappedPos, 0.1F, playerAction._tmp_ids);
                var entityData = EntityData.Null;
                var validBelt = false;
                var isOutput = false;
                var isBelt = false;

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
                    entityData = playerAction.factory.GetEntityData(objId);
                    isBelt = playerAction.ObjectIsBelt(entityData.id);
                    if (isBelt)
                    {
                        ValidateBelt(playerAction, slotPose, entityData, out validBelt, out isOutput);
                    }
                }

                splitterAdjacentBuildings[i] = new AdjacentBuilding
                {
                    slot = i,
                    entityData = entityData,
                    slotPos = playerAction.previewPose.position + playerAction.previewPose.rotation * slotPose.position,
                    isOutput = isOutput,
                    validBelt = validBelt,
                    isBelt = isBelt
                };
            }
        }

        private static void ValidateBelt(PlayerAction_Build playerAction, Pose slotPose, EntityData entityData, out bool validBelt, out bool isOutput)
        {
            playerAction.factory.ReadObjectConn(entityData.id, 0, out _, out var outputObjId, out _);

            if (playerAction.castObjId > 0 && outputObjId == playerAction.castObjId)
            {
                validBelt = true;
                isOutput = false;
                return;
            }

            playerAction.factory.ReadObjectConn(entityData.id, 1, out _, out var inputObjId, out _);
            
            if (playerAction.castObjId > 0 && inputObjId == playerAction.castObjId)
            {
                validBelt = true;
                isOutput = true;
                return;
            }

            if (outputObjId <= 0 && inputObjId <= 0)
            {
                validBelt = false;
                isOutput = false;
                return;
            }

            var slotForward = playerAction.previewPose.rotation * slotPose.forward;
            //TODO: objId can be less than 0 in case of prebuilds
            var outputObjData = playerAction.factory.GetEntityData(outputObjId);
            var outputForward = (outputObjData.pos - entityData.pos).normalized;
            var dot = Vector3.Dot(slotForward, outputForward);
            if (dot > 0.5)
            {
                validBelt = true;
                isOutput = false;
                return;
            }
            else if (dot < -0.5)
            {
                validBelt = true;
                isOutput = true;
                return;
            }

            var inputObjData = playerAction.factory.GetEntityData(inputObjId);
            
            var inputForward = (inputObjData.pos - entityData.pos).normalized;
            dot = Vector3.Dot(slotForward, inputForward);
            if (dot > 0.5)
            {
                validBelt = true;
                isOutput = true;
                return;
            }
            else if (dot < -0.5)
            {
                validBelt = true;
                isOutput = false;
                return;
            }

            validBelt = false;
            isOutput = false;
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
            if (overlapCount == 0)
            {
                return 0;
            }
            if (overlapCount > 4 + (middleBeltExists ? 1 : 0))
            {
                return (int)EBuildCondition.Collide;
            }

            GetAdjacentBuildingsNonAlloc(playerAction, buildPreview, splitterAdjacentBuildings);
            if (splitterAdjacentBuildings.Count(belt => belt.validBelt) + (middleBeltExists ? 1 : 0) > overlapCount)
            {
                return (int)EBuildCondition.Collide;
            }

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
                    return (int)EBuildCondition.Collide;
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
