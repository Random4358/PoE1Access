using System;
using System.Collections.Generic;
using System.Linq;
using static PoE1Access.Loc;

namespace PoE1Access;

// The followed build on the passive tree, after Poe2Access's Build Planner reading: every node
// the cursor reaches says whether it is in the build and from which of the build's trees (a
// leveling guide's step: "in the build, from Act 2", "next in the build", "outside the build"),
// and, while the finder has no target of its own, ends with the build's next node and the way to
// it ("Next in the build: Life: down left"). The finder's lists Your build (the build's nodes to
// take, opening with how far along you are), Taken in the build and Taken outside the build
// follow it. Path of Building keeps no order inside one tree, so a step is the first of the
// build's trees that takes the node.
public partial class PoE1AccessCore
{
    /// <summary>The first of the build's trees with the node, or -1.</summary>
    private int BuildStepOf(ushort pid)
    {
        if (_build == null) return -1;
        for (int i = 0; i < _build.Specs.Count; i++)
            if (_build.Specs[i].Nodes.Contains(pid)) return i;
        return -1;
    }

    /// <summary>", from Act 2" when the build has several trees, else "".</summary>
    private string BuildStepWords(ushort pid)
    {
        if (_build == null || _build.Specs.Count < 2) return "";
        int i = BuildStepOf(pid);
        return i < 0 ? "" : T($", from {SpecName(i)}", $", desde {SpecName(i)}");
    }

    /// <summary>The node's place in the build, for its reading: "in the build, from Act 2",
    /// "next in the build", "outside the build", or "".</summary>
    private string BuildNodeNote(long row, ushort pid, bool allocated)
    {
        if (_build == null) return "";
        var plan = BuildNodes();
        if (pid == ClassStartPassive()) return "";
        if (!plan.Contains(pid))
        {
            // A node of a later tree of the build is still the build's.
            if (BuildStepOf(pid) >= 0) return T("in a later tree of the build", "en un árbol posterior de la build") + BuildStepWords(pid);
            return allocated ? T("outside the build", "fuera de la build") : "";
        }
        if (allocated) return T("in the build", "en la build") + BuildStepWords(pid);
        return (BuildNextRows().Contains(row) ? T("next in the build", "siguiente en la build") : T("in the build", "en la build")) + BuildStepWords(pid);
    }

    /// <summary>The build's nodes you can take next: those of the followed tree next to yours.</summary>
    private HashSet<long> BuildNextRows()
    {
        var result = new HashSet<long>();
        if (_build == null) return result;
        var links = PassiveLinks(_clock.ElapsedMilliseconds);
        if (links == null) return result;
        LearnPassiveRows();
        var plan = BuildNodes();
        var have = AllocatedPassives();
        foreach (var kv in _passiveIdOfRow)
        {
            if (!have.Contains(kv.Value) || !links.TryGetValue(kv.Key, out var near)) continue;
            foreach (long n in near)
                if (_passiveIdOfRow.TryGetValue(n, out ushort id) && plan.Contains(id) && !have.Contains(id)) result.Add(n);
        }
        return result;
    }

    /// <summary>"Next in the build: Life: down left" for a node's reading, while the finder has no
    /// target: the build's next node nearest the cursor. "" on it, or with nothing to take.</summary>
    private string BuildGuideLine(long hoverRow)
    {
        if (_build == null || _finderTarget != 0) return "";
        var next = BuildNextRows();
        if (next.Count == 0 || next.Contains(hoverRow)) return "";
        long best = 0;
        float bestDist = float.MaxValue;
        var elements = _treeElements;
        foreach (long r in next)
        {
            float d = float.MaxValue - 1;
            if (elements != null && _passiveHover != null && elements.TryGetValue(r, out var el))
                d = (Centre(el) - Centre(_passiveHover)).Length();
            if (d < bestDist) { bestDist = d; best = r; }
        }
        if (best == 0) return "";
        string more = next.Count > 1 ? T($", {next.Count} to take now", $", {next.Count} para tomar ahora") : "";
        return T($"Next in the build: {NodeName(best)}{WayDirection(best)}{more}", $"Siguiente en la build: {NodeName(best)}{WayDirection(best)}{more}");
    }

    /// <summary>How far along the build is, opening the Your build list:
    /// "Your build, 16 of 60 taken, 3 points outside the build".</summary>
    private string BuildProgressWords()
    {
        if (_build == null) return "";
        var plan = BuildNodes();
        var have = AllocatedPassives();
        ushort start = ClassStartPassive();
        plan.Remove(start);
        int taken = plan.Count(have.Contains);
        int outside = have.Count(h => h != start && !plan.Contains(h) && !_build.Specs.Any(s => s.Nodes.Contains(h)));
        string text = T($"Your build, {SpecName(FollowedSpec())}: {taken} of {plan.Count} taken", $"Tu build, {SpecName(FollowedSpec())}: {taken} de {plan.Count} tomadas");
        if (outside > 0) text += outside == 1 ? T(", 1 point outside the build", ", 1 punto fuera de la build") : T($", {outside} points outside the build", $", {outside} puntos fuera de la build");
        return text;
    }
}
