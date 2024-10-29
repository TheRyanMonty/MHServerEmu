﻿using System.Text;
using Gazillion;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Serialization;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Common;
using MHServerEmu.Games.DRAG.Generators.Regions;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Entities
{
    public class Transition : WorldEntity
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private string _transitionName = string.Empty;          // Seemingly unused
        private List<Destination> _destinationList = new();

        public IEnumerable<Destination> Destinations { get => _destinationList; }

        public TransitionPrototype TransitionPrototype { get => Prototype as TransitionPrototype; }

        public Transition(Game game) : base(game) { }

        public override bool Initialize(EntitySettings settings)
        {
            base.Initialize(settings);

            // old
            var destination = Destination.FindDestination(settings.Cell, TransitionPrototype);

            if (destination != null)
                _destinationList.Add(destination);

            return true;
        }

        public override void OnEnteredWorld(EntitySettings settings)
        {
            var transProto = TransitionPrototype;
            if (transProto.Waypoint != PrototypeId.Invalid)
            {
                var waypointHotspotRef = GameDatabase.GlobalsPrototype.WaypointHotspot;

                using EntitySettings hotspotSettings = ObjectPoolManager.Instance.Get<EntitySettings>();
                hotspotSettings.EntityRef = waypointHotspotRef;
                hotspotSettings.RegionId = Region.Id;
                hotspotSettings.Position = RegionLocation.Position;

                var inventory = GetInventory(InventoryConvenienceLabel.Summoned);
                if (inventory != null) hotspotSettings.InventoryLocation = new(Id, inventory.PrototypeDataRef);

                var hotspot = Game.EntityManager.CreateEntity(hotspotSettings);
                if (hotspot != null) hotspot.Properties[PropertyEnum.WaypointHotspotUnlock] = transProto.Waypoint;
            }

            if (transProto.Type == RegionTransitionType.Transition)
            {
                var area = Area;
                var entityRef = PrototypeDataRef;
                var cellRef = Cell.PrototypeDataRef;
                var region = Region;
                bool noDest = _destinationList.Count == 0;
                if (noDest && area.RandomInstances.Count > 0)
                    foreach(var instance in area.RandomInstances)
                    {
                        var instanceCell = GameDatabase.GetDataRefByAsset(instance.OriginCell);
                        if (instanceCell == PrototypeId.Invalid || cellRef != instanceCell) continue;
                        if (instance.OriginEntity != entityRef) continue;
                        var destination = Destination.DestinationFromTarget(instance.Target, region, transProto);
                        if (destination == null) continue;
                        _destinationList.Add(destination);
                        noDest = false;
                    }

                if (noDest)
                {
                    // TODO destination from region origin target
                    var targets = region.Targets;
                    if (targets.Count == 1)
                    {
                        var destination = Destination.DestinationFromTarget(targets[0].TargetId, region, TransitionPrototype);
                        if (destination != null)
                        {
                            _destinationList.Add(destination);
                            noDest = false;
                        }
                    }
                }

                // Get default region
                if (noDest)
                {
                    var targetRef = GameDatabase.GlobalsPrototype.DefaultStartTargetFallbackRegion;
                    var destination = Destination.DestinationFromTarget(targetRef, region, TransitionPrototype);
                    if (destination != null) _destinationList.Add(destination);
                }
            }
            else if (transProto.Type == RegionTransitionType.TransitionDirect)
            {
                var targetRef = transProto.DirectTarget;
                var destination = Destination.DestinationFromTargetRef(targetRef);
                if (destination != null) _destinationList.Add(destination);
            }

            base.OnEnteredWorld(settings);
        }

        public override bool Serialize(Archive archive)
        {
            bool success = base.Serialize(archive);

            //if (archive.IsTransient)
            success &= Serializer.Transfer(archive, ref _transitionName);
            success &= Serializer.Transfer(archive, ref _destinationList);

            return success;
        }

        protected override void BuildString(StringBuilder sb)
        {
            base.BuildString(sb);

            sb.AppendLine($"{nameof(_transitionName)}: {_transitionName}");
            for (int i = 0; i < _destinationList.Count; i++)
                sb.AppendLine($"{nameof(_destinationList)}[{i}]: {_destinationList[i]}");
        }

        public void ConfigureTowerGen(Transition transition)
        {
            Destination destination;

            if (_destinationList.Count == 0)
            {
                destination = new();
                _destinationList.Add(destination);
            }
            else
            {
                destination = _destinationList[0];
            }

            destination.EntityId = transition.Id;
            destination.EntityRef = transition.PrototypeDataRef;
            destination.Type = TransitionPrototype.Type;
        }

        public bool UseTransition(Player player)
        {
            // TODO: Separate teleport logic from Transition

            switch (TransitionPrototype.Type)
            {
                case RegionTransitionType.TransitionDirect:
                case RegionTransitionType.Transition:
                    Region region = player.GetRegion();
                    if (region == null) return Logger.WarnReturn(false, "UseTransition(): region == null");

                    if (_destinationList.Count == 0) return Logger.WarnReturn(false, "UseTransition(): No available destinations!");
                    if (_destinationList.Count > 1) Logger.Debug("UseTransition(): _destinationList.Count > 1");

                    Destination destination = _destinationList[0];

                    Logger.Trace($"Transition Destination Entity: {destination.EntityRef.GetName()}");

                    PrototypeId targetRegionProtoRef = destination.RegionRef;

                    // Check if our target is outside of the current region and we need to do a remote teleport
                    // TODO: Additional checks if we need to transfer (e.g. when transferring to another instance of the same region proto).
                    if (targetRegionProtoRef != PrototypeId.Invalid && region.PrototypeDataRef != targetRegionProtoRef)
                    {
                        if (TransitionPrototype.Type == RegionTransitionType.TransitionDirect)
                        {
                            var regionContext = player.PlayerConnection.RegionContext;
                            var regionProto = GameDatabase.GetPrototype<RegionPrototype>(targetRegionProtoRef);

                            if (regionProto.HasEndless())
                                regionContext.EndlessLevel = 1;

                            var properties = regionContext.Properties;
                            properties.Clear();
                            properties.CopyProperty(Properties, PropertyEnum.DifficultyTier);
                            properties.CopyProperty(Properties, PropertyEnum.RegionAffixDifficulty);
                            properties.CopyProperty(Properties, PropertyEnum.DangerRoomScenarioItemDbGuid);
                            regionContext.ItemRarity = Properties[PropertyEnum.ItemRarity];
                            regionContext.PlayerGuidParty = Properties[PropertyEnum.RestrictedToPlayerGuidParty];

                            if (Properties.HasProperty(PropertyEnum.RegionAffix))
                            {
                                regionContext.Affixes.Clear();
                                foreach (var kvp in Properties.IteratePropertyRange(PropertyEnum.RegionAffix))
                                {
                                    Property.FromParam(kvp.Key, 0, out PrototypeId affixRef);
                                    regionContext.Affixes.Add(affixRef);
                                }
                            }
                        }

                        return TeleportToRemoteTarget(player, destination.TargetRef);
                    }

                    // No need to transfer if we are already in the target region
                    return TeleportToLocalTarget(player, destination.TargetRef);

                case RegionTransitionType.TowerUp:
                case RegionTransitionType.TowerDown:
                    return TeleportToTransition(player, _destinationList[0].EntityId);

                case RegionTransitionType.Waypoint:
                    // TODO: Unlock waypoint
                    return true;

                case RegionTransitionType.ReturnToLastTown:
                    return TeleportToLastTown(player);

                default:
                    return Logger.WarnReturn(false, $"UseTransition(): Unimplemented region transition type {TransitionPrototype.Type}");
            }
        }

        public static bool TeleportToRemoteTarget(Player player, PrototypeId targetProtoRef)
        {
            Logger.Trace($"TeleportToRemoteTarget(): targetProtoRef={targetProtoRef.GetNameFormatted()}");
            player.PlayerConnection.MoveToTarget(targetProtoRef);
            return true;
        }

        public static bool TeleportToLocalTarget(Player player, PrototypeId targetProtoRef)
        {
            var targetProto = targetProtoRef.As<RegionConnectionTargetPrototype>();
            if (targetProto == null) return Logger.WarnReturn(false, "TeleportToLocalTarget(): targetProto == null");

            Region region = player.GetRegion();
            if (region == null) return Logger.WarnReturn(false, "TeleportToLocalTarget(): region == null");

            Vector3 position = Vector3.Zero;
            Orientation orientation = Orientation.Zero;

            if (region.FindTargetLocation(ref position, ref orientation,
                targetProto.Area, GameDatabase.GetDataRefByAsset(targetProto.Cell), targetProto.Entity) == false)
            {
                return Logger.WarnReturn(false, $"TeleportToLocalTarget(): Failed to find target location for target {targetProtoRef.GetName()}");
            }

            if (player.CurrentAvatar.Area?.PrototypeDataRef != targetProto.Area)
                region.PlayerBeginTravelToAreaEvent.Invoke(new(player, targetProto.Area));

            player.SendMessage(NetMessageOneTimeSnapCamera.DefaultInstance);    // Disables camera interpolation for movement

            ChangePositionResult result = player.CurrentAvatar.ChangeRegionPosition(position, orientation, ChangePositionFlags.Teleport);
            return result == ChangePositionResult.PositionChanged || result == ChangePositionResult.Teleport;
        }

        public static bool TeleportToTransition(Player player, ulong transitionEntityId)
        {
            // This looks quite similar to TeleportToLocalTarget(), maybe we should merge them

            Transition transition = player.Game.EntityManager.GetEntity<Transition>(transitionEntityId);
            if (transition == null) return Logger.WarnReturn(false, "TeleportToTransition(): transition == null");

            TransitionPrototype transitionProto = transition.TransitionPrototype;
            if (transitionProto == null) return Logger.WarnReturn(false, "TeleportToTransition(): transitionProto == null");

            Vector3 targetPos = transition.RegionLocation.Position;
            Orientation targetRot = transition.RegionLocation.Orientation;
            targetPos += transitionProto.CalcSpawnOffset(targetRot);

            uint cellId = transition.Properties[PropertyEnum.MapCellId];
            uint areaId = transition.Properties[PropertyEnum.MapAreaId];
            Logger.Debug($"TeleportToTransition(): targetPos={targetPos}, areaId={areaId}, cellId={cellId}");

            ChangePositionResult result = player.CurrentAvatar.ChangeRegionPosition(targetPos, targetRot, ChangePositionFlags.Teleport);
            return result == ChangePositionResult.PositionChanged || result == ChangePositionResult.Teleport;
        }

        public static bool TeleportToLastTown(Player player)
        {
            // TODO: Teleport to the last saved hub
            Logger.Trace($"TeleportToLastTown(): Destination LastTown");
            player.PlayerConnection.MoveToTarget(GameDatabase.GlobalsPrototype.DefaultStartTargetFallbackRegion);
            return true;
        }
    }
}
