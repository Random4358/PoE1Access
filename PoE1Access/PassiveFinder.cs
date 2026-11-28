using System;
using System.Collections.Generic;
using System.Linq;
using Vector2 = SharpDX.Vector2;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using static PoE1Access.Loc;

namespace PoE1Access;

// The passive tree finder, after Path of Exile 2 Access's: with the tree open, D-pad up and down
// choose a list (Position, Keystones, Notables, Jewel sockets, Next to your nodes) and left
// and right walk it, the nodes costing fewest points first. The node named becomes the target:
//   "Keystones, 12. Resolute Technique, 8 points away, up left. Allocate first: Life, down"
// and every node the cursor reaches then ends with the way on, "Toward Resolute Technique: up
// left" (or, while it is not linked, toward the next node to allocate on the cheapest line to
// it), until "Arrived at Resolute Technique" and, once it is yours, "You have Resolute
// Technique. Guide off". Back at Position the target is dropped. Nothing is allocated for you.
//
// The cost is the number of points to spend: from the nodes that can be allocated now (1 each)
// along nodes you do not have, as for "N points away".
public partial class PoE1AccessCore
{
    private static readonly string[] FinderLists = { "Position", "Keystones", "Notables", "Jewel sockets", "Next to your nodes", "Your build" };
    private int _finderList, _finderIndex = -1;
    private long _finderTarget;
    private Element _passiveHover;
    private long _passiveHoverRow;

    private sealed class FinderNode
    {
        public long Row;
        public string Name = "";
        public int Cost;          // 0 allocated, 1 can be allocated, more: points away
        public List<long> Path;   // the nodes to allocate on the way, nearest first (empty when allocated or allocatable)
    }

    /// <summary>A D-pad press in the passive tree.</summary>
    // How a list is said; FinderLists keeps the English names.
    private static string FinderListName(int i) => i switch
    {
        0 => T("Position", "Posición"),
        1 => T("Keystones", "Piedras angulares"),
        2 => T("Notables", "Notables"),
        3 => T("Jewel sockets", "Engarces de joya"),
        4 => T("Next to your nodes", "Junto a tus nodos"),
        5 => T("Your build", "Tu build"),
        _ => FinderLists[i],
    };

    private void FinderStep(int dpad)
    {
        if (_passiveHover == null || _passiveHoverRow == 0) { Say(T("Move onto a passive node first", "Ponte primero sobre un nodo pasivo"), true); return; }
        if ((dpad & (ControllerInput.DPadUp | ControllerInput.DPadDown)) != 0)
        {
            int step = (dpad & ControllerInput.DPadDown) != 0 ? 1 : -1;
            _finderList = (_finderList + step + FinderLists.Length) % FinderLists.Length;
            _finderIndex = -1;
            if (_finderList == 0) { SayPosition(); return; }
            FinderMove(1, true);
            return;
        }
        if (_finderList == 0) { SayPosition(); return; }
        FinderMove((dpad & ControllerInput.DPadLeft) != 0 ? -1 : 1, false);
    }

    private void SayPosition()
    {
        bool had = _finderTarget != 0;
        _finderTarget = 0;
        string here = _passiveNameOfRow.TryGetValue(_passiveHoverRow, out string n) && n.Length > 0 ? n : T("this node", "este nodo");
        Say(had ? T($"Position: {here}. Target dropped", $"Posición: {here}. Objetivo descartado") : T($"Position: {here}", $"Posición: {here}"), true);
    }

    private void FinderMove(int step, bool announceList)
    {
        var nodes = FinderNodes(_finderList);
        string list = FinderListName(_finderList);
        if (nodes.Count == 0 && _finderList == 5)
        {
            // The Path of Building build followed (BuildFollow.cs). An empty list is "all taken"
            // only when nothing of the build is left to take.
            var have = AllocatedPassives();
            bool left = _build != null && BuildNodes().Any(n => !have.Contains(n));
            Say(_build == null ? T("Your build: none loaded. Load one in the mod menu, under Build to follow.", "Tu build: ninguna cargada. Carga una en el menú del mod, en Build a seguir.")
                : left ? T($"Your build: no way found from your passives to those of {SpecName(FollowedSpec())}", $"Tu build: no hay camino desde tus pasivas hasta las de {SpecName(FollowedSpec())}")
                : T($"Your build: every passive of {SpecName(FollowedSpec())} is taken", $"Tu build: todas las pasivas de {SpecName(FollowedSpec())} están tomadas"), true);
            return;
        }
        if (nodes.Count == 0) { Say(T($"{list}: none on this tree", $"{list}: ninguno en este árbol"), true); return; }
        _finderIndex = _finderIndex < 0 ? 0 : (_finderIndex + step + nodes.Count) % nodes.Count;
        var node = nodes[_finderIndex];
        _finderTarget = node.Row;

        var parts = new List<string>();
        if (announceList) parts.Add(list + ", " + nodes.Count);
        int pointsAway = node.Cost;
        string cost = node.Cost == 0 ? T("allocated", "asignado") : node.Cost == 1 ? (CanAllocate(node.Row) ? T("can be allocated", "se puede asignar") : T("1 point away", "a 1 punto"))
            : T($"{pointsAway} points away", $"a {pointsAway} puntos");
        int position = _finderIndex + 1, total = nodes.Count;
        parts.Add(T($"{node.Name}, {cost}, {position} of {total}", $"{node.Name}, {cost}, {position} de {total}"));
        string way = WayTo(node.Row);
        if (way.Length > 0) parts.Add(way);
        if (node.Path != null && node.Path.Count > 0)
        {
            string first = NodeName(node.Path[0]) + WayDirection(node.Path[0]);
            parts.Add(T($"Allocate first: {first}", $"Asigna primero: {first}"));
        }
        // What the node does, last, so the way is heard first.
        parts.AddRange(PassiveStatLines(node.Row));
        // The node under the cursor, whose reading now ends with the new target's way, is not
        // said again over this until the cursor moves.
        _finderQuietRow = _passiveHoverRow;
        Say(JoinSpeech(parts), true);
    }

    /// <summary>What the guide adds to a node's reading: the way to the target or to the next
    /// node to allocate for it. Empty without a target.</summary>
    private string FinderGuide(long row)
    {
        if (_finderTarget == 0) return "";
        var allocated = AllocatedPassives();
        string target = NodeName(_finderTarget);
        if (_passiveIdOfRow.TryGetValue(_finderTarget, out ushort tid) && allocated.Contains(tid))
        {
            _finderTarget = 0;
            return T($"You have {target}. Guide off", $"Ya tienes {target}. Guía desactivada");
        }
        if (row == _finderTarget)
        {
            var path = PathTo(_finderTarget);
            if (path == null || path.Count == 0)
                return CanAllocate(row) ? T($"Arrived at {target}, allocate this one", $"Has llegado a {target}, asigna este")
                    : T($"Arrived at {target}", $"Has llegado a {target}");
            int toGo = path.Count + 1;
            string first = NodeName(path[0]) + WayDirection(path[0]);
            return T($"Arrived at {target}, {toGo} points to go. Allocate first: {first}", $"Has llegado a {target}, faltan {toGo} puntos. Asigna primero: {first}");
        }
        var steps = PathTo(_finderTarget);
        if (steps != null && steps.Count > 0)
        {
            int toGo = steps.Count + 1;
            string next = NodeName(steps[0]) + WayDirection(steps[0]);
            if (steps[0] == row) return T($"Allocate this one for {target}, {toGo} points to go", $"Asigna este para {target}, faltan {toGo} puntos");
            if (steps.Contains(row)) return T($"On the way to {target}. Next to allocate: {next}", $"De camino a {target}. Siguiente a asignar: {next}");
            return T($"{target}, {toGo} points to go. Next to allocate: {next}", $"{target}, faltan {toGo} puntos. Siguiente a asignar: {next}");
        }
        string way = WayDirection(_finderTarget);
        return T($"Toward {target}{way}", $"Hacia {target}{way}");
    }

    private long _finderQuietRow;

    // A node's stats in words ("20% increased mana regeneration rate"), from its PassiveSkills
    // row, as a node is read when the game's tooltip cannot be found.
    private List<string> PassiveStatLines(long row)
    {
        var lines = new List<string>();
        var skill = Try(() => GameController.Files.PassiveSkills.EntriesList.FirstOrDefault(p => p.Address == row), null);
        if (skill == null) return lines;
        foreach (var (stat, value) in Try(() => skill.Stats.ToList(), new List<(ExileCore.PoEMemory.FilesInMemory.StatsDat.StatRecord, int)>()))
        {
            var game = Try(() => PassiveStatText(stat, value), new List<string>());
            if (game.Count > 0) { lines.AddRange(game); continue; }
            string text = StatWords(stat?.Key, value);
            if (text.Length > 0) lines.Add(text);
        }
        return lines;
    }

    private string NodeName(long row) => _passiveNameOfRow.TryGetValue(row, out string n) && n.Length > 0 ? n : T("unnamed node", "nodo sin nombre");

    // ": up left", from the node under the cursor; "" without screen positions.
    private string WayDirection(long row)
    {
        var elements = _treeElements;
        if (elements == null || _passiveHover == null || !elements.TryGetValue(row, out Element el)) return "";
        Vector2 here = Centre(_passiveHover), there = Centre(el);
        if (here == Vector2.Zero || there == Vector2.Zero) return "";
        if (row == _passiveHoverRow) return T(": here", ": aquí");
        return ": " + DirectionName(DirectionIndex(here, there));
    }

    private string WayTo(long row)
    {
        string d = WayDirection(row);
        return d.Length > 0 ? T($"From here{d}", $"Desde aquí{d}") : "";
    }

    /// <summary>Your passives, with your class's starting node: the game does not list it, so at
    /// level 1 with nothing taken every list was empty ("none on this tree", seen 28 Nov 2026).</summary>
    private HashSet<ushort> AllocatedPassives()
    {
        var set = new HashSet<ushort>(Try(() => GameController.IngameState.ServerData.PassiveSkillIds.Select(i => (ushort)i).ToList(), new List<ushort>()));
        ushort start = ClassStartPassive();
        if (start != 0) set.Add(start);
        return set;
    }

    // Each class's starting node on the passive tree, by class number (0 Scion ... 6 Shadow, as in
    // ClassScreen.cs); the same in every tree version (Path of Building's tree data, 3.30).
    private static readonly ushort[] ClassStartPassives = { 58833, 47175, 50459, 54447, 50986, 61525, 44683 };

    private ushort ClassStartPassive()
    {
        string path = Try(() => GameController.Player?.Path, "") ?? "";
        int c = Array.FindIndex(Classes, x => path.StartsWith(x.Id, StringComparison.OrdinalIgnoreCase));
        return c >= 0 ? ClassStartPassives[c] : (ushort)0;
    }

    private bool CanAllocate(long row) =>
        _treeElements != null && _treeElements.TryGetValue(row, out Element el) && Try(() => el.AsObject<TreePassiveElement>().CanAllocate, false);

    // The nodes of a list, fewest points first (allocated ones last), nodes with no way to them
    // (another class's ascendancy) left out.
    private List<FinderNode> FinderNodes(int list)
    {
        var result = new List<FinderNode>();
        var elements = _passiveHover != null ? TreeElements(_passiveHover) : null;
        var links = PassiveLinks(Environment.TickCount64);
        if (elements == null || links == null) return result;
        LearnPassiveRows();
        var costs = PassiveCosts(elements, links, out var parents);
        var mem = Mem;
        var buildNodes = list == 5 ? BuildNodes() : null;
        foreach (var kv in elements)
        {
            long row = kv.Key;
            if (!costs.TryGetValue(row, out int cost)) continue;
            bool keep = list switch
            {
                1 => Try(() => mem.Read<byte>(row + 0x4A), (byte)0) == 1,
                2 => Try(() => mem.Read<byte>(row + 0x4B), (byte)0) == 1 && Try(() => mem.Read<byte>(row + 0x4A), (byte)0) != 1,
                3 => _passiveIdTextOfRow.TryGetValue(row, out string pid) && pid.StartsWith("jewel_slot", StringComparison.OrdinalIgnoreCase),
                4 => cost == 1,
                5 => cost > 0 && _passiveIdOfRow.TryGetValue(row, out ushort bid) && buildNodes.Contains(bid),
                _ => false,
            };
            if (!keep) continue;
            result.Add(new FinderNode { Row = row, Name = NodeName(row), Cost = cost, Path = PathFromParents(row, parents) });
        }
        // Fewest points first; those you have at the end; then by name, so the order holds.
        return result.OrderBy(n => n.Cost == 0 ? int.MaxValue : n.Cost).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The nodes to allocate before the target, nearest first; null without a way.</summary>
    private List<long> PathTo(long target)
    {
        var elements = _treeElements;
        var links = PassiveLinks(Environment.TickCount64);
        if (elements == null || links == null) return null;
        var costs = PassiveCosts(elements, links, out var parents);
        if (!costs.ContainsKey(target)) return null;
        return PathFromParents(target, parents);
    }

    private static List<long> PathFromParents(long row, Dictionary<long, long> parents)
    {
        var path = new List<long>();
        long cur = row;
        while (parents.TryGetValue(cur, out long p) && p != 0) { path.Add(p); cur = p; }
        path.Reverse();   // the first to allocate first
        return path;
    }

    // Points to each node: 0 for those you have, 1 for those linked to them (or that the game
    // says can be allocated, such as your class's start before any point is spent), then one
    // more per node along the links through nodes you do not have. Counted from your nodes, not
    // from the game's "can be allocated", which is false everywhere while you have no point to
    // spend (seen 21 Nov 2026). Parents give the line taken (0: next to your nodes).
    private Dictionary<long, int> PassiveCosts(Dictionary<long, Element> elements, Dictionary<long, List<long>> links, out Dictionary<long, long> parents)
    {
        var allocated = AllocatedPassives();
        var cost = new Dictionary<long, int>();
        parents = new Dictionary<long, long>();
        var queue = new Queue<long>();
        foreach (var kv in elements)
            if (_passiveIdOfRow.TryGetValue(kv.Key, out ushort pid) && allocated.Contains(pid)) { cost[kv.Key] = 0; queue.Enqueue(kv.Key); }
        foreach (var kv in elements)
            if (!cost.ContainsKey(kv.Key) && Try(() => kv.Value.AsObject<TreePassiveElement>().CanAllocate, false))
            { cost[kv.Key] = 1; parents[kv.Key] = 0; queue.Enqueue(kv.Key); }
        while (queue.Count > 0)
        {
            long cur = queue.Dequeue();
            if (!links.TryGetValue(cur, out var next)) continue;
            foreach (long nx in next)
            {
                if (cost.ContainsKey(nx) || !elements.ContainsKey(nx)) continue;
                cost[nx] = cost[cur] + 1;
                parents[nx] = cost[cur] == 0 ? 0 : cur;
                queue.Enqueue(nx);
            }
        }
        return cost;
    }
}
