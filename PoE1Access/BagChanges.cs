using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using StackComp = ExileCore.PoEMemory.Components.Stack;
using static PoE1Access.Loc;

namespace PoE1Access;

// What comes into and leaves the bag without passing through the hand, as a controller moves
// items: bought or received ("Received: Orb of Transmutation, column 3, row 1"), given to a
// vendor or a window ("Handed over: …"), to and from the stash, picked up from the ground, and
// moved within the bag. What goes through the hand or into an equipment slot is said by
// PollInventoryMoves.
//
// The game rebuilds an item's data when it changes, giving it a new address, so an item is
// matched by address first and then by its name. A change is said once the bag has looked the
// same for two polls (its lists flicker while the game updates them).
public partial class PoE1AccessCore
{
    private readonly record struct BagItem(long Addr, string Name, int Stack, int Col, int Row);

    private List<BagItem> _bagStable;      // the bag as last said
    private List<BagItem> _bagLast;        // the bag at the last poll
    private long _otherPanelSeenMs;        // when a vendor's, a reward's or another window was last open
    private long _tradeSeenMs;             // when a vendor's sell window (Accept Trade) was last open
    private readonly List<(long Addr, string Name, long Ms)> _recentHand = new List<(long, string, long)>();
    private static readonly GameWord AcceptTradeWord = new("Accept Trade", "ConsoleActionAcceptTrade");

    private void TrackBag(ServerData server, long handAddr, long handBefore, string handTitleBefore,
                          Dictionary<string, (long Addr, string Title)> equippedBefore, Dictionary<string, (long, string)> equippedNow, long now)
    {
        var bag = Slots(server, InventorySlotE.MainInventory1)
            .Select(s => new BagItem(s.Item.Address, ItemTitle(s.Item, false), StackOf(s.Item), s.PosX + 1, s.PosY + 1))
            .OrderBy(b => b.Row).ThenBy(b => b.Col).ToList();

        // Which windows are open: the stash, or another window than the character's own panels
        // (a vendor's, a quest reward's, a trade).
        var panels = _openPanels ?? new HashSet<string>();
        // What the hand held in the last two seconds: swapping it with a bag item moves both
        // between two polls.
        if (handAddr != 0 && !_recentHand.Any(h => h.Addr == handAddr)) _recentHand.Add((handAddr, NameOnly(_handItemTitle), now));
        if (handBefore != 0 && !_recentHand.Any(h => h.Addr == handBefore)) _recentHand.Add((handBefore, NameOnly(handTitleBefore), now));
        for (int h = 0; h < _recentHand.Count; h++)
            if (_recentHand[h].Addr == handAddr || _recentHand[h].Addr == handBefore) _recentHand[h] = (_recentHand[h].Addr, _recentHand[h].Name, now);
        _recentHand.RemoveAll(h => now - h.Ms > 2000);
        // A vendor's sell window has no title the panels know; its legend offers Accept Trade.
        bool trading = _legendNow.Any(l => WordsFor(AcceptTradeWord).Any(w => w.Length > 0 && l.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
        bool stash = panels.Any(t => IsGameWord(t, StashTitle));
        bool other = panels.Any(t => !IsGameWord(t, StashTitle) && !IsGameWord(t, InventoryTitle) && !IsGameWord(t, CharacterTitle) &&
                                     !IsGameWord(t, SkillsTitle) && !IsGameWord(t, WorldTitle) && !IsGameWord(t, PassivesTitle)) || trading;
        if (other) _otherPanelSeenMs = now;
        if (trading) _tradeSeenMs = now;

        // An area load empties the lists for a moment.
        if (bag.Count == 0 && _bagStable != null && _bagStable.Count > 2) return;
        bool same = _bagLast != null && _bagLast.SequenceEqual(bag);
        _bagLast = bag;
        if (_bagStable == null) { _bagStable = bag; return; }
        if (!same || _bagStable.SequenceEqual(bag)) return;

        var before = _bagStable;
        _bagStable = bag;

        // Pair the items that stayed: by address, then by name and cell, then by name.
        var left = before.ToList();
        var pairs = new List<(BagItem Was, BagItem Is)>();
        var added = new List<BagItem>();
        foreach (var b in bag)
        {
            int i = left.FindIndex(o => o.Addr == b.Addr);
            if (i < 0) i = left.FindIndex(o => o.Name == b.Name && o.Col == b.Col && o.Row == b.Row);
            if (i < 0) i = left.FindIndex(o => o.Name == b.Name && !bag.Any(n => n.Addr == o.Addr));
            // The same cell with a new name: identified or crafted in place, not a new item.
            if (i < 0) i = left.FindIndex(o => o.Col == b.Col && o.Row == b.Row && !bag.Any(n => n.Addr == o.Addr));
            if (i >= 0) { pairs.Add((left[i], b)); left.RemoveAt(i); }
            else added.Add(b);
        }
        var removed = left;

        // Through the hand or the equipment slots: said elsewhere.
        var wornBefore = new HashSet<string>((equippedBefore ?? new Dictionary<string, (long Addr, string Title)>()).Values.Select(v => NameOnly(v.Title)));
        var wornNow = new HashSet<string>(equippedNow.Values.Select(v => NameOnly(v.Item2)));
        bool ViaHand(BagItem b) => b.Addr == handAddr || b.Addr == handBefore ||
                                   _recentHand.Any(h => h.Addr == b.Addr || (h.Name.Length > 0 && h.Name == NameOnly(b.Name))) ||
                                   (handBefore != 0 && NameOnly(b.Name) == NameOnly(handTitleBefore)) ||
                                   (handAddr != 0 && NameOnly(b.Name) == NameOnly(_handItemTitle));
        added.RemoveAll(b => ViaHand(b) || (wornBefore.Contains(NameOnly(b.Name)) && !wornNow.Contains(NameOnly(b.Name))));
        removed.RemoveAll(b => ViaHand(b) || (wornNow.Contains(NameOnly(b.Name)) && !wornBefore.Contains(NameOnly(b.Name))));

        bool tradeRecently = other || now - _otherPanelSeenMs < 3000;
        var say = new List<string>();
        foreach (var b in added)
        {
            string item = WithStack(b.Name, b.Stack);
            int col = b.Col, row = b.Row;
            say.Add(stash ? T($"From the stash: {item}, column {col}, row {row}", $"Del alijo: {item}, columna {col}, fila {row}")
                : tradeRecently ? T($"Received: {item}, column {col}, row {row}", $"Recibido: {item}, columna {col}, fila {row}")
                : T($"Picked up: {item}, column {col}, row {row}", $"Recogido: {item}, columna {col}, fila {row}"));
        }
        foreach (var b in removed)
        {
            string item = WithStack(b.Name, b.Stack);
            // Items chosen in a vendor's window leave the bag when the sale is accepted, as
            // the window closes.
            if (stash) say.Add(T($"To the stash: {item}", $"Al alijo: {item}"));
            else if (SoldRecently(now)) say.Add(T($"Sold: {item}", $"Vendido: {item}"));
            else if (other) say.Add(T($"Handed over: {item}", $"Entregado: {item}"));
        }
        foreach (var (was, now2) in pairs)
        {
            string name = now2.Name;
            int count = now2.Stack;
            if (now2.Stack > was.Stack)
                say.Add(stash ? T($"From the stash: {name}, now {count}", $"Del alijo: {name}, ahora {count}")
                    : tradeRecently ? T($"Received: {name}, now {count}", $"Recibido: {name}, ahora {count}")
                    : T($"Picked up: {name}, now {count}", $"Recogido: {name}, ahora {count}"));
            else if (now2.Stack < was.Stack && (stash || other))
                say.Add(stash ? T($"To the stash: {name}, {count} left", $"Al alijo: {name}, quedan {count}")
                    : T($"Handed over: {name}, {count} left", $"Entregado: {name}, quedan {count}"));
            else if ((now2.Col != was.Col || now2.Row != was.Row) && !ViaHand(now2))
            {
                int col = now2.Col, row = now2.Row;
                say.Add(T($"Moved: {name}, column {col}, row {row}", $"Movido: {name}, columna {col}, fila {row}"));
            }
        }
        if (say.Count == 0) return;
        PluginLog.Write("Bag change (panels: " + string.Join(", ", panels) + "; legend: " + string.Join(", ", _legendNow) + "): " + string.Join(" | ", say));
        SayNews(News.Bag, JoinSpeech(say), false);
    }

    /// <summary>A vendor's sell window is open or closed in the last three seconds.</summary>
    private bool SoldRecently(long now) => _tradeSeenMs != 0 && now - _tradeSeenMs < 3000;

    private int StackOf(Entity item) => Math.Max(1, Try(() => GetComp<StackComp>(item)?.Size ?? 1, 1));

    private static string WithStack(string name, int stack) => stack > 1 ? name + ", " + stack : name;

    // An item's title without its rarity and stack ("Rusted Sword, magic" -> "Rusted Sword").
    private static string NameOnly(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        int comma = title.IndexOf(", ", StringComparison.Ordinal);
        return comma > 0 ? title.Substring(0, comma) : title;
    }
}
