using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory;
using static PoE1Access.Loc;

namespace PoE1Access;

// Messages on the screens before the game that are plain text, not something the selection
// lands on: "A character with this name already exists." under the name box when creating a
// character (found 21 Nov 2026 two levels below the creation screen's root, beside its title
// and the name box). Any sentence that newly appears near the top of the screen is said; what
// is there when a screen opens is not (the screen's own lines are read as it opens).
public partial class PoE1AccessCore
{
    private HashSet<string> _screenTexts;
    private long _screenTextsRoot, _nextScreenTextsMs;
    private bool _deleteDialogSaid;
    private long _deleteSinceMs;
    private string _migrationSaid = "";
    private bool _justOpened;

    // A visible question anywhere on the screen (the delete confirmation), searched deeper than
    // the screen's own lines.
    private string DeepQuestion(List<Element> tops) => DeepText(tops, t => t.Contains('?'));

    // A visible sentence anywhere on the screen that `match` accepts.
    private string DeepText(List<Element> tops, Func<string, bool> match)
    {
        var mem = Mem;
        foreach (Element top in tops)
        {
            var queue = new Queue<(Element E, int D)>();
            queue.Enqueue((top, 0));
            int visited = 0;
            while (queue.Count > 0 && visited < 4000)
            {
                var (e, d) = queue.Dequeue();
                visited++;
                if (d > 0 && !Try(() => UiText.VisibleLocal(mem, e), false)) continue;
                string t = CleanText(UiText.Text(mem, e));
                if (t.Length >= 15 && t.Contains(' ') && match(t)) return t;
                if (d >= 14) continue;
                var kids = Try<IList<Element>>(() => Fresh(e)?.Children, null);
                if (kids == null || kids.Count > 200) continue;
                foreach (Element k in kids) queue.Enqueue((k, d + 1));
            }
        }
        return null;
    }

    private long _nextDeleteDiagMs;

    // While the delete confirmation is open, once a second: its delete and cancel buttons
    // (shown, highlighted, the selection's focus flag) and which window has the focus, to find
    // why A does not delete (reported 27 Nov 2026). Only written to the log.
    private void LogDeleteDialog(List<Element> tops)
    {
        var mem = Mem;
        var parts = new List<string>();
        foreach (Element top in tops)
        {
            var queue = new Queue<(Element E, int D, string Path)>();
            queue.Enqueue((top, 0, "top"));
            int visited = 0;
            while (queue.Count > 0 && visited < 4000)
            {
                var (e, d, path) = queue.Dequeue();
                visited++;
                string t = CleanText(UiText.Text(mem, e)).ToLowerInvariant();
                if (t.Length > 0 && t.Length < 40 && (t.Contains("delete") || t.Contains("cancel") || t.Contains("migration")))
                {
                    bool shown = Try(() => UiText.Visible(mem, e), false);
                    bool lit = Try(() => UiText.Highlighted(mem, e), false);
                    bool focus = Try(() => UiText.FocusFlag(UiText.ReadSelectionBlock(mem, e)), false);
                    var up = UiText.ParentOf(mem, e);
                    bool upLit = up != null && Try(() => UiText.Highlighted(mem, up), false);
                    bool upFocus = up != null && Try(() => UiText.FocusFlag(UiText.ReadSelectionBlock(mem, up)), false);
                    parts.Add($"{path} '{t}' shown={shown} lit={lit} focus={focus} parentLit={upLit} parentFocus={upFocus}");
                }
                if (d >= 14) continue;
                var kids = Try<IList<Element>>(() => Fresh(e)?.Children, null);
                if (kids == null || kids.Count > 200) continue;
                for (int i = 0; i < kids.Count; i++) queue.Enqueue((kids[i], d + 1, path + "/" + i));
            }
        }
        string fg = "";
        try
        {
            IntPtr h = GetForegroundWindow();
            GetWindowThreadProcessId(h, out uint pid);
            var title = new System.Text.StringBuilder(128);
            GetWindowText(h, title, title.Capacity);
            fg = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName + " '" + title + "'";
        }
        catch { }
        PluginLog.Write("Delete dialog: focus window " + fg + "; legend " + string.Join(", ", _legendNow) + "; " + string.Join(" | ", parts));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    private void PollScreenMessages(long now)
    {
        if (now < _nextScreenTextsMs) return;
        _nextScreenTextsMs = now + 400;
        Element root = UiRoot();
        if (root == null || root.Address == 0) { _screenTexts = null; return; }
        // The screen's root and the one above it (the creation screen keeps its parts beside it).
        var tops = new List<Element> { root };
        Element above = UiText.ParentOf(Mem, root);
        if (above != null && above.Address != 0) tops.Add(above);

        var texts = new HashSet<string>(StringComparer.Ordinal);
        var mem = Mem;
        foreach (Element top in tops)
        {
            var queue = new Queue<(Element E, int D)>();
            queue.Enqueue((top, 0));
            int visited = 0;
            while (queue.Count > 0 && visited < 600)
            {
                var (e, d) = queue.Dequeue();
                visited++;
                if (d > 0 && !Try(() => UiText.VisibleLocal(mem, e), false)) continue;
                string t = CleanText(UiText.Text(mem, e));
                // A sentence: long enough, with a space, not a button or a label.
                if (t.Length >= 15 && t.Contains(' ')) texts.Add(t);
                if (d >= 3) continue;
                var kids = Try<IList<Element>>(() => Fresh(e)?.Children, null);
                if (kids == null || kids.Count > 80) continue;
                foreach (Element k in kids) queue.Enqueue((k, d + 1));
            }
        }

        if (_screenTexts == null || _screenTextsRoot != root.Address)
        {
            _screenTexts = texts;   // a new screen: its lines are read by the screen's own reader
            _screenTextsRoot = root.Address;
            _migrationSaid = "";
            _justOpened = true;
        }
        // A league migration waiting in the queue: a window over the character list (found
        // 25 Nov 2026 at root's parent/9/0: "League Migration", "Your league migration request
        // is currently in the queue. Please wait for your request to be handled.", "cancel
        // migration") that keeps the other windows, such as deleting a character, from taking
        // the buttons. Said when it is there, also when the screen opens with it.
        string migration = DeepText(tops, t => t.IndexOf("migration request", StringComparison.OrdinalIgnoreCase) >= 0);
        if (migration != null && migration != _migrationSaid)
        {
            _migrationSaid = migration;
            PluginLog.Write("League migration: " + migration);
            Say(T($"League migration: {migration} It has a cancel migration button. While it waits, other windows, such as deleting a character, may not respond.",
                  $"Migración de liga: {migration} Tiene un botón para cancelar la migración. Mientras espera, otras ventanas, como borrar un personaje, pueden no responder."), false);
        }
        if (migration == null) _migrationSaid = "";
        if (_justOpened) { _justOpened = false; return; }
        // The delete confirmation stays in the screen after it closes, so it is said when the
        // game's prompts turn to its own ("A: Delete Character", "B: Cancel"), not when its text
        // changes (seen 21 Nov 2026: said on entering the character list, and missed when opened
        // again for the same character).
        // The confirmation's prompt is a combination, "A and Y together: Delete Character" (the
        // character list's own is "X: Delete Character").
        string deletePrompt = _legendNow.FirstOrDefault(e => e.StartsWith("A", StringComparison.Ordinal) && e.Contains(": ") && EndsWithGameWord(e, DeleteCharacterWord));
        bool deleting = deletePrompt != null;
        if (deleting && !_deleteDialogSaid)
        {
            if (_deleteSinceMs == 0) _deleteSinceMs = now;
            // The question can sit deeper than the screen's other lines (seen 21 Nov 2026: read
            // for one character and missed for another), so the whole screen is searched.
            string question = texts.FirstOrDefault(t => t.Contains('?')) ?? DeepQuestion(tops);
            // The buttons the game's prompt names: "A and Y together" (found 27 Nov 2026; A alone
            // does nothing).
            string keys = deletePrompt.Substring(0, deletePrompt.IndexOf(": ", StringComparison.Ordinal));
            string how = T($"Press {keys} to delete it, B cancels.", $"Pulsa {keys} para borrarlo, B cancela.");
            if (question != null)
            {
                _deleteDialogSaid = true;
                PluginLog.Write("Delete confirmation: " + question);
                Say(question + " " + how, true);
            }
            else if (now - _deleteSinceMs > 1200)
            {
                // Not found: the question in plain words, with the character the list was on.
                _deleteDialogSaid = true;
                // The character the list was on ("zireah. Level 1 Ranger. Covens League" -> "zireah").
                int dot = _lastFocusText.IndexOf(". ", StringComparison.Ordinal);
                string who = dot > 0 && _lastFocusText.IndexOf("Level", StringComparison.OrdinalIgnoreCase) > dot ? _lastFocusText.Substring(0, dot) : T("this character", "este personaje");
                PluginLog.Write("Delete confirmation not found on screen; said plainly for " + who + ". Texts: " + string.Join(" | ", texts.Take(30)));
                Say(T($"Delete {who}? Move any items you want to keep to your stash first. {how}", $"¿Borrar {who}? Mueve antes a tu alijo los objetos que quieras conservar. {how}"), true);
            }
        }
        if (deleting && now >= _nextDeleteDiagMs) { _nextDeleteDiagMs = long.MaxValue; Try(() => { LogDeleteDialog(tops); return true; }, false); }
        if (!deleting) { _deleteDialogSaid = false; _deleteSinceMs = 0; _nextDeleteDiagMs = 0; }
        // A question (a confirmation) only while a dialog is open, which the prompts show with
        // a way to cancel; one left behind in the screen is not said.
        bool dialog = deleting || _legendNow.Any(e => e.EndsWith("Cancel", StringComparison.OrdinalIgnoreCase));
        var fresh = texts.Where(t => !_screenTexts.Contains(t) && (dialog && !deleting || !t.Contains('?'))).ToList();
        _screenTexts = texts;
        foreach (string t in fresh)
        {
            if (_lastFocusText.Length > 0 && t == _lastFocusText) continue;
            // The name and class screens' own lines are said by their reader.
            if (t == _classScreenLine || t.StartsWith("Enter the name", StringComparison.OrdinalIgnoreCase)) continue;
            // Nor what was just said some other way (a class's story, a focused line).
            if (_lastSpoken.Length > 0 && (_lastSpoken.Contains(t) || t.Contains(_lastSpoken))) continue;
            PluginLog.Write("Screen message: " + t);
            Say(t, true);
        }
    }
}
