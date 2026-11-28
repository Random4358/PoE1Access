using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using static PoE1Access.Loc;

namespace PoE1Access;

// Speed in maps (and everywhere):
//
//   Loot follows your loot filter: an item whose label the game's loot filter hides makes no loot
//   sound and is left out of the scanner, as a sighted player sees only what the filter shows.
//   When the game shows no labels at all (labels hidden until a key is held), nothing is left out.
//
//   Explore mode: guiding to unexplored ground goes on by itself to the next unexplored ground
//   when you get there, the biggest and nearest first, until the area is explored ("Next: ...",
//   "Area explored"). Triple tap L3 stops it as any guide.
//
//   The map device (in a hideout) is listed under Objects.
//
//   In maps, and anywhere for league objects, every new kind of object is written to the log
//   once per area ("Area object: <path> type=..."), so the first maps show the game's own names.
internal sealed partial class Navigator
{
    public bool FollowLootFilter = true;

    /// <summary>Whether you are in a hideout (set each tick; read by Category, which is static).</summary>
    public static bool InHideout;
    public bool ExploreMode = true;

    private HashSet<long> _shownLoot = new HashSet<long>();
    private bool _labelsShown;
    private long _nextLabelPollMs;

    private void PollLootLabels(long now)
    {
        InHideout = Try(() => _gc.Area.CurrentArea.IsHideout, false);
        if (now < _nextLabelPollMs) return;
        _nextLabelPollMs = now + 400;
        var shown = new HashSet<long>();
        var labels = Try(() => _gc.IngameState.IngameUi.ItemsOnGroundLabelsVisible?.ToList(), null);
        if (labels != null)
            foreach (var l in labels)
            {
                long a = Try(() => l.ItemOnGround?.Address ?? 0, 0L);
                if (a != 0) shown.Add(a);
            }
        _shownLoot = shown;
        // Once labels have been seen in this area the list is trusted, also when it is empty: an
        // enemy that dropped only what the filter hides (a plain ring) left no label shown, every
        // item then counted as shown, and the hidden ring was heard but not listed (28 Nov 2026).
        long area = Try(() => (long)_gc.Area.CurrentArea.Hash, 0L);
        if (area != _labelsArea) { _labelsArea = area; _labelsShown = false; }
        if (shown.Count > 0) _labelsShown = true;
    }

    private long _labelsArea;

    /// <summary>Whether an item on the ground is one the game's loot filter shows.</summary>
    private bool LootShown(Entity e) => !FollowLootFilter || !_labelsShown || _shownLoot.Contains(Try(() => e.Address, 0L));

    public static bool IsMapDevice(Entity e)
    {
        string p = Try(() => e.Path, "") ?? "";
        return p.IndexOf("MappingDevice", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("MapDevice", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Whether a living enemy is within `cells` of you (the tutorial waits for quiet).</summary>
    public bool EnemyNear(float cells)
    {
        var p = PlayerGrid();
        foreach (Entity e in Entities())
        {
            if (Try(() => e.Type, EntityType.Error) != EntityType.Monster) continue;
            if (!Try(() => e.IsHostile && e.IsAlive && e.IsTargetable, false)) continue;
            if (Try(() => (e.GridPosNum - p).Length(), 1e9f) <= cells) return true;
        }
        return false;
    }

    /// <summary>Where you stand, in grid cells.</summary>
    public Vector2 PlayerCell() => PlayerGrid();

    // ---- Explore mode ----

    /// <summary>At unexplored ground the guide was leading to: the next one, or false when there is none.</summary>
    private bool ExploreNext(Vector2 p)
    {
        _uxCache = null;
        var options = UnexploredList(p).Where(s => (PosOf(s) - p).Length() > 20f).ToList();
        // With a quest route, ground that holds an exit you have not found comes first, as the
        // scanner's own hint says ("leads to a new exit"), without knowing which exit it is.
        string newExit = T("leads to a new exit", "lleva a una salida nueva");
        var next = (!string.IsNullOrEmpty(QuestRouteNext) ? options.FirstOrDefault(s => (s.Label ?? "").IndexOf(newExit, StringComparison.OrdinalIgnoreCase) >= 0) : null)
                   ?? options.FirstOrDefault();
        if (next == null) { _say(T("Area explored", "Área explorada")); return false; }
        _guideTarget = next;
        _scanChoice = next;
        _guideName = NameOf(next, _guideItemName);
        _guideLastPos = PosOf(next);
        _guidePath = null;
        _guidePathMs = 0;
        ResetPrompts();
        _say(T($"Next: {_guideName}, {RouteText(p, _guideLastPos)}", $"Siguiente: {_guideName}, {RouteText(p, _guideLastPos)}"));
        return true;
    }

    // ---- Logging what an area holds ----

    private readonly HashSet<string> _areaLogged = new HashSet<string>();
    private long _areaLogArea, _nextAreaLogMs;

    private void TickAreaLog(long now)
    {
        if (now < _nextAreaLogMs) return;
        _nextAreaLogMs = now + 1500;
        long area = Try(() => (long)_gc.Area.CurrentArea.Hash, 0L);
        if (area != _areaLogArea) { _areaLogArea = area; _areaLogged.Clear(); }
        string id = Try(() => _gc.Area.CurrentArea.Area.Id, "") ?? "";
        bool map = id.StartsWith("MapWorlds", StringComparison.OrdinalIgnoreCase) || Try(() => _gc.Area.CurrentArea.IsHideout, false);
        foreach (Entity e in Entities())
        {
            if (_areaLogged.Count > 300) return;
            string path = Try(() => e.Path, "") ?? "";
            if (path.Length == 0) continue;
            EntityType t = Try(() => e.Type, EntityType.Error);
            bool league = path.IndexOf("/Leagues/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!league && (!map || t == EntityType.WorldItem || t == EntityType.Effect || t == EntityType.Player)) continue;
            // Monsters: only the unusual ones (league monsters and bosses), not every pack.
            if (t == EntityType.Monster && !league && Try(() => e.Rarity, MonsterRarity.White) != MonsterRarity.Unique) continue;
            string key = path.Contains('@') ? path.Substring(0, path.IndexOf('@')) : path;
            if (!_areaLogged.Add(key)) continue;
            PluginLog.Write("Area object: " + key + " type=" + t + " name=" + Try(() => e.RenderName, "") +
                            " icon=" + Try(() => e.GetComponent<MinimapIcon>()?.Name, "") + " targetable=" + Try(() => e.IsTargetable, false));
        }
    }
}
