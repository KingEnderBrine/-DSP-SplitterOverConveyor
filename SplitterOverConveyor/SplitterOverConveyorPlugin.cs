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
            //if (!playerAction.ObjectIsSplitter(buildPreview.objId))
            //{
            //    return;
            //}
            ////TODOOO
            //if (playerAction.castObjId <= 0 || !playerAction.ObjectIsBelt(playerAction.castObjId))
            //{
            //    return;
            //}
            //
            //var castEntityData = playerAction.factory.GetEntityData(playerAction.castObjId);
            //var itemProto = LDB.items.Select(castEntityData.protoId);
            //var objectPose = playerAction.GetObjectPose(playerAction.castObjId);
            //
            ////TODOOO
            //GetAdjacentToSlotsBuildings(playerAction.factory, castEntityData.id);
            //var localGates = playerAction.GetLocalGates(buildPreview.objId);
            //
            //playerAction.DoDestructObject(castEntityData.id, out _);
            //
            //for (var i = 0; i < connections.Length; i++)
            //{
            //    var connection = connections[i];
            //    if (connection == 0)
            //    {
            //        continue;
            //    }
            //
            //    var connectionEntityData = playerAction.factory.GetEntityData(connection);
            //
            //    //Slot detection mostly coppied from `DetermineBuildPreviews`
            //    //Renamed what I could to meaningful names
            //    var entityDirection = playerAction.cursorTarget - connectionEntityData.pos;
            //    var num1 = (playerAction.altitude * 1.333333F + playerAction.factory.planet.realRadius + 0.2F) - playerAction.cursorTarget.magnitude;
            //    var num2 = 0.0f;
            //    var gatePos = Vector3.zero;
            //    var slot = -1;
            //    for (int index = 0; index < localGates.Length; ++index)
            //    {
            //        var startGatePos = objectPose.position + objectPose.rotation * localGates[index].position;
            //        var forwardDirection = objectPose.rotation * localGates[index].forward;
            //        var endGatePos = startGatePos + forwardDirection * 2f;
            //        var normalized = (endGatePos - objectPose.position).normalized;
            //        var num3 = 0.25f - Mathf.Abs((num1 - localGates[index].position.y) * 0.25F);
            //        var num4 = Vector3.Dot(-entityDirection.normalized, normalized) + num3;
            //        if (num4 > num2)
            //        {
            //            num2 = num4;
            //            slot = index;
            //            gatePos = startGatePos;
            //        }
            //    }
            //
            //    if (slot == -1)
            //    {
            //        continue;
            //    }
            //
            //    var connectionPrebuildData = new PrebuildData
            //    {
            //        protoId = (short)itemProto.ID,
            //        modelIndex = (short)itemProto.prefabDesc.modelIndex,
            //        pos = gatePos,
            //        pos2 = Vector3.zero,
            //        rot = prebuildData.rot,
            //        rot2 = Quaternion.identity,
            //    };
            //
            //    var objId = -playerAction.factory.AddPrebuildDataWithComponents(connectionPrebuildData);
            //    //First slot in belts is always output
            //    playerAction.factory.WriteObjectConn(buildPreview.objId, slot, i == 0, objId, i == 0 ? 1 : 0);
            //    playerAction.factory.WriteObjectConn(connection, i == 0 ? 1 : 0, i != 0, objId, i == 0 ? 0 : 1);
            //}
        }

        private static void GetAdjacentBuildingsNonAlloc(PlayerAction_Build playerAction, BuildPreview buildPreview, AdjacentBuilding[] splitterAdjacentBuildings)
        {
            var slotPoses = buildPreview.desc.slotPoses;
            for (var i = 0; i < 4; i++)
            {
                var slotPose = slotPoses[i];
                var snappedPos = playerAction.planetAux.mainGrid.SnapTo(slotPose.position);
                var count = playerAction.nearcdLogic.GetBuildingsInAreaNonAlloc(snappedPos, 0.01F, playerAction._tmp_ids);
                var entityData = EntityData.Null;
                var validBelt = false;
                var isOutput = false;
                var isBelt = false;
                if (count == 1)
                {
                    entityData = playerAction.factory.GetEntityData(playerAction._tmp_ids[0]);
                    isBelt = playerAction.ObjectIsBelt(entityData.id);
                    if (isBelt)
                    {
                        validBelt = true;
                        //isOutput;
                    }
                }

                splitterAdjacentBuildings[i] = new AdjacentBuilding
                {
                    slot = i,
                    entityData = entityData,
                    slotPos = slotPoses[i].position,
                    isOutput = isOutput,
                    validBelt = validBelt,
                    isBelt = isBelt
                };
            }
        }

        private static int CheckSplitterCollides(PlayerAction_Build playerAction, BuildPreview buildPreview, ColliderData buildCollider, int layerMask)
        {
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
            if (splitterAdjacentBuildings.Count(belt => belt.validBelt) + (middleBeltExists ? 1 : 0) >= overlapCount)
            {
                return (int)EBuildCondition.Collide + 1;
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
                InstanceLogger.LogInfo(string.Join(", ", splitterAdjacentBuildings.Select(el => el.entityData.id.ToString()).ToArray()));
                InstanceLogger.LogWarning(colliderIds[i]);
                return (int)EBuildCondition.Collide + 2;
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
                    return (int)EBuildCondition.Collide + 3;
                }

                if (!adjacentBuilding.validBelt)
                {
                    return (int)EBuildCondition.Collide + 4;
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
