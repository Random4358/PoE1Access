using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExileCore.PoEMemory;
using static PoE1Access.Loc;

namespace PoE1Access;

// The game's button legend: the row of controller prompts it shows for the current screen
// ("Navigate Skills", "Select Skill", "Back", "Use Skill" in skill binding mode). Each entry is a
// button picture and a label. The pictures are boxes cut from one sheet of controller icons
// (Gamepad/XboxOne/1.dds|left,top,right,bottom), matched to buttons by the game's own
// picture names (see LegendButtons). Boxes not named yet are read by their label only.
//
// Also skill binding mode (R3 in play, as the game does it on controller): its start and end
// are said, and the details box of the selected skill is read when it changes.
public partial class PoE1AccessCore
{
    // Boxes on the controller icon sheet, by the game's own picture names (found 20 Nov 2026 in
    // UI elements that hold both a name such as "Controller/Button4" and its box; the game's
    // button table names A "Button4", B "Button3", X "Button1", Y "Button2"). Boxes not named
    // there are read by their label only, rather than with a guessed button.
    private static readonly Dictionary<string, string> LegendButtons = new Dictionary<string, string>
    {
        ["c2,39"] = "A",
        ["61,39"] = "B",
        ["14e,0"] = "X",
        ["0,39"] = "Y",
        ["c2,99"] = "D-pad up",
        ["184,c0"] = "L3",
        ["c2,f9"] = "R3",
        ["0,f9"] = "Start",
        // Checked 20 Nov 2026 against the prompts on screen (PS5 pictures): "L1 Current League
        // Info" on the login screen, "R Navigate Skills" in skill binding, "L Switch League" and
        // "R1 Toggle Solo Self-Found" on the league screen, "L Switch Waypoint" and "R Browse
        // Primary Areas" on the world map.
        ["184,120"] = "Left bumper",
        ["0,159"] = "Right bumper",
        ["123,c0"] = "Left stick",
        ["61,f9"] = "Right stick",
        // "Close" in the character windows, which the View button (Share on PlayStation) opens
        // and closes; its picture is the sheet's first box.
        ["0,0"] = "View button",
    };

    private List<Element> _legends = new List<Element>();
    private readonly HashSet<string> _unnamedPictures = new HashSet<string>();
    private long _legendRoot;
    private long _nextLegendSearchMs, _nextLegendPollMs, _legendFullSearchMs;
    private string _legendSig = "", _actionMenuSig = "";
    private List<string> _legendNow = new List<string>();
    private bool _bindingMode, _legendWasBusy, _bindingHowSaid, _bindingListOpen;
    private string _bindingDetailsPending = "";
    private long _bindingDetailsSinceMs;
    private long _guideCommandMs = -100000, _nextBindSlotPollMs;
    private long _bindingLastSeenMs;
    private string _bindingSlot = "";
    private string _bindingDetailsSig = "";
    private string _bindingStickDir = "";

    private void PollButtonLegend(long now, bool uiBusy)
    {
        // A slot shows it is selected for only a moment, so slots are checked every tick.
        // In the skill list the slots flicker as you move, so their names are left out there.
        if (_bindingMode)
        {
            var rs = _pad.RightStick;
            string dir = rs.Length() < 0.5f ? "" : Math.Abs(rs.X) > Math.Abs(rs.Y) ? (rs.X > 0 ? "right" : "left") : (rs.Y > 0 ? "up" : "down");
            if (dir != _bindingStickDir)
            {
                _bindingStickDir = dir;
                if (dir.Length > 0) PluginLog.Write("Skill binding: right stick " + dir);
            }
        }
        if (_bindingMode && !_bindingListOpen && now >= _nextBindSlotPollMs)
        {
            _nextBindSlotPollMs = now + 40;
            Try(() => { PollBindingSlots(now); return 0; }, 0);
        }
        if (now < _nextLegendPollMs) return;
        _nextLegendPollMs = now + (_bindingMode ? 40 : 250);   // a slot's mark lasts a third of a second

        var entries = LegendEntries(now);
        _legendNow = entries;

        // An item's action menu (B on an item: unsocket, sell, split): said as it opens and as
        // its actions change.
        // Its entries stay "visible" while it is closed, changing with the selected item, so it
        // is said only right after a face button is pressed.
        if (now - _pad.FacePressedMs < 1500)
        {
            var actions = Try(ActionMenuEntries, new List<string>());
            string asig = _pad.FacePressedMs + "|" + string.Join("|", actions);
            if (asig != _actionMenuSig && actions.Count > 0)
            {
                _actionMenuSig = asig;
                Say(T($"More actions: {string.Join(". ", actions)}", $"Más acciones: {string.Join(". ", actions)}"), true);
            }
        }
        // Skill binding mode is the legend offering to navigate and select skills, in play (the
        // skills panel offers "Select Skill" too, which is not the mode).
        bool panelOpen = _openPanels != null && _openPanels.Count > 0;
        bool binding = !panelOpen && entries.Any(e => EndsWithGameWord(e, NavigateSkillsWord) || EndsWithGameWord(e, SelectSkillWord));
        var details = binding ? SkillBindingDetails() : new List<string>();

        // The prompts change for a moment as you move in the mode, so it counts as closed only
        // after a second without them.
        if (binding) _bindingLastSeenMs = now;
        if (binding && !_bindingMode)
        {
            _bindingMode = true;
            _bindingDetailsSig = "";
            string why = "";
            string how = _bindingHowSaid ? "" : ". " + T("Move to a slot and its skill is said; A opens the skills you can put there, and the one you move to is read; B goes back", "Muévete a una ranura y se dice su habilidad; A abre las habilidades que puedes poner ahí, y se lee la que seleccionas; B vuelve atrás");
            _bindingHowSaid = true;
            Say(T("Skill binding", "Asignación de habilidades") + why + (entries.Count > 0 ? ". " + string.Join(". ", entries) : "") + how, true);
            _legendSig = string.Join("|", entries.OrderBy(x => x, StringComparer.Ordinal));
            return;
        }
        if (!binding && _bindingMode && now - _bindingLastSeenMs > 1200)
        {
            _bindingMode = false;
            _bindingSlot = "";
            _bindingListOpen = false;
            Say(T("Skill binding closed", "Asignación de habilidades cerrada"), true);
            _legendSig = string.Join("|", entries.OrderBy(x => x, StringComparer.Ordinal));
            return;
        }
        binding = _bindingMode;
        if (binding)
        {
            if (_skillsBar == null) SkillBindingDetails();   // finds the skill bar
            // The skill list is open while its details box shows. Its skill is said once the box
            // has settled (it fills in over a moment), and cuts off whatever was being said.
            _bindingListOpen = details.Count > 0;
            if (!_bindingListOpen) _bindingDetailsSig = "";   // opening the list again says its skill again
            string dsig = string.Join("|", details);
            if (dsig != _bindingDetailsPending) { _bindingDetailsPending = dsig; _bindingDetailsSinceMs = now; }
            else if (dsig != _bindingDetailsSig && details.Count > 0 && now - _bindingDetailsSinceMs >= 60)
            {
                _bindingDetailsSig = dsig;
                var say = new List<string>(details);
                string place = BindingListPlace(details[0]);
                if (place.Length > 0) say.Insert(1, place);
                Say(JoinSpeech(say), true);
            }
        }

        // The legend is said as it changes in windows and menus (in play it changes with
        // whatever you stand by, which would be chatter; hold L3 hears it there).
        string sig = string.Join("|", entries.OrderBy(x => x, StringComparer.Ordinal));
        // A window or menu becoming active says its controls even when they were already on
        // screen (the mod started with the window open, or noticed it late).
        if (uiBusy && !_legendWasBusy) _legendSig = "";
        _legendWasBusy = uiBusy;
        if (sig == _legendSig) return;
        _legendSig = sig;
        // Said when they appear (a window opening, an item selected) and when they change.
        if ((uiBusy || binding) && entries.Count > 0)
            Say(T($"Controls: {string.Join(". ", entries)}", $"Controles: {string.Join(". ", entries)}"), false);
    }

    /// <summary>The legend as "X: Select Skill", one entry each, in screen order. There are
    /// several legends (play, the inventory's, ...); every shown entry of each counts.</summary>
    private List<string> LegendEntries(long now)
    {
        var mem = Mem;
        var result = new List<string>();
        Element root = UiRoot();
        // Before the game the legend is not below the screen's own root but beside it, under the
        // root above (found 20 Nov 2026: root > [8] > [1], 61 entries, on the class screen).
        if (_preGameState.Length > 0 && root != null && UiText.Name(mem, root) != "root")
        {
            Element above = UiText.ParentOf(mem, root);
            if (above != null && above.Address != 0) root = above;
        }
        long rootAddr = root?.Address ?? 0;
        if (rootAddr != _legendRoot) { _legends.Clear(); _legendRoot = rootAddr; _nextLegendSearchMs = 0; }
        if (_legends.Count == 0 || now >= _nextLegendSearchMs)
        {
            if (now >= _nextLegendSearchMs)
            {
                // The search walks much of the interface (120 to 280 ms in play, seen 28 Nov
                // 2026) and the mod's sounds wait for it, so while the legends found still look
                // like legends it is done only once a minute (every 5 s while none is found).
                bool stillThere = _legends.Count > 0 && now - _legendFullSearchMs < 60000 &&
                                  _legends.All(l => Try(() => l.ChildCount, 0L) >= 20);
                if (!stillThere) { _legends = FindLegends(root); _legendFullSearchMs = now; }
                _nextLegendSearchMs = now + 5000;
            }
            if (_legends.Count == 0) return result;
        }
        foreach (Element legend in _legends)
        {
            if (!Try(() => UiText.Visible(mem, legend), false)) continue;
            var kids = Try<IList<Element>>(() => legend.Children, null);
            if (kids == null) continue;
            foreach (Element entry in kids)
            {
                if (!Try(() => UiText.VisibleLocal(mem, entry), false)) continue;
                string line = LegendLine(entry);
                if (line.Length > 0 && !result.Contains(line)) result.Add(line);
            }
        }
        return result;
    }

    /// <summary>One legend entry as "X: Select Skill", or "" when it is not one.</summary>
    private string LegendLine(Element entry)
    {
        var mem = Mem;
        {
            {
                var parts = Try<IList<Element>>(() => entry.Children, null);
                if (parts == null || parts.Count < 2) return "";
                var labels = new List<string>();
                CollectText(entry, 3, labels);
                string label = string.Join(" ", labels.Where(l => l != "+"));
                if (label.Length == 0) return "";
                // The button picture. An entry is a picture part and a label part, both reused
                // from a pool: the picture part's main picture (+0x510) is the button, and the
                // label part keeps the same one second (+0x230) while its main picture is left
                // over from an earlier use (found 20 Nov 2026: "Back" showed B on the picture part
                // and +0x230, X as the label part's leftover).
                var buttons = new List<string>();
                var shown = parts.Where(pt => Try(() => UiText.VisibleLocal(mem, pt), false)).ToList();
                Element labelPart = shown.FirstOrDefault(pt => { var t = new List<string>(); CollectTextAll(pt, 3, t); return t.Count > 0; });
                // The label part's copy (+0x230) names the button; the picture parts' own picture
                // is used only when it names none, since after leaving the game it keeps the
                // picture of an earlier use (found 20 Nov 2026: R3 for the login screen's "L1
                // Current League Info", while the label part still had L1).
                foreach (Element part in new[] { labelPart }.Concat(shown.Where(pt => pt != labelPart)))
                {
                    if (part == null || (part != labelPart && buttons.Count > 0)) continue;
                    string box = part == labelPart ? IconBox(part, 0x230, 0x280) : IconBox(part, 0x510, 0x560);
                    if (box.Length > 0 && LegendButtons.TryGetValue(box, out string b)) buttons.Add(ButtonWord(b));
                    else if (box.Length > 0 && _unnamedPictures.Add(box)) PluginLog.Write("Legend picture not named: " + box + " (" + label + ")");
                }
                // A combination: "[A] + [Y] Delete Character" (found 27 Nov 2026 on the delete
                // confirmation: the label part names A, then a "+" part, then a picture part with
                // Y in its second picture, +0x230). Every button after the "+" is added, and the
                // line says they go together ("A and Y together: Delete Character").
                int plus = shown.FindIndex(pt => CleanText(UiText.Text(mem, pt)) == "+");
                bool combo = plus >= 0;
                if (combo)
                    foreach (Element part in shown.Skip(plus + 1))
                    {
                        string box = IconBox(part, 0x230, 0x280, 0x510, 0x560);
                        if (box.Length > 0 && LegendButtons.TryGetValue(box, out string b)) buttons.Add(ButtonWord(b));
                        else if (box.Length > 0 && _unnamedPictures.Add(box)) PluginLog.Write("Legend picture not named: " + box + " (" + label + ", after +)");
                    }
                buttons = buttons.Where(x => x.Length > 0).Distinct().ToList();
                if (combo && buttons.Count > 1) return string.Join(T(" and ", " y "), buttons) + T(" together", " a la vez") + ": " + label;
                return buttons.Count > 0 ? string.Join(T(" and ", " y "), buttons) + ": " + label : label;
            }
        }
    }

    /// <summary>An item's action menu ("Compare / More Actions": "Unsocket", "Pick Up Gem",
    /// "Sell Item"), beside the legend under the same parent: each action an entry wrapped in a
    /// box, as "X: Unsocket" (found 21 Nov 2026 at root/1/67/2/n/0, the legend at root/1/67/1).</summary>
    private List<string> ActionMenuEntries()
    {
        var mem = Mem;
        var result = new List<string>();
        foreach (Element legend in _legends)
        {
            Element parent = UiText.ParentOf(mem, legend);
            var groups = parent != null && parent.Address != 0 ? Try<IList<Element>>(() => parent.Children, null) : null;
            if (groups == null || groups.Count > 12) continue;
            // The menu comes after the legend; before it is the selected item's name.
            bool pastLegend = false;
            foreach (Element group in groups)
            {
                if (group != null && group.Address == legend.Address) { pastLegend = true; continue; }
                if (group == null || !pastLegend || _legends.Any(l => l.Address == group.Address)) continue;
                if (!Try(() => UiText.Visible(mem, group), false)) continue;
                var boxes = Try<IList<Element>>(() => group.Children, null);
                if (boxes == null || boxes.Count > 12) continue;
                foreach (Element box in boxes)
                {
                    if (!Try(() => UiText.VisibleLocal(mem, box), false)) continue;
                    Element entry = Try<Element>(() => box.GetChildAtIndex(0), null);
                    if (entry == null || entry.Address == 0 || !Try(() => UiText.VisibleLocal(mem, entry), false)) continue;
                    string line = LegendLine(entry);
                    if (line.Length > 0 && !result.Contains(line)) result.Add(line);
                }
            }
        }
        return result;
    }

    // The legends: elements holding prompt entries (dozens of them, mostly hidden, with labels
    // such as "Pick Up", "Move", "Close", "Navigate", in the client's language: LegendWords).
    private int _preGameLegendCount = -1;

    private List<Element> FindLegends(Element root)
    {
        var found = new List<Element>();
        if (root == null) return found;
        var queue = new Queue<(Element E, int D)>();
        queue.Enqueue((root, 0));
        int visited = 0;
        bool preGame = _preGameState.Length > 0;
        var known = new HashSet<string>((preGame ? PreGameLegendWords : LegendWords).SelectMany(WordsFor), StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0 && visited < 8000)
        {
            var (e, d) = queue.Dequeue();
            visited++;
            var kids = Try<IList<Element>>(() => e.Children, null);
            if (kids == null || kids.Count > 500) continue;
            if (kids.Count >= 20)
            {
                int hits = 0;
                foreach (Element k in kids.Skip(Math.Max(0, kids.Count - 16)))
                {
                    var t = new List<string>();
                    CollectTextAll(k, 3, t);
                    if (t.Any(x => known.Contains(x))) hits++;
                }
                if (hits >= 2) { found.Add(e); continue; }
            }
            if (d >= 5) continue;
            foreach (Element k in kids) queue.Enqueue((k, d + 1));
        }
        if (preGame && found.Count != _preGameLegendCount) PluginLog.Write("Legends before the game: " + found.Count);
        _preGameLegendCount = preGame ? found.Count : -1;
        return found;
    }

    // Text in an element and its children, shown or not (the legend keeps hidden entries).
    private void CollectTextAll(Element e, int depth, List<string> into)
    {
        if (e == null || depth < 0) return;
        string t = CleanText(UiText.Text(Mem, e));
        if (t.Length > 0) into.Add(t);
        var kids = Try<IList<Element>>(() => e.Children, null);
        if (kids == null || kids.Count > 30) return;
        foreach (Element k in kids) CollectTextAll(k, depth - 1, into);
    }

    // "0,39" for a picture cut from the controller icon sheet at 0,39 (its top-left corner).
    private string IconBox(Element e) => IconBox(e, 0x510, 0x560, 0x230, 0x280);

    private string IconBox(Element e, params int[] offsets)
    {
        var mem = Mem;
        foreach (int off in offsets)
        {
            try
            {
                long p = mem.Read<long>(e.Address + off + UiText.Shift);
                if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) continue;
                long s = mem.Read<long>(p + 8);
                if (s < 0x10000 || s > 0x7FFF_FFFF_FFFF) continue;
                string name = ReadAsciiOrWide(s);
                int bar = name.IndexOf('|');
                if (bar < 0 || name.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var nums = name.Substring(bar + 1).Split(',', '|');
                if (nums.Length >= 2) return nums[0] + "," + nums[1];
            }
            catch { }
        }
        return "";
    }

    private string ReadAsciiOrWide(long addr)
    {
        byte[] b = Try(() => Mem.ReadBytes(addr, 240), null);
        if (b == null) return "";
        // Wide text: every second byte zero.
        if (b.Length > 3 && b[1] == 0 && b[3] == 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < b.Length; i += 2) { char c = (char)BitConverter.ToUInt16(b, i); if (c == 0) break; sb.Append(c); }
            return sb.ToString();
        }
        var a = new StringBuilder();
        foreach (byte x in b) { if (x == 0) break; a.Append((char)x); }
        return a.ToString();
    }

    // The selected skill's details box in binding mode: the skill bar ("skills_bar")'s second
    // part ("Default Attack", "DPS 12.9", "Attack Time 0.71 sec", its description, its damage).
    private List<string> SkillBindingDetails()
    {
        var details = new List<string>();
        var mem = Mem;
        Element bar = _skillsBar;
        if (bar == null || bar.Address == 0)
        {
            if (Environment.TickCount64 < _nextSkillsBarSearchMs) return details;
            _nextSkillsBarSearchMs = Environment.TickCount64 + 3000;
            _skillsBar = bar = ChildNamed(UiRoot(), "skills_bar", 4);
            if (bar == null) return details;
        }
        // Read afresh each time: ExileApi caches an element's children and refreshes them only
        // when it ticks, and the details box is rebuilt for each skill.
        bar = Fresh(bar);
        var kids = Try<IList<Element>>(() => bar?.Children, null);
        if (kids != null && kids.Count > 1 && Try(() => UiText.Visible(mem, kids[1]), false))
            CollectText(kids[1], 10, details);
        return details;
    }

    private Element _skillsBar;

    // The same element read anew, so none of ExileApi's cached children are reused.
    private Element Fresh(Element e) =>
        e == null || e.Address == 0 ? null : Try<Element>(() => Game.GetObject<Element>(e.Address), null);
    private long _nextSkillsBarSearchMs;

    // ---- Binding mode: the slot you move to ----
    //
    // The skill bar's slots are named skillA, skillB, skillX, skillY, skillDpadU/R/D/L. Moving
    // onto one sets a byte at +0x128 (+0x130 after the patch's 8-byte shift) to 1 for about a
    // third of a second (found 20 Nov 2026); each slot's icon (its "icon1" child, or "icon4" on
    // the D-pad) holds the picture of the skill bound there ("Art/2DArt/SkillIcons/
    // ghostlythrow.dds"), which the skill's own data names too.
    private string _lastSlotSaid = "";
    private long _slotSaidMs;

    // "2 of 3" for the skill shown in the list, from the list's entries (the skill bar's second
    // part, then its second part: an entry per skill, each with the skill's picture). With only
    // one, it says so, since moving then does nothing.
    private string BindingListPlace(string skillName)
    {
        var mem = Mem;
        Element bar = Fresh(_skillsBar);
        var list = Try(() => bar?.Children[1]?.Children[1]?.Children[0], null);
        var entries = Try<IList<Element>>(() => list?.Children, null);
        if (entries == null) return "";
        var names = new List<string>();
        foreach (Element entry in entries)
        {
            string icon = "";
            var stack = new Stack<(Element E, int D)>();
            stack.Push((entry, 0));
            while (stack.Count > 0 && icon.Length == 0)
            {
                var (e, d) = stack.Pop();
                try
                {
                    long p = mem.Read<long>(e.Address + 0x230 + UiText.Shift);
                    long t = p > 0x10000 ? mem.Read<long>(p + 8) : 0;
                    string path = t > 0x10000 ? ReadAsciiOrWide(t) : "";
                    if (path.IndexOf("SkillIcons/", StringComparison.OrdinalIgnoreCase) >= 0) icon = path;
                }
                catch { }
                if (d >= 3) continue;
                var kids = Try<IList<Element>>(() => e.Children, null);
                if (kids != null) foreach (Element k in kids) stack.Push((k, d + 1));
            }
            if (icon.Length == 0) continue;
            string file = icon.Substring(icon.IndexOf("SkillIcons/", StringComparison.OrdinalIgnoreCase) + 11);
            int cut = file.IndexOfAny(new[] { '.', '|' });
            if (cut > 0) file = file.Substring(0, cut);
            names.Add(SkillNameForIcon(file));
        }
        if (names.Count == 0) return "";
        if (names.Count == 1) return T("the only skill you can put here; socket skill gems in your gear for more", "la única habilidad que puedes poner aquí; engarza gemas de habilidad en tu equipo para tener más");
        int at = names.FindIndex(n => string.Equals(n, skillName, StringComparison.OrdinalIgnoreCase));
        return at >= 0 ? T($"{at + 1} of {names.Count}", $"{at + 1} de {names.Count}") : T($"{names.Count} skills", $"{names.Count} habilidades");
    }

    private void PollBindingSlots(long now)
    {
        var mem = Mem;
        Element bar = Fresh(_skillsBar);
        if (bar == null) return;
        Element main = ChildNamed(bar, "main", 1);
        Element container = ChildNamed(main ?? bar, "skills_container", 2);
        Element indicators = ChildNamed(main ?? bar, "indicator_container", 2);
        var slots = Try<IList<Element>>(() => container?.Children, null);
        if (slots == null) return;
        var marks = Try<IList<Element>>(() => indicators?.Children, null) ?? new List<Element>();
        // The selected slot pulses for a moment (a byte at +0x128 set to 1), on the slot itself,
        // one of its pictures, or its twin in the indicator row.
        bool Pulsing(Element e, int depth)
        {
            if (e == null || e.Address == 0) return false;
            if (Try(() => mem.Read<byte>(e.Address + 0x128 + UiText.Shift), (byte)0) == 1) return true;
            if (depth <= 0) return false;
            var kids = Try<IList<Element>>(() => e.Children, null);
            if (kids == null || kids.Count > 20) return false;
            foreach (Element k in kids) if (Pulsing(k, depth - 1)) return true;
            return false;
        }
        foreach (Element slot in slots)
        {
            string name = UiText.Name(mem, slot);
            if (!name.StartsWith("skill") || name == "skills_container") continue;
            Element twin = marks.FirstOrDefault(m => UiText.Name(mem, m) == name);
            if (!Pulsing(slot, 4) && !Pulsing(twin, 4)) continue;
            // The mark lasts about a third of a second; moving back to the same slot later says it again.
            if (name == _lastSlotSaid && now - _slotSaidMs < 450) continue;
            _lastSlotSaid = name;
            _slotSaidMs = now;
            string skill = SlotSkill(slot);
            Say(SlotButton(name) + ": " + (skill.Length > 0 ? skill : T("empty", "vacía")), true);
            return;
        }
    }

    private static string SlotButton(string slot) => slot switch
    {
        "skillA" => "A", "skillB" => "B", "skillX" => "X", "skillY" => "Y",
        "skillDpadU" => ButtonWord("D-pad up"), "skillDpadR" => ButtonWord("D-pad right"), "skillDpadD" => ButtonWord("D-pad down"), "skillDpadL" => ButtonWord("D-pad left"),
        _ => slot,
    };

    // A button's spoken name: LegendButtons holds the English names, said in the mod's language here.
    private static string ButtonWord(string english) => english switch
    {
        "D-pad up" => T("D-pad up", "Cruceta arriba"),
        "D-pad down" => T("D-pad down", "Cruceta abajo"),
        "D-pad left" => T("D-pad left", "Cruceta izquierda"),
        "D-pad right" => T("D-pad right", "Cruceta derecha"),
        "Left bumper" => T("Left bumper", "LB"),
        "Right bumper" => T("Right bumper", "RB"),
        "Left stick" => T("Left stick", "Stick izquierdo"),
        "Right stick" => T("Right stick", "Stick derecho"),
        "View button" => T("View button", "Botón View"),
        _ => english,   // A, B, X, Y, L3, R3, Start
    };

    // The skill on a slot, named from its icon: the skill on your bar with that icon, or the
    // icon's own file name in words.
    private string SlotSkill(Element slot)
    {
        var mem = Mem;
        var kids = Try<IList<Element>>(() => slot.Children, null);
        if (kids == null) return "";
        foreach (Element k in kids)
        {
            string n = UiText.Name(mem, k);
            if (!n.StartsWith("icon")) continue;
            if (!Try(() => UiText.VisibleLocal(mem, k), false)) continue;
            string icon = "";
            try
            {
                long p = mem.Read<long>(k.Address + 0x230 + UiText.Shift);
                long s = p > 0x10000 ? mem.Read<long>(p + 8) : 0;
                if (s > 0x10000) icon = ReadAsciiOrWide(s);
            }
            catch { }
            int at = icon.IndexOf("SkillIcons/", StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            string file = icon.Substring(at + 11);
            int cut = file.IndexOfAny(new[] { '.', '|' });
            if (cut > 0) file = file.Substring(0, cut);
            return SkillNameForIcon(file);
        }
        return "";
    }

    private string SkillNameForIcon(string iconFile)
    {
        var actor = GetComp<ExileCore.PoEMemory.Components.Actor>(GameController.Player);
        var skills = actor != null ? Try(() => actor.ActorSkills, null) : null;
        if (skills != null)
            foreach (var s in skills)
            {
                var active = Try(() => s.EffectsPerLevel?.SkillGemWrapper?.ActiveSkill, null);
                string icon = Try(() => active?.Icon, "") ?? "";
                if (icon.Length > 0 && icon.IndexOf(iconFile, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string name = CleanText(Try(() => active.DisplayName, "") ?? "");
                    if (name.Length > 0) return name;
                }
            }
        // Words from the file name: "ghostlythrow" stays as it is, "iconbasicattack" -> "basic attack".
        string w = iconFile.StartsWith("icon", StringComparison.OrdinalIgnoreCase) ? iconFile.Substring(4) : iconFile;
        w = System.Text.RegularExpressions.Regex.Replace(w, "([a-z])([A-Z])", "$1 $2");
        if (w.Equals("basicattack", StringComparison.OrdinalIgnoreCase)) return "Default Attack";
        return w.Length > 0 ? w : iconFile;
    }
}
