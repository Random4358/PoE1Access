using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using static PoE1Access.Loc;
using BaseComp = ExileCore.PoEMemory.Components.Base;
using ModsComp = ExileCore.PoEMemory.Components.Mods;

namespace PoE1Access;

// The followed build's gear and gems against yours, after Poe2Access's Build Planner reading.
//   - A worn piece, after its name: "Build: Royal Plate, same base, 3 of 6 modifiers", then after
//     its own lines each modifier the build's piece has, against yours: "+150 to maximum Life,
//     yours is 90", "+30% to Cold Resistance, met", "Missing: 6% additional Physical Damage
//     Reduction". A unique the build names: "Build: unique The Stampede, not this one".
//   - An empty equipment slot: what the build puts there.
//   - Opening the inventory: how the worn gear stands ("Build gear: 4 of 10 pieces on its base,
//     12 of 48 modifiers. Furthest from the build: helmet").
//   - A gem: "Build: for Righteous Fire" or "Build: not in Totem Time".
// A modifier is matched by its words with the numbers left out; a range such as (17-23) asks for
// its lowest. Flasks and jewels are not compared.
public partial class PoE1AccessCore
{
    // The mod's equipment slot names, as Path of Building names them.
    private static string PoBSlotFor(string label) => PoBSlotOf.TryGetValue(label, out string s) ? s : null;

    private BuildPlan.GearPiece BuildPieceFor(string label)
    {
        string slot = label == null ? null : PoBSlotFor(label);
        return slot == null || _build == null ? null : _build.Pieces.FirstOrDefault(p => p.Slot == slot);
    }

    private static string ModTemplate(string line)
    {
        string t = Regex.Replace((line ?? "").ToLowerInvariant(), @"\(\s*-?\d+(\.\d+)?\s*-\s*-?\d+(\.\d+)?\s*\)", "#");
        t = Regex.Replace(t, @"\d+(\.\d+)?", "#");
        return Regex.Replace(t, @"[+-]#", "#").Trim();
    }

    /// <summary>The number a line asks for: a range's lowest, else its first number; null without.</summary>
    private static double? ModValue(string line)
    {
        var m = Regex.Match(line ?? "", @"\(\s*(-?\d+(?:\.\d+)?)\s*-\s*-?\d+(?:\.\d+)?\s*\)|(-?\d+(?:\.\d+)?)");
        if (!m.Success) return null;
        string v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    private static string ModWithValue(string line)
    {
        // "+(17-23)% to Chaos Resistance" -> "+17% to Chaos Resistance"
        return Regex.Replace(line ?? "", @"\(\s*(-?\d+(?:\.\d+)?)\s*-\s*-?\d+(?:\.\d+)?\s*\)", "$1");
    }

    private List<string> WornModLines(Entity item)
    {
        var mods = GetComp<ModsComp>(item);
        var lines = new List<string>();
        if (mods == null) return lines;
        lines.AddRange(Try(() => mods.HumanImpStats?.ToList(), null) ?? new List<string>());
        lines.AddRange(Try(() => mods.HumanStats?.ToList(), null) ?? new List<string>());
        lines.AddRange(Try(() => mods.EnchantedStats?.ToList(), null) ?? new List<string>());
        return lines.Select(CleanText).Where(l => l.Length > 0).ToList();
    }

    /// <summary>The build's piece for a slot against what is worn there: a short line for after
    /// the item's name, and the modifiers one by one; (null, null) when the build has none there.</summary>
    private (string Short, List<string> Details, int Met, int Total, bool SameBase) BuildGearCompare(string label, Entity worn)
    {
        var piece = BuildPieceFor(label);
        if (piece == null) return (null, null, 0, 0, false);
        var details = new List<string>();
        if (piece.Rarity == "UNIQUE")
        {
            string wornUnique = worn == null ? "" : CleanText(Try(() => GetComp<ModsComp>(worn)?.UniqueName, "") ?? "");
            bool same = wornUnique.Length > 0 && piece.Name.IndexOf(wornUnique, StringComparison.OrdinalIgnoreCase) >= 0;
            string name = piece.Name.Length > 0 ? piece.Name : piece.Base;
            string s = worn == null ? T($"Build puts here: unique {name}, {piece.Base}", $"La build pone aquí: único {name}, {piece.Base}")
                : same ? T($"Build: unique {name}, this one", $"Build: único {name}, este") : T($"Build: unique {name}, not this one", $"Build: único {name}, no es este");
            return (s, details, same ? 1 : 0, 1, same);
        }
        string wornBase = worn == null ? "" : CleanText(Try(() => GetComp<BaseComp>(worn)?.Name, "") ?? "");
        bool sameBase = wornBase.Length > 0 && string.Equals(wornBase, piece.Base, StringComparison.OrdinalIgnoreCase);
        var have = worn == null ? new List<string>() : WornModLines(worn);
        int met = 0;
        foreach (string want in piece.Mods)
        {
            string tpl = ModTemplate(want);
            string mine = have.FirstOrDefault(h => ModTemplate(h) == tpl);
            double? target = ModValue(want);
            if (mine == null) { details.Add(T($"Missing: {ModWithValue(want)}", $"Falta: {ModWithValue(want)}")); continue; }
            double? value = ModValue(mine);
            if (target == null || value == null || Math.Abs(value.Value) >= Math.Abs(target.Value)) { met++; details.Add(T($"{ModWithValue(want)}, met", $"{ModWithValue(want)}, cumplido")); }
            else details.Add(T($"{mine}, the build asks for {target.Value.ToString(CultureInfo.CurrentCulture)}", $"{mine}, la build pide {target.Value.ToString(CultureInfo.CurrentCulture)}"));
        }
        string baseWords = worn == null ? piece.Base : sameBase ? T("same base", "misma base") : T($"another base, the build's is {piece.Base}", $"otra base, la de la build es {piece.Base}");
        string head = worn == null
            ? T($"Build puts here: {piece.Base}, {piece.Mods.Count} modifiers", $"La build pone aquí: {piece.Base}, {piece.Mods.Count} modificadores")
            : T($"Build: {baseWords}, {met} of {piece.Mods.Count} modifiers", $"Build: {baseWords}, {met} de {piece.Mods.Count} modificadores");
        return (head, details, met, piece.Mods.Count, sameBase);
    }

    /// <summary>Opening the inventory: how the worn gear stands against the build, or "".</summary>
    private string BuildGearSummary()
    {
        if (_build == null || _build.Pieces.Count == 0) return "";
        ServerData server = GetServerData();
        if (server == null) return "";
        int pieces = 0, onBase = 0, met = 0, total = 0;
        string furthest = null;
        double worst = 2;
        foreach (var (slot, label) in EquipmentSlots)
        {
            if (slot == InventorySlotE.Flask1 || BuildPieceFor(label) == null) continue;
            Entity worn = Slots(server, slot).FirstOrDefault()?.Item;
            var c = BuildGearCompare(label, worn);
            if (c.Short == null) continue;
            pieces++;
            if (c.SameBase) onBase++;
            met += c.Met;
            total += c.Total;
            double share = c.Total > 0 ? (double)c.Met / c.Total : 1;
            if (share < worst) { worst = share; furthest = SlotName(label); }
        }
        if (pieces == 0) return "";
        string text = T($"Build gear: {onBase} of {pieces} pieces on its base or unique, {met} of {total} modifiers",
                        $"Equipo de la build: {onBase} de {pieces} piezas con su base o único, {met} de {total} modificadores");
        if (furthest != null && worst < 1) text += T($". Furthest from the build: {furthest}", $". Lo más lejos de la build: {furthest}");
        return text;
    }

    /// <summary>"Build: for Righteous Fire" or "Build: not in Totem Time" for a gem, or "".</summary>
    private string BuildGemLine(Entity gem)
    {
        if (_build == null || _build.SkillSets.Count == 0) return "";
        string name = CleanText(Try(() => GetComp<BaseComp>(gem)?.Name, "") ?? "");
        if (name.Length == 0) return "";
        var groups = _build.SkillSets[_buildSkillSet].Groups.Where(g => g.Enabled && g.Gems.Any(x => x.Enabled && GemKey(x.Name) == GemKey(name))).Select(g => g.Title).Distinct().ToList();
        return groups.Count > 0 ? T($"Build: for {string.Join(", ", groups)}", $"Build: para {string.Join(", ", groups)}")
            : T($"Build: not in {SkillSetName(_buildSkillSet)}", $"Build: no está en {SkillSetName(_buildSkillSet)}");
    }
}
