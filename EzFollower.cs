using ImGuiNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using Input = ExileCore.Input;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using GameOffsets;
using GameOffsets.Native;
using System.Windows.Forms;
using Positioned = ExileCore.PoEMemory.Components.Positioned;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using SharpDX;
using System.Numerics;
using Newtonsoft.Json;
using System.IO;


namespace EzFollower;

public class EzFollower : BaseSettingsPlugin<EzFollowerSettings>
{
    public const float GridToWorld = PoeMapExtension.TileToWorldConversion / (float)PoeMapExtension.TileToGridConversion;
    public const float WorldToGrid = 1 / GridToWorld;
    private float[][] _heightData;
    private long lastRun;
    private long portalDelayTime;
    private long reviveDelayTime;
    private PathFinder pathFinder;
    private List<Vector2i> path;
    private SharpDX.RectangleF _rect;
    private static readonly Random _rng = new();
    private FollowerState _state = FollowerState.SearchingForLeader;
    private bool isHideout = false;
    private string[] excludeZones =
    {
        "hideout",
        "kingsmarch",
        "vault"
    };


    public override bool Initialise()
    {
        //Perform one-time initialization here

        //Maybe load you custom config (only do so if builtin settings are inadequate for the job)
        //var configPath = Path.Join(ConfigDirectory, "custom_config.txt");
        //if (File.Exists(configPath))
        //{
        //    var data = File.ReadAllText(configPath);
        //}
        lastRun = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _rect = GameController.Window.GetWindowRectangle() with {Location = SharpDX.Vector2.Zero};

        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        _heightData = GameController.IngameState.Data.RawTerrainHeightData;
        pathFinder = new PathFinder(GameController.IngameState.Data.RawPathfindingData, new[] { 1, 2, 3, 4, 5 });
        portalDelayTime = 0;
        reviveDelayTime = 0;
        isHideout = GameController.Area?.CurrentArea?.IsHideout == true;
        path = null;
        _state = FollowerState.SearchingForLeader;

        // var isTown = GameController.Area?.CurrentArea?.IsTown == true;
        //Perform once-per-zone processing here
        //TODO: Auto stash. Auto Link. Auto Flask.
    }

    public override Job Tick()
    {
        //Perform non-render-related work here, e.g. position calculation.
        //This method is still called on every frame, so to really gain
        //an advantage over just throwing everything in the Render method
        //you have to return a custom job, but this is a bit of an advanced technique
        //here's how, just in case:
        //return new Job($"{nameof(EzFollower)}MainJob", () =>
        //{
        //    var a = Math.Sqrt(7);
        //});

        //otherwise, just run your code here
        //var a = Math.Sqrt(7);
        if (GameController.IsLoading) return null;

        if (!GameController.Player.IsAlive)
        {
            if (reviveDelayTime == 0)
            {
                reviveDelayTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _rng.Next(750, 1500);
            }
            else if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > reviveDelayTime)
            {
                var revivePanel = GameController.IngameState.IngameUi.ResurrectPanel;
                if (Settings.EnableAutoRevive && revivePanel?.IsVisible == true)
                {
                    var atCheckpoint = revivePanel.ResurrectAtCheckpoint;
                    if (atCheckpoint?.IsVisible == true)
                    {
                        var btnRect = atCheckpoint.GetClientRect();
                        var center = new Vector2(btnRect.Center.X, btnRect.Center.Y);
                        MoveCursorAndClick(new Vector2(_rect.X + center.X, _rect.Y + center.Y), false);
                        reviveDelayTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _rng.Next(750, 1500);
                    }
                }
            }
            return null;
        }

        if (!GameController.IsForeGroundCache) return null;

        var ingameUi = GameController.Game.IngameState.IngameUi;
        if (ingameUi.FullscreenPanels.Any(x => x.IsVisible)) return null;
        if (ingameUi.LargePanels.Any(x => x.IsVisible)) return null;
        if (ingameUi.OpenRightPanel.IsVisible) return null;
        if (ingameUi.OpenLeftPanel.IsVisible) return null;

        if (Settings.Run && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastRun >= Settings.FindPathDelay)
        {
            var playerGridPos = GetPlayerGridPos();
            var leader = FindLeader();

            if (leader == null)
            {
                if (isHideout)
                {
                    var leaderZone = GetLeaderZoneFromPartyUI();
                    if (!string.IsNullOrEmpty(leaderZone) && !IsLeaderInExcludeZone(leaderZone)){
                        var portals = FindAllPortals();
                        if (portals.Count <= Settings.PortalRemain && portals.Count > 0)
                        {
                            if (portalDelayTime == 0)
                            {
                                portalDelayTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (Settings.PortalDelay * 1000);
                            }
                            else if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > portalDelayTime)
                            {
                                var portalGridPos = new Vector2(portals[0].GridPosNum.X, portals[0].GridPosNum.Y);
                                var distance = Vector2.Distance(playerGridPos, portalGridPos);

                                if (distance < 30)
                                {
                                    FollowTarget(new Vector2i((int)playerGridPos.X, (int)playerGridPos.Y), new Vector2i((int)portals[0].GridPosNum.X, (int)portals[0].GridPosNum.Y), ClickMode.Left);
                                }
                                else
                                {
                                    FollowTarget(new Vector2i((int)playerGridPos.X, (int)playerGridPos.Y), new Vector2i((int)portals[0].GridPosNum.X, (int)portals[0].GridPosNum.Y), ClickMode.MoveKey);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var portals = FindAllPortals();
                    if (portals.Count > 0)
                    {
                        var portalGridPos = new Vector2(portals[0].GridPosNum.X, portals[0].GridPosNum.Y);
                        var distance = Vector2.Distance(playerGridPos, portalGridPos);

                        if (distance < 30)
                        {
                            FollowTarget(new Vector2i((int)playerGridPos.X, (int)playerGridPos.Y), new Vector2i((int)portals[0].GridPosNum.X, (int)portals[0].GridPosNum.Y), ClickMode.Left);
                        }
                        else
                        {
                            FollowTarget(new Vector2i((int)playerGridPos.X, (int)playerGridPos.Y), new Vector2i((int)portals[0].GridPosNum.X, (int)portals[0].GridPosNum.Y), ClickMode.MoveKey);
                        }
                    }
                }
            }
            else if (!isHideout)
            {
                var leaderGridPos = new Vector2(leader.GridPosNum.X, leader.GridPosNum.Y);
                var distance = Vector2.Distance(playerGridPos, leaderGridPos);

                if (distance > Settings.StartFollowRange || (distance > Settings.StopFollowRange && _state != FollowerState.NearLeader))
                {
                    _state = FollowerState.Following;
                    FollowTarget(new Vector2i((int)playerGridPos.X, (int)playerGridPos.Y), new Vector2i((int)leader.GridPosNum.X, (int)leader.GridPosNum.Y), ClickMode.MoveKey);
                }else if (distance < Settings.StopFollowRange)
                {
                    _state = FollowerState.NearLeader;
                }
                if (Settings.EnableLoot && _state == FollowerState.NearLeader)
                {
                    var labels = ingameUi.ItemsOnGroundLabelElement.VisibleGroundItemLabels;
                    if (labels != null)
                    {
                        Vector2 nearbyItem = new Vector2(-1000f, -1000f);
                        Vector2 closestItem = new Vector2(-1000f, -1000f);
                        var minDistance = float.MaxValue;
                        foreach (var label in labels)
                        {
                            if (label.Label == null || !label.Label.IsVisible)
                                continue;
                            if (label.Entity == null)
                                continue;
                            if (!IsLabelClickable(label.Label))
                                continue;

                            var worldItemEntity = label.Entity;
                            var itemName = label.Label.Text ?? "?";
                            if (!DoWePickThis(worldItemEntity, itemName))
                                continue;

                            var itemGridPos = new Vector2(worldItemEntity.GridPosNum.X, worldItemEntity.GridPosNum.Y);
                            var distanceToPlayer = Vector2.Distance(playerGridPos, itemGridPos);
                            var distanceToLeader = Vector2.Distance(leaderGridPos, itemGridPos);
                            var labelRect = label.ClientRect;
                            var labelScreenPos = new Vector2(labelRect.X + labelRect.Width / 2, labelRect.Y + labelRect.Height / 2);

                            if (distanceToPlayer <= 20)
                            {
                                nearbyItem = labelScreenPos;
                                break;
                            }
                            if (distanceToLeader > Settings.StartFollowRange)
                                continue;

                            if (distanceToPlayer < minDistance)
                            {
                                closestItem = labelScreenPos;
                                minDistance = distanceToPlayer;
                            }
                        }
                        if (!nearbyItem.Equals(new Vector2(-1000f, -1000f)))
                        {
                            PickNearby(nearbyItem);
                        }
                        else if (!closestItem.Equals(new Vector2(-1000f, -1000f)))
                        {
                            if (minDistance > 40)
                            {
                                MoveCursorAndPressKey(closestItem, Settings.MoveKey);
                            }
                            else
                            {
                                PickNearby(closestItem);
                            }
                        }
                    }
                }
            }

            lastRun = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        if (Settings.QuickLootKey.PressedOnce())
        {
            var playerGridPos = GetPlayerGridPos();
            var labels = ingameUi.ItemsOnGroundLabelElement.VisibleGroundItemLabels;
            if (labels != null)
            {
                List<Vector2> nearbyItems = new();
                foreach (var label in labels)
                {
                    if (label.Label == null || !label.Label.IsVisible)
                        continue;
                    if (label.Entity == null)
                        continue;
                    if (!IsLabelClickable(label.Label))
                        continue;

                    var worldItemEntity = label.Entity;
                    var itemName = label.Label.Text ?? "?";
                    if (!DoWePickThis(worldItemEntity, itemName))
                        continue;

                    var itemGridPos = new Vector2(worldItemEntity.GridPosNum.X, worldItemEntity.GridPosNum.Y);
                    var distanceToPlayer = Vector2.Distance(playerGridPos, itemGridPos);
                    var labelRect = label.ClientRect;
                    var labelScreenPos = new Vector2(labelRect.X + labelRect.Width / 2, labelRect.Y + labelRect.Height / 2);

                    if (distanceToPlayer <= 20)
                    {
                        nearbyItems.Add(labelScreenPos);
                    }
                }
                if (nearbyItems.Count > 0)
                {
                    PickNearbyMulti(nearbyItems);
                }
            }
        }
        if (Settings.StartKey.PressedOnce())
        {
            Settings.Run.Value = new ToggleNode(!Settings.Run);
        }
        return null;
    }

    public override void Render()
    {
        //Any Imgui or Graphics calls go here. This is called after Tick
        var status = Settings.Run ? "RUNNING" : "STOPPED";
        Graphics.DrawText($"{status}: {_state}", new Vector2(100, 100), SharpDX.Color.Red);

        if (Settings.CircleRadius > 0)
        {
            var playerPos = GameController.Player.PosNum;
            var worldRadius = Settings.CircleRadius * GridToWorld;
            Graphics.DrawCircleInWorld(new System.Numerics.Vector3(playerPos.X, playerPos.Y, playerPos.Z), (float)worldRadius, SharpDX.Color.Yellow, 2f);
        }

        if (Settings.DrawWorldPath && path != null && path.Count > 0) {
            var player = GameController.Game.IngameState.Data.LocalPlayer;
            var playerRender = player?.GetComponent<Render>();
            if (playerRender == null)
                return;
            var initPos = GameController.IngameState.Camera.WorldToScreen(playerRender.PosNum with { Z = playerRender.UnclampedHeight });

            var p0 = initPos;
            foreach (var elem in path)
            {
                var p1 = GameController.IngameState.Camera.WorldToScreen(new Vector3(elem.X * GridToWorld, elem.Y * GridToWorld, _heightData[elem.Y][elem.X]));

                if (_rect.Contains(p0) || _rect.Contains(p1))
                {
                    Graphics.DrawLine(p0, p1, 1.0f, SharpDX.Color.Red);
                }
                else
                {
                    break;
                }
                p0 = p1;
            }
        }

    }

    public override void EntityAdded(Entity entity)
    {
        //If you have a reason to process every entity only once,
        //this is a good place to do so.
        //You may want to use a queue and run the actual
        //processing (if any) inside the Tick method.
    }

    private bool DoWePickThis(Entity worldItemEntity, string name)
    {
        if (name.Contains("contract", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var worldItem = worldItemEntity.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            return false;
        }

        var entity = worldItem.ItemEntity;
        if (entity.Path.Contains("/Quest", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mods = entity.GetComponent<Mods>();
        if (mods != null)
        {
            return false;
        }

//        if (mods.ItemRarity == ItemRarity.Unique)
//        {
//            return false;
//        }

        return true;
    }

    private async Task PickNearby(Vector2 item)
    {
        Input.SetCursorPos(item);
        await Task.Delay(10);
        Input.LeftDown();
        await Task.Delay(_rng.Next(30, 51));
        Input.LeftUp();
    }

    private async Task PickNearbyMulti(List<Vector2> items)
    {
        var index = 0;
        foreach(var item in items)
        {
            await MoveCursorAndClick(item, false);
            if (index < items.Count - 1)
            {
                await Task.Delay(65);
            }
            index++;
        }
    }


    private bool IsLabelClickable(Element element)
    {
        if (element is not { IsValid: true, IsVisible: true, IndexInParent: not null })
        {
            return false;
        }

        var center = element.GetClientRect().Center;

        var gameRect = GameController.Window.GetWindowRectangleTimeCache with { Location = SharpDX.Vector2.Zero };
        gameRect.Inflate(-36, -36);
        return gameRect.Contains(center.X, center.Y);
    }

    private void FollowTarget(Vector2i start, Vector2i target, ClickMode clickMode)
    {
        path = pathFinder.FindPath(start, target);


        if (path != null || path.Count > 0)
        {
            var screenPos = new Vector2();
            var screenCenter = new Vector2(_rect.Width / 2f, _rect.Height / 2f);
            foreach (var elem in path)
            {
                screenPos = GameController.IngameState.Camera.WorldToScreen(new Vector3(elem.X * GridToWorld, elem.Y * GridToWorld, _heightData[elem.Y][elem.X]));
                var screenDist = Vector2.Distance(screenPos, screenCenter);

                if (screenDist > Settings.MoveRange) {
                    break;
                }
            }
            if (clickMode == ClickMode.MoveKey)
            {
                MoveCursorAndPressKey(screenPos, Settings.MoveKey);
            }
            else if (clickMode == ClickMode.Left)
            {
                MoveCursorAndClick(screenPos, false);
            }
            else
            {
                MoveCursorAndClick(screenPos, true);
            }

        }
    }

    private bool IsLeaderInExcludeZone (string leaderZone)
    {
        foreach (var ez in excludeZones)
        {
            if (leaderZone.Contains(ez, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private string? GetLeaderZoneFromPartyUI()
    {
        try
        {
            var partyElement = GameController.IngameState?.IngameUi?.PartyElement;
            if (partyElement?.IsVisible != true)
                return null;

            var playerElements = partyElement.PlayerElements;
            if (playerElements == null)
                return null;

            foreach (var pe in playerElements)
            {
                if (pe?.PlayerName == Settings.LeaderName)
                    return pe.ZoneName;
            }
        }
        catch { }
        return null;
    }

    private List<Entity> FindAllPortals()
    {
        var result = new List<Entity>();
        foreach (var entity in GameController.EntityListWrapper.OnlyValidEntities)
        {
            if (!entity.IsTargetable)
                continue;

            if (entity.Type == EntityType.TownPortal || entity.Type == EntityType.Portal)
            {
                result.Add(entity);
            }
        }
        return result;
    }

    private Vector2 GetPlayerGridPos()
    {
        var pos = GameController.Player.GridPosNum;
        return new Vector2(pos.X, pos.Y);
        // var pos = GameController.Game.IngameState.Data.LocalPlayer.PosNum;
        // return new Vector2(pos.X * PoeMapExtension.WorldToGridConversion, pos.Y * PoeMapExtension.WorldToGridConversion);
    }

    private Entity? FindLeader()
    {
        foreach (var entity in GameController.EntityListWrapper.OnlyValidEntities)
        {
            if (entity.Type != EntityType.Player)
                continue;
            var playerComp = entity.GetComponent<Player>();
            if (playerComp != null && playerComp.PlayerName == Settings.LeaderName)
                return entity;
        }
        return null;
    }

    /// <summary>Min ms for cursor travel (very short moves).</summary>
    private const int MoveMinMs = 35;
    /// <summary>Max ms for cursor travel (full screen moves).</summary>
    private const int MoveMaxMs = 200;
    /// <summary>Distance in pixels that maps to MoveMaxMs.</summary>
    private const float MoveMaxDistance = 1024f;
    /// <summary>Number of intermediate points for interpolation.</summary>
    private const int MoveSteps = 9;
    /// <summary>Max random pixel offset applied to final cursor landing position.</summary>
    private const float LandingJitterPx = 3f;

    private static async Task MoveCursorTo(Vector2 target)
    {
        var mp = Input.MousePosition;
        var start = new Vector2(mp.X, mp.Y);
        var delta = target - start;
        var dist = delta.Length();
        var curveOffsetPx = ((dist / MoveMaxDistance) * 65) + 3;

        if (dist < 10f)
        {
            // Too close to bother interpolating
            Input.SetCursorPos(target);
            await Task.Delay(10);
            return;
        }

        // Duration scales linearly with distance, clamped
        float t = Math.Clamp(dist / MoveMaxDistance, 0f, 1f);
        int totalMs = MoveMinMs + (int)((MoveMaxMs - MoveMinMs) * t);
        int stepDelayMs = Math.Max(1, totalMs / MoveSteps);

        var perp = new Vector2(-delta.Y, delta.X) / dist;
        var control = start + delta * 0.5f + perp * curveOffsetPx;

        for (int i = 1; i <= MoveSteps; i++)
        {
            float progress = (float)i / MoveSteps;

            // Quadratic Bézier: start → control → target
            var fromStart = start + (control - start) * progress;
            var fromControl = control + (target - control) * progress;
            var pos = fromStart + (fromControl - fromStart) * progress;

            Input.SetCursorPos(pos);
            await Task.Delay(stepDelayMs);
        }

        var jitterX = (float)(_rng.NextDouble() * 2 - 1) * LandingJitterPx;
        var jitterY = (float)(_rng.NextDouble() * 2 - 1) * LandingJitterPx;
        Input.SetCursorPos(target + new Vector2(jitterX, jitterY));
        await Task.Delay(10);
    }

    private static async Task MoveCursorAndClick(Vector2 target, bool isRight)
    {
        await MoveCursorTo(target);
        if (isRight)
        {
            Input.RightDown();
            await Task.Delay(_rng.Next(30, 51));
            Input.RightUp();
        }
        else
        {
            Input.LeftDown();
            await Task.Delay(_rng.Next(30, 51));
            Input.LeftUp();
        }
    }

    private static async Task MoveCursorAndPressKey(Vector2 target, Keys key)
    {
        await MoveCursorTo(target);
        Input.KeyDown(key);
        await Task.Delay(_rng.Next(30, 51));
        Input.KeyUp(key);
    }

    public enum FollowerState
    {
        SearchingForLeader,
        Following,
        NearLeader
    }
    public enum ClickMode
    {
        Left,
        Right,
        MoveKey
    }
}