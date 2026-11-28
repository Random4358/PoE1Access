using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory;
using static PoE1Access.Loc;

namespace PoE1Access;

// Tooltips of any menu, without knowing the menu: when the selection moves (the controller's
// focus or the cursor), whatever text appears on screen within a moment, outside the selected
// thing itself, is its tooltip ("Allocate passive points...", a league's rules, an option's
// explanation). It is said after the selected thing, and tap, double tap and hold L3 step through
// it as with item tooltips. A tooltip the game keeps on screen and only rewrites (one box reused
// for every entry of a list) is read again when its text changes.
//
// The focus reader already walks every shown element several times a second; an element that
// was not shown on the walk before is new. A tooltip appears as one group of new elements under
// one place in the interface, so the biggest group is taken. Item, gem and passive tooltips are
// read from the game's data instead (see InventoryReader, DescribePassive) and are left alone here.
//
// Selections the mod finds no words for are written to the log with everything under them
// ("Unread selection: ..."), so a new screen of a game update can be fixed from a log.
public partial class PoE1AccessCore
{
    private long _tipAnchorMs = -100000, _tipAnchorAddr, _tipPendingMs, _tipBoxAddr;
    private string _tipAnchorText = "", _tipPendingSig = "", _tipBoxText = "";
    private bool _tipAnchorDone = true;
    private readonly List<(long Addr, long Ms)> _recentFresh = new List<(long, long)>();
    private List<string> _menuTipLines = new List<string>();
    private int _menuTipIndex;
    private bool _hoverGeneric;
    private readonly HashSet<string> _unreadLogged = new HashSet<string>();

    /// <summary>The selection moved to `addr`, which was said as `text`: its tooltip is looked for.</summary>
    private void TooltipAnchor(long addr, string text)
    {
        if (!Settings.SpeakTooltips.Value) return;
        if (addr == _tipAnchorAddr && !_tipAnchorDone) return;
        _tipAnchorMs = _clock.ElapsedMilliseconds;
        _tipAnchorAddr = addr;
        _tipAnchorText = text ?? "";
        _tipAnchorDone = false;
        _tipPendingSig = "";
        _menuTipLines = new List<string>();
        _menuTipIndex = 0;
    }

    private void CatchTooltip(long now, Dictionary<long, (bool Flag, int Sel, bool Lit)> state,
                              Dictionary<long, (Element E, string Path)> elements, List<long> fresh)
    {
        // A panel opening shows hundreds of elements at once: those are the panel, not a tooltip.
        if (fresh.Count <= 150) foreach (long a in fresh) _recentFresh.Add((a, now));
        _recentFresh.RemoveAll(f => now - f.Ms > 1500 || !state.ContainsKey(f.Addr));
        if (_tipAnchorDone) return;
        long since = now - _tipAnchorMs;
        if (since > 1000 || now - _panelsChangedMs < 600) { _tipAnchorDone = true; return; }

        var mem = Mem;
        string anchorPath = elements.TryGetValue(_tipAnchorAddr, out var an) ? an.Path : null;
        var legendPaths = _legends.Where(l => l != null && elements.ContainsKey(l.Address)).Select(l => elements[l.Address].Path).ToList();
        bool Under(string p, string top) => p == top || p.StartsWith(top + "/", StringComparison.Ordinal);

        var lines = new List<(string Path, string Text)>();
        foreach (var f in _recentFresh)
        {
            if (f.Ms < _tipAnchorMs - 300 || !elements.TryGetValue(f.Addr, out var el)) continue;
            string p = el.Path;
            // Not the selected thing, what holds it, or the button prompts.
            if (anchorPath != null && (Under(p, anchorPath) || Under(anchorPath, p))) continue;
            if (legendPaths.Any(lp => Under(p, lp))) continue;
            string t = CleanText(UiText.Text(mem, el.E));
            if (t.Length < 2 || _tipAnchorText.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (_gameMessageSaid.TryGetValue(t, out long msgAt) && now - msgAt < 3000) continue;
            lines.Add((p, t));
        }

        // The biggest group of new text under one place (the first four steps of its path). A
        // group that holds the selection is the window around it, not a tooltip (seen 28 Nov 2026
        // in the options: switching tabs showed the new page's labels and the tab names, read as
        // a tooltip that cut off the setting just reached).
        string Group(string p) => string.Join("/", p.Split('/').Take(4));
        var group = lines.Where(l => anchorPath == null || !Under(anchorPath, Group(l.Path)))
                         .GroupBy(l => Group(l.Path)).OrderByDescending(g => g.Count()).FirstOrDefault();
        var texts = group == null ? new List<string>()
            : group.OrderBy(l => l.Path, PathOrder.Instance).Select(l => l.Text).Distinct().ToList();
        long boxAddr = 0;
        if (group != null)
            boxAddr = elements.FirstOrDefault(kv => kv.Value.Path == group.Key).Key;

        // Nothing new: the tooltip the game keeps on screen, when its text changed.
        if (texts.Count == 0 && since >= 300 && _tipBoxAddr != 0 && elements.TryGetValue(_tipBoxAddr, out var box) &&
            (anchorPath == null || (!Under(elements[_tipBoxAddr].Path, anchorPath) && !Under(anchorPath, elements[_tipBoxAddr].Path))))
        {
            var again = new List<string>();
            CollectText(box.E, 10, again);
            again = again.Where(t => _tipAnchorText.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (again.Count > 0 && JoinSpeech(again) != _tipBoxText) { texts = again; boxAddr = _tipBoxAddr; }
        }

        if (texts.Count == 0 || texts.Count > 30) return;
        string sig = string.Join("|", texts);
        if (sig != _tipPendingSig) { _tipPendingSig = sig; _tipPendingMs = now; return; }
        // It fills in over a moment: said once it has stayed the same for a scan.
        if (since < 200 || now - _tipPendingMs < 100) return;

        _tipAnchorDone = true;
        _tipBoxAddr = boxAddr;
        _tipBoxText = JoinSpeech(texts);
        _menuTipLines = texts;
        _menuTipIndex = 0;
        PluginLog.Write("Tooltip: [" + (group?.Key ?? "kept box") + "] " + string.Join(" | ", texts));
        Say(_tipBoxText, false);
    }

    /// <summary>Tap and double tap L3 in menus: the caught tooltip's next or previous line.</summary>
    private bool MenuTipNext(int step)
    {
        if (_menuTipLines.Count == 0) return false;
        int next = _menuTipIndex + step;
        if (next >= _menuTipLines.Count) { Say(T("End of tooltip", "Fin de la descripción"), true); return true; }
        if (next < 0) { Say(T($"Start of tooltip. {_menuTipLines[0]}", $"Inicio de la descripción. {_menuTipLines[0]}"), true); _menuTipIndex = 0; return true; }
        _menuTipIndex = next;
        Say(_menuTipLines[_menuTipIndex], true);
        return true;
    }

    private bool MenuTipAll()
    {
        if (_menuTipLines.Count == 0) return false;
        Say(JoinSpeech(_menuTipLines), true);
        return true;
    }

    // A selection with no words: its picture's name when it shows an item, skill or other art
    // ("Art/2DItems/Currency/CurrencyRerollRare.dds" -> "Currency Reroll Rare"), and everything
    // under it written to the log once.
    private string UnreadSelection(Element e, string path)
    {
        var mem = Mem;
        var art = new List<string>();
        var stack = new Stack<(Element E, int D)>();
        stack.Push((e, 0));
        int seen = 0;
        while (stack.Count > 0 && seen++ < 60)
        {
            var (x, d) = stack.Pop();
            foreach (int off in new[] { 0x230, 0x510 })
            {
                try
                {
                    long p = mem.Read<long>(x.Address + off + UiText.Shift);
                    if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) continue;
                    long s = mem.Read<long>(p + 8);
                    if (s < 0x10000 || s > 0x7FFF_FFFF_FFFF) continue;
                    string file = ReadAsciiOrWide(s);
                    if (file.StartsWith("Art/", StringComparison.OrdinalIgnoreCase) && !art.Contains(file)) art.Add(file);
                }
                catch { }
            }
            if (d >= 3) continue;
            var kids = Try<IList<Element>>(() => x.Children, null);
            if (kids != null && kids.Count <= 30) foreach (Element k in kids) stack.Push((k, d + 1));
        }

        string shape = string.Join("/", path.Split('/').Select(s => char.IsDigit(s.FirstOrDefault()) ? "n" : s));
        if (_unreadLogged.Add(shape + "|" + UiText.Name(mem, e)) && _unreadLogged.Count < 200)
        {
            var all = new List<string>();
            CollectTextAll(e, 4, all);
            PluginLog.Write("Unread selection: [" + path + "] name=" + UiText.Name(mem, e) +
                            " texts=" + (all.Count > 0 ? string.Join(" | ", all) : "(none)") +
                            " pictures=" + (art.Count > 0 ? string.Join(" | ", art) : "(none)"));
        }

        string named = art.FirstOrDefault(a => a.IndexOf("2DItems/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                a.IndexOf("SkillIcons/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                a.IndexOf("2DArt/", StringComparison.OrdinalIgnoreCase) >= 0);
        if (named == null) return "";
        string name = System.IO.Path.GetFileNameWithoutExtension(named.Split('|')[0]);
        if (named.IndexOf("SkillIcons/", StringComparison.OrdinalIgnoreCase) >= 0) return SkillNameForIcon(name);
        return System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z0-9])", " ");
    }

    // "root/1/10/2" before "root/1/9/0": paths in screen order, by their numbers.
    private sealed class PathOrder : IComparer<string>
    {
        public static readonly PathOrder Instance = new PathOrder();
        public int Compare(string a, string b)
        {
            var x = a.Split('/'); var y = b.Split('/');
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                int c = int.TryParse(x[i], out int xi) && int.TryParse(y[i], out int yi) ? xi.CompareTo(yi) : string.CompareOrdinal(x[i], y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
