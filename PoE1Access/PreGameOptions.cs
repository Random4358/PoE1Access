using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory;
using static PoE1Access.Loc;

namespace PoE1Access;

// The options window opened from the login screen (the options button, or Y) is not in the login
// screen's own UI tree but in PreGameState's, the state below every screen before the game, at
// state +0x228 (found 28 Nov 2026: "Options", "save", "close", the Game, UI, Sound, Input,
// Notifications and Graphics tabs and every setting under them; nothing of it under the login
// screen's root, so nothing was read). While that tree is shown with something in it, it is the
// screen the mod reads before the game, as the pause menu's tree is in the game.
public partial class PoE1AccessCore
{
    private const int PreGameOverlayOffset = 0x228;
    private long _overlayCheckedMs = -1000, _overlayAddr;
    private Element _overlayRoot;

    /// <summary>PreGameState's own window (the options) while it is shown, else null.</summary>
    private Element PreGameOverlayRoot()
    {
        long now = _clock.ElapsedMilliseconds;
        if (now - _overlayCheckedMs < 200) return _overlayRoot;
        _overlayCheckedMs = now;
        Element found = Try(FindPreGameOverlay, null);
        long addr = found?.Address ?? 0;
        if (addr != _overlayAddr)
        {
            bool opened = addr != 0;
            PluginLog.Write("Pre-game window " + (opened ? "open, root 0x" + addr.ToString("X") : "closed"));
            _overlayAddr = addr;
            _focusState = null;   // another tree: start comparing afresh
            _initialFocusPending = opened ? now : 0;
            // Its title ("Options") as it opens; the selection follows.
            string title = opened ? CleanText(FirstText(found, 4, null)) : "";
            if (opened) Say(title.Length > 0 ? title : T("Options", "Opciones"), true);
        }
        _overlayRoot = found;
        return found;
    }

    private Element FindPreGameOverlay()
    {
        var game = Game;
        if (game == null) return null;
        long st = 0;
        foreach (var kv in game.AllGameStates)
            if (kv.Key.ToString() == "PreGameState") { st = kv.Value; break; }
        if (st == 0) return null;
        var active = Try<IList<ExileCore.PoEMemory.MemoryObjects.GameState>>(() => game.CurrentGameStates, null);
        if (active == null || !active.Any(a => a != null && a.Address == st)) return null;
        long p = Mem.Read<long>(st + PreGameOverlayOffset);
        if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) return null;
        Element e = Try<Element>(() => game.GetObject<Element>(p), null);
        if (e == null || !LooksLikeElement(e, out _) || !Try(() => UiText.VisibleLocal(Mem, e), false)) return null;
        // Shown with something in it: a shown child that holds a shown text (its title).
        var kids = Try<IList<Element>>(() => e.Children, null);
        if (kids == null) return null;
        foreach (Element k in kids)
        {
            if (!Try(() => UiText.VisibleLocal(Mem, k), false)) continue;
            if (CleanText(FirstText(k, 3, null)).Length > 0) return e;
        }
        return null;
    }
}
