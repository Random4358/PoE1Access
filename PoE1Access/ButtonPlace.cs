using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory;
using ExileCore.Shared.Enums;
using static PoE1Access.Loc;

namespace PoE1Access;

// "Log in button, 2 of 5": a selected button is said with the word button and its place among the
// buttons beside it. A button is what carries the game's click sound (IsButton); the buttons
// beside it are the shown buttons among the children of the nearest parent, up to four levels
// above, that holds two or more (a dialog's "cancel" and "delete", the login screen's buttons).
// Items, passives, character entries and settings rows are left as they are. "Say button and its
// place among the buttons" in the Reading submenu turns it off.
public partial class PoE1AccessCore
{
    /// <summary>" button, 2 of 5" (" button" when it stands alone), or "" when e is no button.</summary>
    private string ButtonSuffix(Element e)
    {
        if (!Settings.SayButtons.Value || e == null || e.Address == 0) return "";
        var mem = Mem;
        if (UiText.Read(mem, e, 0x338 + UiText.Shift).Length > 0) return "";   // a character entry
        var type = Try(() => UiText.TypeOf(mem, e), (ElementType?)null);
        if (type == ElementType.InventoryItem || type == ElementType.PassivePoint) return "";
        if (_focusRow != null || !IsButton(e)) return "";

        Element came = e;
        Element p = UiText.ParentOf(mem, e);
        for (int up = 0; up < 4 && p != null && p.Address != 0; up++)
        {
            var kids = Try<IList<Element>>(() => p.Children, null);
            if (kids != null && kids.Count <= 40)
            {
                var buttons = kids.Where(k => Try(() => UiText.VisibleLocal(mem, k), false) && IsButton(k)).ToList();
                int at = buttons.FindIndex(k => k.Address == came.Address);
                if (buttons.Count >= 2 && at >= 0)
                    return T($" button, {at + 1} of {buttons.Count}", $" botón, {at + 1} de {buttons.Count}");
            }
            came = p;
            p = UiText.ParentOf(mem, p);
        }
        return T(" button", " botón");
    }
}
