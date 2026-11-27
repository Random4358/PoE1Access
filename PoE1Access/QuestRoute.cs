using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.MemoryObjects;
using static PoE1Access.Loc;

namespace PoE1Access;

// Quest route: once any of your characters has finished the campaign, the way through the act's
// areas to the one your quest names, as the game's world map shows it to everyone (its areas and
// the paths between them), from the objective the quest tracker shows ("Travel to the Tidal
// Island"): "Quest route: the Coast, then the Tidal Island. Last waypoint on the way: the Coast".
// Said as you enter an area and when the objective changes. Exits you have found that are on the
// way say so in the scanner, and explore mode tries unexplored ground that holds an exit you have
// not found first, as the scanner already hints, without saying where the exit is.
//
// Nothing the game keeps hidden is used: where things are inside an area is still yours to find.
//
// The campaign counts as finished on reaching the epilogue or any map (kept in
// app\mechanics-seen.txt as "campaign done", for every character). "Quest route" in the Speech
// submenu turns it off. Not tried in the game yet.
public partial class PoE1AccessCore
{
    private string _routeSig = "";
    private long _nextRouteMs;

    private bool CampaignDone() => SeenHas("campaign done");

    private void NoteCampaignDone()
    {
        if (CampaignDone()) return;
        var area = Try(() => GameController.Area.CurrentArea, null);
        if (area == null) return;
        int act = Try(() => area.Area.Act, 0);
        string id = Try(() => area.Area.Id, "") ?? "";
        if (act >= 11 || id.StartsWith("MapWorlds", StringComparison.OrdinalIgnoreCase))
        {
            SeenAdd("campaign done");
            PluginLog.Write("Campaign done (area " + id + ", act " + act + ")");
            if (Settings.QuestRoute.Value)
                Say(T("You have finished the campaign. From now on, on every character, Quest route says the way through the areas to your quest, as the world map shows it.",
                      "Has terminado la campaña. A partir de ahora, en todos tus personajes, Ruta de misión dice el camino por las áreas hasta tu misión, como lo muestra el mapa del mundo."), false);
        }
    }

    private void PollQuestRoute(long now, bool uiBusy)
    {
        if (now < _nextRouteMs) return;
        _nextRouteMs = now + 2000;
        NoteCampaignDone();
        if (!Settings.QuestRoute.Value || !CampaignDone() || _nav == null) { if (_nav != null) _nav.QuestRouteNext = null; return; }
        if (uiBusy) return;

        var current = Try(() => GameController.Area.CurrentArea.Area, null);
        if (current == null) return;
        var objectives = Try(QuestObjectives, new List<string>());
        int act = Try(() => current.Act, 0);
        var areas = Try(() => GameController.Files.WorldAreas.AreasIndexDictionary.Values.Where(a => a != null && Try(() => a.Act, -1) == act && !Try(() => a.IsHideout, false)).ToList(), null);
        if (areas == null || areas.Count == 0 || objectives.Count == 0) { _nav.QuestRouteNext = null; return; }

        // The area the objectives name; the longest name wins ("the Lower Prison" over "the Prison").
        static string Core(string n) => (n ?? "").Trim().StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? n.Trim().Substring(4) : (n ?? "").Trim();
        WorldArea target = areas.Where(a => Core(a.Name).Length >= 4 && objectives.Any(o => o.IndexOf(Core(a.Name), StringComparison.OrdinalIgnoreCase) >= 0))
                                .OrderByDescending(a => Core(a.Name).Length).FirstOrDefault();
        string sig = Try(() => current.Id, "") + "|" + string.Join("|", objectives);
        if (target == null) { _nav.QuestRouteNext = null; _routeSig = sig; return; }

        var path = AreaPath(current, target, act);
        _nav.QuestRouteNext = path != null && path.Count > 1 ? path[1].Name : null;
        if (sig == _routeSig) return;
        _routeSig = sig;
        if (Try(() => current.Id == target.Id, false)) { SayNews(News.Quest, T("Quest route: your quest is in this area.", "Ruta de misión: tu misión está en esta área."), false); return; }
        if (path == null || path.Count < 2) { PluginLog.Write("Quest route: no way found to " + target.Name); return; }
        var names = path.Skip(1).Select(a => a.Name).ToList();
        string way = names.Count == 1 ? T($"next area, {names[0]}", $"siguiente área, {names[0]}")
            : string.Join(", ", names.Take(names.Count - 1)) + T($", then {names[^1]}", $", después {names[^1]}");
        string text = T($"Quest route: {way}.", $"Ruta de misión: {way}.");
        // The waypoint on the way nearest your quest, when the route has one past this area.
        var wp = path.Skip(1).Reverse().FirstOrDefault(a => Try(() => a.HasWaypoint, false) && !Try(() => a.IsTown, false));
        if (wp != null && names.Count > 1) text += " " + T($"Last waypoint on the way: {wp.Name}.", $"Último transportador del camino: {wp.Name}.");
        PluginLog.Write("Quest route: " + string.Join(" > ", path.Select(a => a.Name)));
        SayNews(News.Quest, text, false);
    }

    // The fewest areas from one to the other through the act's paths, or null.
    private static List<WorldArea> AreaPath(WorldArea from, WorldArea to, int act)
    {
        var prev = new Dictionary<int, WorldArea>();
        var seen = new HashSet<int> { from.Index };
        var queue = new Queue<WorldArea>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var a = queue.Dequeue();
            if (a.Index == to.Index)
            {
                var path = new List<WorldArea> { a };
                while (prev.TryGetValue(path[0].Index, out var p)) path.Insert(0, p);
                return path;
            }
            foreach (var n in Try(() => a.Connections?.ToList(), null) ?? new List<WorldArea>())
            {
                if (n == null || Try(() => n.Act, -1) != act || !seen.Add(n.Index)) continue;
                prev[n.Index] = a;
                queue.Enqueue(n);
            }
        }
        return null;
    }
}
