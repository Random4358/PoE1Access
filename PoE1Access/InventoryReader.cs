using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using BaseComp = ExileCore.PoEMemory.Components.Base;
using ModsComp = ExileCore.PoEMemory.Components.Mods;
using QualityComp = ExileCore.PoEMemory.Components.Quality;
using SocketsComp = ExileCore.PoEMemory.Components.Sockets;
using StackComp = ExileCore.PoEMemory.Components.Stack;
using GemComp = ExileCore.PoEMemory.Components.SkillGem;
using ReqComp = ExileCore.PoEMemory.Components.AttributeRequirements;
using StatsComp = ExileCore.PoEMemory.Components.Stats;
using NormalInventoryItem = ExileCore.PoEMemory.Elements.InventoryElements.NormalInventoryItem;
using static PoE1Access.Loc;

namespace PoE1Access;

/// <summary>
/// The inventory, read the way Path of Exile 2 Access reads it:
///  * Selecting an item: "Rusted Sword, column 4, row 1", then a missing requirement
///    ("Cannot use: 21 Int"), then the tooltip lines. Equipped: "Helmet: Iron Hat".
///  * In a menu: tap L3 (Insert) the next tooltip line, double tap (F1) the previous one,
///    hold half a second (Delete) the whole tooltip, hold a second and a half (End) the whole
///    inventory grid.
///  * Items in hand: "In hand: …", then "Equipped: …, Helmet", "Placed at column 3, row 2" or
///    "You no longer hold an item"; "Unequipped: …" when something leaves an equipment slot.
/// Positions come from the game's own inventory lists (ServerData), as the item browser's do.
/// </summary>
public partial class PoE1AccessCore
{
    // ---- Where an item is ----

    private enum ItemPlace { None, Bag, Equipped, Hand }

    private (ItemPlace Place, string Label, int Col, int Row) Locate(Entity item)
    {
        if (item == null || item.Address == 0) return (ItemPlace.None, "", 0, 0);
        ServerData server = GetServerData();
        if (server == null) return (ItemPlace.None, "", 0, 0);

        foreach (var s in Slots(server, InventorySlotE.MainInventory1))
            if (s.Item?.Address == item.Address) return (ItemPlace.Bag, "", s.PosX + 1, s.PosY + 1);
        foreach (var (slot, label) in EquipmentSlots)
            foreach (var s in Slots(server, slot))
                if (s.Item?.Address == item.Address)
                    return (ItemPlace.Equipped, slot == InventorySlotE.Flask1 ? label + " " + (s.PosX + 1) : label, 0, 0);
        foreach (var s in Slots(server, InventorySlotE.Cursor1))
            if (s.Item?.Address == item.Address) return (ItemPlace.Hand, "", 0, 0);
        return (ItemPlace.None, "", 0, 0);
    }

    private IEnumerable<ServerInventory.InventSlotItem> Slots(ServerData server, InventorySlotE slot)
    {
        ServerInventory inv = Try(() => server.GetPlayerInventoryBySlot(slot), null);
        if (inv == null || inv.Address == 0) return Enumerable.Empty<ServerInventory.InventSlotItem>();
        var items = Try(() => inv.InventorySlotItems, null);
        return items != null ? items.Where(s => s.Item != null && s.Item.Address != 0) : Enumerable.Empty<ServerInventory.InventSlotItem>();
    }

    // ---- What an item is ----

    // "Rusted Sword", "Rare Grim Bane, Iron Hat", "Orb of Transmutation, 6" (without the
    // stack's size when withStack is false).
    private string ItemTitle(Entity item, bool withStack = true)
    {
        BaseComp baseC = GetComp<BaseComp>(item);
        ModsComp mods = GetComp<ModsComp>(item);
        StackComp stack = GetComp<StackComp>(item);

        string baseName = baseC != null ? CleanText(Try(() => baseC.Name, "")) : "";
        if (baseName.Length == 0) baseName = LastPathSegment(Try(() => item.Path, ""));

        ItemRarity rarity = mods != null ? Try(() => mods.ItemRarity, ItemRarity.Normal) : ItemRarity.Normal;
        string unique = mods != null && (rarity == ItemRarity.Rare || rarity == ItemRarity.Unique)
            ? CleanText(Try(() => mods.UniqueName, "")) : "";

        string title = unique.Length > 0 && !string.Equals(unique, baseName, StringComparison.OrdinalIgnoreCase)
            ? unique + ", " + baseName : MagicFullName(item, baseName);
        string rarityWord = rarity switch
        {
            ItemRarity.Magic => T("magic", "mágico"),
            ItemRarity.Rare => T("rare", "raro"),
            ItemRarity.Unique => T("unique", "único"),
            _ => "",
        };
        if (rarityWord.Length > 0) title += ", " + rarityWord;

        int size = stack != null ? Try(() => stack.Size, 0) : 0;
        if (withStack && size > 1) title += ", " + size;
        return title;
    }

    // "Cannot use: 21 Int", or "" when the character meets every requirement.
    private string MissingRequirement(Entity item)
    {
        var parts = new List<string>();
        ReqComp req = GetComp<ReqComp>(item);
        if (req != null)
        {
            var stats = GetComp<StatsComp>(GameController?.Player);
            int Have(GameStat s)
            {
                var d = stats != null ? Try(() => stats.StatDictionary, null) : null;
                return d != null && d.TryGetValue(s, out int v) ? v : 0;
            }
            int str = Try(() => req.strength, 0), dex = Try(() => req.dexterity, 0), intel = Try(() => req.intelligence, 0);
            if (stats != null)
            {
                if (str > Have(GameStat.Strength)) parts.Add(T($"{str} Str", $"{str} Fue"));
                if (dex > Have(GameStat.Dexterity)) parts.Add(T($"{dex} Dex", $"{dex} Des"));
                if (intel > Have(GameStat.Intelligence)) parts.Add(T($"{intel} Int", $"{intel} Int"));
            }
        }
        GemComp gem = GetComp<GemComp>(item);
        if (gem != null)
        {
            int need = Try(() => gem.RequiredLevel, 0);
            int level = Try(() => GameController.Player.GetComponent<ExileCore.PoEMemory.Components.Player>().Level, 0);
            if (need > 0 && level > 0 && need > level) parts.Insert(0, T($"level {need}", $"nivel {need}"));
        }
        string needs = string.Join(", ", parts);
        return parts.Count > 0 ? T($"Cannot use: {needs}", $"No puedes usarlo: {needs}") : "";
    }

    // The tooltip, one line per entry: item level, quality, sockets, gem level, mods...
    private List<string> ItemLines(Entity item)
    {
        var lines = new List<string>();
        BaseComp baseC = GetComp<BaseComp>(item);
        ModsComp mods = GetComp<ModsComp>(item);
        QualityComp quality = GetComp<QualityComp>(item);
        SocketsComp sockets = GetComp<SocketsComp>(item);
        GemComp gem = GetComp<GemComp>(item);

        if (gem != null)
        {
            // As the game's tooltip has them: the tags ("Attack, Projectile, AoE, Melee, Strike,
            // Fire"), the level, then the level it needs.
            string tags = GemTags(gem);
            if (tags.Length > 0) lines.Add(tags);
            int lvl = Try(() => gem.Level, 0), max = Try(() => gem.MaxLevel, 0);
            if (lvl > 0)
                lines.Add(max > 0 ? T($"Gem level {lvl} of {max}", $"Nivel de gema {lvl} de {max}") : T($"Gem level {lvl}", $"Nivel de gema {lvl}"));
            int need = Try(() => gem.RequiredLevel, 0);
            if (need > 1) lines.Add(T($"Requires level {need}", $"Requiere nivel {need}"));
        }
        int itemLevel = mods != null ? Try(() => mods.ItemLevel, 0) : 0;
        if (itemLevel > 0 && gem == null) lines.Add(T($"Item level {itemLevel}", $"Nivel de objeto {itemLevel}"));
        int q = quality != null ? Try(() => quality.ItemQuality, 0) : 0;
        if (q > 0) lines.Add(T($"Quality {q} percent", $"Calidad {q} por ciento"));
        // Damage, speed and defences, and what the item requires, as the tooltip has them.
        if (gem == null) lines.AddRange(Try(() => ItemProperties(item), new List<string>()));

        int socketCount = sockets != null ? Try(() => sockets.NumberOfSockets, 0) : 0;
        if (socketCount > 0)
        {
            int link = Try(() => sockets.LargestLinkSize, 0);
            lines.Add(socketCount == 1 ? T($"{socketCount} socket", $"{socketCount} engarce")
                : link > 1 ? T($"{socketCount} sockets, largest link {link}", $"{socketCount} engarces, enlace más grande {link}")
                : T($"{socketCount} sockets", $"{socketCount} engarces"));
            lines.AddRange(SocketLines(sockets));
        }

        var gemDetails = gem != null ? GemDetails(gem) : (new List<string>(), new List<string>());
        lines.AddRange(gemDetails.Item1);
        string description = ItemDescription(item, gem);
        if (description.Length > 0) lines.Add(description);
        lines.AddRange(gemDetails.Item2);
        string charges = FlaskChargesText(item);
        if (charges.Length > 0) lines.Add(charges);

        if (baseC != null && Try(() => baseC.isCorrupted, false)) lines.Add(T("Corrupted", "Corrupto"));
        if (mods != null && Try(() => mods.IsMirrored, false)) lines.Add(T("Mirrored", "Reflejado"));
        ItemRarity rarity = mods != null ? Try(() => mods.ItemRarity, ItemRarity.Normal) : ItemRarity.Normal;
        if (mods != null && !Try(() => mods.Identified, true) && rarity != ItemRarity.Normal) lines.Add(T("Unidentified", "Sin identificar"));

        if (mods != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int before = lines.Count;
            void Add(string label, IEnumerable<string> stats)
            {
                if (stats == null) return;
                foreach (string s in stats)
                {
                    string c = CleanText(s);
                    if (c.Length > 0 && seen.Add(c)) lines.Add(label.Length > 0 ? label + ": " + c : c);
                }
            }
            Add(T("Enchant", "Encantamiento"), Try<IEnumerable<string>>(() => mods.EnchantedStats, null));
            Add(T("Implicit", "Implícito"), Try<IEnumerable<string>>(() => mods.HumanImpStats, null));
            Add(T("Fractured", "Fracturado"), Try<IEnumerable<string>>(() => mods.FracturedStats, null));
            Add(T("Crafted", "Fabricado"), Try<IEnumerable<string>>(() => mods.HumanCraftedStats, null));
            Add("", Try<IEnumerable<string>>(() => mods.HumanStats, null));

            if (lines.Count == before)
            {
                // The readable lists came back empty: the modifiers' own translations.
                var raw = Try<List<ItemMod>>(() => mods.ItemMods, null);
                if (raw != null)
                    foreach (ItemMod m in raw)
                    {
                        ItemMod mod = m;
                        string t = Try(() => mod.Translation, "");
                        if (string.IsNullOrWhiteSpace(t)) t = Try(() => mod.DisplayName, "");
                        t = CleanText(t);
                        if (t.Length > 0 && seen.Add(t)) lines.Add(t);
                    }
            }
        }
        return lines;
    }

    // ---- Loot sounds: which cue an item on the ground gets ----

    private GameSounds _gameSounds;
    private SoundPack _soundPack;

    // The game's drop sound for an item on the ground (GameSounds.DropEvent).
    private string LootEventOf(Entity ground)
    {
        Entity item = Try(() => ground.GetComponent<ExileCore.PoEMemory.Components.WorldItem>()?.ItemEntity, null);
        if (item == null || item.Address == 0) return null;
        string path = Try(() => item.Path, "") ?? "";
        string cls = Try(() => GameController.Files.BaseItemTypes.Translate(path)?.ClassName, "") ?? "";
        string ev = GameSounds.DropEvent(cls, path);
        // Each item's sound, once (it is worked out once per item), to find what an unexpected
        // drop sound belongs to (28 Nov 2026: a ring's sound on killing enemies, no ring listed).
        string name = Try(() => GetComp<BaseComp>(item)?.Name, "") ?? "";
        PluginLog.Write($"Loot sound: {name} | class {cls} | {path} -> {ev.Replace("event:/Sound Effects/ItemSounds/", "")}");
        return ev;
    }

    private Synth.Loot? LootKindOf(Entity ground)
    {
        Entity item = Try(() => ground.GetComponent<ExileCore.PoEMemory.Components.WorldItem>()?.ItemEntity, null);
        if (item == null || item.Address == 0) return null;
        string path = Try(() => item.Path, "") ?? "";
        string cls = Try(() => GameController.Files.BaseItemTypes.Translate(path)?.ClassName, "") ?? "";
        ModsComp mods = GetComp<ModsComp>(item);
        ItemRarity rarity = mods != null ? Try(() => mods.ItemRarity, ItemRarity.Normal) : ItemRarity.Normal;

        Synth.Loot kind;
        if (path.Contains("/QuestItems/") || cls.StartsWith("Quest", StringComparison.OrdinalIgnoreCase)) kind = Synth.Loot.Quest;
        else if (path.EndsWith("CurrencyIdentification") || path.EndsWith("CurrencyPortal")) kind = Synth.Loot.Scroll;
        else if (cls.Contains("Currency") || path.StartsWith("Metadata/Items/Currency")) kind = Synth.Loot.Currency;
        else if (cls.Contains("Gem") || path.StartsWith("Metadata/Items/Gems")) kind = Synth.Loot.Gem;
        else if (rarity == ItemRarity.Unique) kind = Synth.Loot.Unique;
        else if (cls.Contains("Flask") || path.StartsWith("Metadata/Items/Flasks")) kind = Synth.Loot.Flask;
        else if (cls == "Map" || path.StartsWith("Metadata/Items/Maps")) kind = Synth.Loot.Map;
        else if (cls.Contains("Divination") || path.StartsWith("Metadata/Items/DivinationCards")) kind = Synth.Loot.Card;
        else if (cls.Contains("Jewel") || path.StartsWith("Metadata/Items/Jewels")) kind = Synth.Loot.Jewel;
        else if (rarity == ItemRarity.Rare) kind = Synth.Loot.Rare;
        else if (rarity == ItemRarity.Magic) kind = Synth.Loot.Magic;
        else if (mods != null) kind = Synth.Loot.Normal;
        else kind = Synth.Loot.Other;

        int filter = Settings.LootFilter.Value;
        if (filter >= 1 && (kind == Synth.Loot.Normal || kind == Synth.Loot.Other || kind == Synth.Loot.Scroll)) return null;
        if (filter >= 2 && (kind == Synth.Loot.Magic || kind == Synth.Loot.Flask || kind == Synth.Loot.Jewel && rarity < ItemRarity.Rare)) return null;
        return kind;
    }

    // ---- Sockets: each linked group, each socket's colour and the gem in it ----

    // "Linked: red, Double Strike level 3; green, empty", one line per group of linked sockets.
    // Each socket of the item last read, in order: its colour, its gem or "empty", and whether
    // it is linked to another (SocketSelection says them in the game's socket mode).
    private readonly List<(string Colour, string Gem, bool Linked)> _socketTexts = new List<(string, string, bool)>();

    private List<string> SocketLines(SocketsComp sockets)
    {
        var lines = new List<string>();
        _socketTexts.Clear();
        var groups = Try(() => sockets.SocketGroup, null);
        if (groups == null || groups.Count == 0) return lines;

        var gemAt = new Dictionary<int, string>();
        var gems = Try(() => sockets.SocketedGems, null);
        if (gems != null)
            foreach (var g in gems)
            {
                Entity gem = Try(() => g.GemEntity, null);
                if (gem == null || gem.Address == 0) continue;
                string name = CleanText(Try(() => GetComp<BaseComp>(gem)?.Name, "") ?? "");
                if (name.Length == 0) continue;
                int level = Try(() => GetComp<GemComp>(gem)?.Level ?? 0, 0);
                gemAt[Try(() => g.SocketIndex, -1)] = level > 0 ? T($"{name} level {level}", $"{name} nivel {level}") : name;
            }

        static string Colour(char c) => char.ToUpperInvariant(c) switch
        {
            'R' => T("red", "rojo"), 'G' => T("green", "verde"), 'B' => T("blue", "azul"), 'W' => T("white", "blanco"),
            'A' => T("abyss", "abismo"), 'D' => T("resonator", "resonador"),
            _ => T("socket", "engarce"),
        };

        int index = 0;
        foreach (string group in groups)
        {
            if (string.IsNullOrEmpty(group)) continue;
            var parts = new List<string>();
            foreach (char c in group)
            {
                parts.Add(Colour(c) + ", " + (gemAt.TryGetValue(index, out string g) ? g : T("empty", "vacío")));
                _socketTexts.Add((Colour(c), gemAt.TryGetValue(index, out string g2) ? g2 : T("empty", "vacío"), group.Length > 1));
                index++;
            }
            string joined = string.Join("; ", parts);
            lines.Add(group.Length > 1 ? T($"Linked: {joined}", $"Enlazados: {joined}") : T($"Socket: {joined}", $"Engarce: {joined}"));
        }
        return lines;
    }

    // ---- Descriptions from the game's data ----

    private Dictionary<string, string> _currencyDescriptions;

    // ExileApi reads game text up to a fixed length, which cuts long gem descriptions short
    // ("...causes balls of molten magma"). The full text: the pointer in the record whose text
    // starts with the short one, read again without the limit.
    // The gem's tags, read from its GemEffects row: a list of GemTags rows (an id and the
    // shown name; ids such as "strength" or "grants_active_skill" have no name and are not
    // shown). Found at +0x60 on the 18 Nov 2026 patch; other offsets are searched if it moves.
    private string GemTags(GemComp gem)
    {
        long row = Try(() => gem.GemEffect?.Address ?? 0, 0L);
        if (row == 0) return "";
        var mem = Mem;
        string Ascii(long a)
        {
            if (a < 0x10000 || a > 0x7FFF_FFFF_FFFF) return null;
            string t = Try(() => mem.ReadStringU(a, 120), null);
            return t != null && t.All(c => c >= 32 && c < 127) ? t : null;
        }
        List<string> TagsAt(int off)
        {
            long count = Try(() => mem.Read<long>(row + off), 0L);
            long list = Try(() => mem.Read<long>(row + off + 8), 0L);
            if (count < 1 || count > 20 || list < 0x10000 || list > 0x7FFF_FFFF_FFFF) return null;
            var names = new List<string>();
            bool anyId = false;
            for (int i = 0; i < count; i++)
            {
                long tag = Try(() => mem.Read<long>(list + i * 16), 0L);
                string id = Ascii(Try(() => mem.Read<long>(tag), 0L));
                if (string.IsNullOrEmpty(id) || id.Any(c => c == ' ' || char.IsUpper(c))) return null;
                anyId = true;
                string name = Ascii(Try(() => mem.Read<long>(tag + 8), 0L));
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return anyId && names.Count > 0 ? names : null;
        }
        var found = TagsAt(0x60);
        for (int off = 0; found == null && off < 0x100; off += 4) found = TagsAt(off);
        return found != null ? string.Join(", ", found) : "";
    }

    private string FullText(long record, string shortText)
    {
        if (record == 0 || shortText.Length < 20) return shortText;
        var mem = Mem;
        string start = shortText.Substring(0, 20);
        for (int off = 0; off < 0x100; off++)
        {
            try
            {
                long p = mem.Read<long>(record + off);
                if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) continue;
                string s = mem.ReadStringU(p, 4000) ?? "";
                if (s.StartsWith(start, StringComparison.Ordinal) && s.Length >= shortText.Length) return s;
            }
            catch { }
        }
        return shortText;
    }

    // A support gem's description ("Supports attack skills."): in its GrantedEffects.dat row, the
    // text pointer right after the gem's name pointer (+0xEB name, +0xF3 description; rows are
    // packed, so these are not 8-aligned). Found 19 Nov 2026 on three support gems.
    private string SupportDescription(long grantedEffectRow, string gemName)
    {
        if (grantedEffectRow == 0) return "";
        var mem = Mem;
        string Str(int off)
        {
            try
            {
                long p = mem.Read<long>(grantedEffectRow + off);
                if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) return "";
                string s = mem.ReadStringU(p, 600) ?? "";
                // Any printable text: Korean, Japanese and Chinese clients are above 0x3000.
                return s.All(ch => ch >= 0x20 && ch < 0xFFF0 && !char.IsSurrogate(ch)) ? s : "";
            }
            catch { return ""; }
        }
        // Check that the name is where expected before trusting the description next to it:
        // it is the gem's own name, which ends in "Support" in the English client.
        string name = CleanText(Str(0xEB));
        if (name.Length == 0) return "";
        if (!name.Contains("Support") && !string.Equals(name, gemName, StringComparison.OrdinalIgnoreCase)) return "";
        return CleanText(Str(0xF3));
    }

    /// <summary>What the item does, from the game's own data: a gem's skill description
    /// ("Support gem" for supports), or a currency item's description ("Identifies an item").</summary>
    private string ItemDescription(Entity item, GemComp gem)
    {
        if (gem != null)
        {
            var effect = Try(() => gem.GemEffect?.GrantedEffect, null);
            if (effect == null) effect = Try(() => gem.GrantedEffect1, null);
            if (effect == null) return "";
            bool support = Try(() => effect.IsSupport, false);
            if (!support)
            {
                var active = Try(() => effect.ActiveSkill, null);
                string shortText = Try(() => active?.Description, "") ?? "";
                return CleanText(FullText(active?.Address ?? 0, shortText));
            }
            string gemName = CleanText(Try(() => GetComp<BaseComp>(item)?.Name, "") ?? "");
            string supportText = SupportDescription(effect.Address, gemName);
            return supportText.Length > 0 ? T($"Support gem. {supportText}", $"Gema de asistencia. {supportText}") : T("Support gem", "Gema de asistencia");
        }

        string path = Try(() => item.Path, "") ?? "";
        if (!path.StartsWith("Metadata/Items/Currency", StringComparison.OrdinalIgnoreCase)) return "";
        if (_currencyDescriptions == null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var c in GameController.Files.CurrencyItems.EntriesList)
                {
                    string meta = Try(() => c.Type?.Metadata, null);
                    string d = Try(() => c.Description, null);
                    if (!string.IsNullOrEmpty(meta) && !string.IsNullOrWhiteSpace(d)) map[meta] = d;
                }
            }
            catch { }
            if (map.Count == 0) return "";   // the files are not loaded yet: try again next time
            _currencyDescriptions = map;
        }
        return _currencyDescriptions.TryGetValue(path, out string desc) ? CleanText(desc) : "";
    }

    // ---- Selecting an item ----

    private List<string> _tipLines = new List<string>();
    private int _tipIndex;
    private long _tipItemAddr;
    private Entity _tipItem;

    /// <summary>What is said when the selection lands on an inventory item (or equipment slot).</summary>
    private string DescribeInventoryFocus(Element hover)
    {
        NormalInventoryItem slot = hover.AsObject<NormalInventoryItem>();
        Entity item = slot?.Item;
        if (item == null || item.Address == 0 || !item.IsValid) return null;
        _tipElement = hover;

        var (place, label, col, row) = Locate(item);
        _lastFocusWasEquipment = place == ItemPlace.Equipped;
        NoteCraftItem(item);   // for the triple tap's comparison (BuildCraft.cs)
        // A vendor's item: its price is looked for on screen (Currency.cs).
        PriceAnchor(item, place != ItemPlace.None);
        string title = ItemTitle(item);
        string head;
        switch (place)
        {
            case ItemPlace.Bag: head = T($"{title}, column {col}, row {row}", $"{title}, columna {col}, fila {row}"); break;
            case ItemPlace.Equipped: head = SlotName(label) + ": " + title; break;
            default:
                // Not in the player's own lists (a stash or merchant): the name only for now.
                head = title;
                break;
        }
        // Holding an item over another: placing it would swap them.
        if (_handItemAddr != 0 && _handItemAddr != item.Address) head = T($"Over {head}", $"Sobre {head}");

        var lines = new List<string> { head };
        string missing = MissingRequirement(item);
        if (missing.Length > 0) lines.Add(missing);
        lines.AddRange(ItemLines(item));
        // A map: its tier, Atlas Drops, rarity and pack size, and dangerous mods first (Atlas.cs).
        string mapLine = Try(() => MapSummary(item, lines), "");
        if (mapLine.Length > 0) lines.Insert(1, mapLine);
        // The followed build (BuildGear.cs): a gem's place in it, a worn piece against its piece.
        if (_build != null)
        {
            if (GetComp<GemComp>(item) != null)
            {
                string g = Try(() => BuildGemLine(item), "");
                if (g.Length > 0) lines.Insert(1, g);
            }
            else if (place == ItemPlace.Equipped)
            {
                try
                {
                    var c = BuildGearCompare(label, item);
                    if (c.Short != null) { lines.Insert(1, c.Short); lines.AddRange(c.Details); }
                }
                catch (Exception ex) { Report("build gear", ex); }
            }
        }

        _tipLines = lines;
        _tipIndex = 0;
        _tipItemAddr = item.Address;
        _tipItem = item;
        return JoinSpeech(lines);
    }

    // ---- Equipment slots, also when empty ----

    // The inventory panel's slot areas, in their fixed order (found 18 Nov 2026 by matching
    // worn items to the game's inventory lists; same order on two characters). An entry of ""
    // is a slot the game hides, or the flasks row and the bag grid, which are read item by item.
    private static readonly string[] SlotOrder =
    {
        "Helmet", "Amulet", "", "Body armour", "Off hand", "Main hand", "Swap off hand", "Swap main hand",
        "Ring 1", "Ring 2", "", "Gloves", "Belt", "Boots", "", "", "",
    };

    private bool _lastFocusWasEquipment;

    // The equipment slot keys are English (the code compares them); this is how one is said.
    private static string SlotName(string key)
    {
        if (key != null && key.StartsWith("Flask ", StringComparison.Ordinal))
        {
            string n = key.Substring(6);
            return T($"Flask {n}", $"Frasco {n}");
        }
        return EquipmentSlotName(key);
    }

    /// <summary>The equipment slot the element is (or lies in), or null: walks up to the slot
    /// area that sits directly in the inventory panel and counts its place among the slot areas.</summary>
    private string EquipmentSlotOf(Element e)
    {
        var mem = Mem;
        Element cur = e;
        for (int up = 0; up <= 4 && cur != null && cur.Address != 0; up++)
        {
            Element parent = UiText.ParentOf(Mem, cur);
            Element panel = parent != null ? UiText.ParentOf(Mem, parent) : null;
            if (panel != null && IsGameWord(UiText.Title(mem, panel), InventoryTitle))
            {
                int type = Try(() => (int)UiText.TypeOf(Mem, cur), -1);
                IList<Element> kids = Try<IList<Element>>(() => parent.Children, null);
                if (kids == null) return null;
                int k = 0;
                foreach (Element s in kids)
                {
                    if (Try(() => (int)UiText.TypeOf(Mem, s), -2) != type) continue;
                    if (s.Address == cur.Address) return k < SlotOrder.Length && SlotOrder[k].Length > 0 ? SlotOrder[k] : null;
                    k++;
                }
                return null;
            }
            cur = parent;
        }
        return null;
    }

    /// <summary>An empty equipment slot under the selection: "Helmet, empty".</summary>
    private string DescribeEmptySlot(Element hover)
    {
        string slot = EquipmentSlotOf(hover);
        if (slot == null) return null;
        _lastFocusWasEquipment = true;

        // The selection can rest on the slot rather than the item in it: name what is worn.
        ServerData server = GetServerData();
        var known = EquipmentSlots.FirstOrDefault(s => s.Label == slot);
        if (server != null && known.Label != null)
        {
            var worn = Slots(server, known.Slot).FirstOrDefault();
            if (worn?.Item != null)
            {
                string t = SlotName(slot) + ": " + ItemTitle(worn.Item);
                _tipLines = new List<string> { t };
                _tipLines.AddRange(ItemLines(worn.Item));
                _tipIndex = 0;
                _tipItemAddr = worn.Item.Address;
                return t;
            }
        }
        string emptySlot = T($"{SlotName(slot)}, empty", $"{SlotName(slot)}, vacío");
        string buildHere = Try(() => BuildGearCompare(slot, null).Short, null);
        if (buildHere != null) emptySlot += ". " + buildHere;
        _tipLines = new List<string> { emptySlot };
        _tipIndex = 0;
        _tipItemAddr = 0;
        return emptySlot;
    }

    // The controller selection in the inventory panel. The game reports no hovered element on an
    // empty slot; instead each slot area holds a "selected" int at +0x358 (1 = selected), and the
    // flasks row and the bag hold the selected cell's index at +0x340 (found 18 Nov 2026).
    private static int SlotSelectedOffset => 0x358 + UiText.Shift; private static int SlotCellOffset => 0x340 + UiText.Shift;
    private Element _inventoryPanel;
    private string _lastSelectedSlot = "";
    private long _nextSlotPollMs;

    private void PollEquipmentSelection(long now)
    {
        if (now < _nextSlotPollMs) return;
        _nextSlotPollMs = now + 100;
        Element panel = _inventoryPanel;
        if (panel == null || panel.Address == 0 || !Try(() => UiText.Visible(Mem, panel), false)) { _lastSelectedSlot = ""; return; }

        var mem = Mem;
        IList<Element> kids = Try<IList<Element>>(() => panel.Children[0].Children, null);
        if (kids == null || kids.Count > 80) return;
        int slotType = -1, k = 0, selected = -1, cell = -1;
        foreach (Element s in kids)
        {
            int type = Try(() => (int)UiText.TypeOf(Mem, s), -2);
            if (slotType < 0)
            {
                if (type <= 0) continue;
                slotType = type;   // the first typed child is the first slot area (the helmet)
            }
            if (type != slotType) continue;
            if (Try(() => mem.Read<int>(s.Address + SlotSelectedOffset), 0) == 1)
            {
                selected = k;
                cell = Try(() => mem.Read<int>(s.Address + SlotCellOffset), -1);
                break;
            }
            k++;
        }

        string key = selected < 0 ? "" : selected + ":" + cell;
        if (key == _lastSelectedSlot) return;
        _lastSelectedSlot = key;
        if (selected < 0) return;

        string label = selected < SlotOrder.Length ? SlotOrder[selected] : "";
        ServerData server = GetServerData();
        if (server == null) return;

        if (label.Length > 0)
        {
            _lastFocusWasEquipment = true;
            var known = EquipmentSlots.FirstOrDefault(s => s.Label == label);
            if (known.Label == null || Slots(server, known.Slot).Any()) return;   // filled: the item reader speaks
            string emptyLabel = T($"{SlotName(label)}, empty", $"{SlotName(label)}, vacío");
            string buildHere = Try(() => BuildGearCompare(label, null).Short, null);
            if (buildHere != null) emptyLabel += ". " + buildHere;
            _tipLines = new List<string> { emptyLabel };
            _tipIndex = 0;
            _tipItemAddr = 0;
            Say(emptyLabel, true);
            return;
        }

        if (selected == FlaskRowIndex)
        {
            _lastFocusWasEquipment = true;
            if (cell < 0 || cell > 4) return;
            if (Slots(server, InventorySlotE.Flask1).Any(f => f.PosX == cell)) return;
            int flaskNo = cell + 1;
            string text = T($"Flask {flaskNo}, empty", $"Frasco {flaskNo}, vacío");
            _tipLines = new List<string> { text };
            _tipIndex = 0;
            _tipItemAddr = 0;
            Say(text, true);
            return;
        }

        _lastFocusWasEquipment = false;   // the bag
    }

    private const int FlaskRowIndex = 14;

    /// <summary>Every equipment slot: what is in it, or empty.</summary>
    private void SayAllEquipment()
    {
        ServerData server = GetServerData();
        if (server == null) { Say(T("No equipment data", "Sin datos de equipamiento"), true); return; }
        var parts = new List<string> { T("Equipment", "Equipamiento") };
        foreach (var (slot, label) in EquipmentSlots)
        {
            var items = Slots(server, slot).OrderBy(s => s.PosX).ToList();
            if (slot == InventorySlotE.Flask1)
            {
                for (int i = 0; i < 5; i++)
                {
                    var f = items.FirstOrDefault(s => s.PosX == i);
                    string flaskName = SlotName(label + " " + (i + 1));
                    parts.Add(f != null ? flaskName + ": " + ItemTitle(f.Item) : T($"{flaskName}, empty", $"{flaskName}, vacío"));
                }
                continue;
            }
            string slotName = SlotName(label);
            parts.Add(items.Count > 0 ? slotName + ": " + ItemTitle(items[0].Item) : T($"{slotName}, empty", $"{slotName}, vacío"));
        }
        Say(JoinSpeech(parts), true);
    }

    // ---- Tooltip line by line (in menus) ----

    private bool TooltipNext(int step)
    {
        if (_tipLines.Count == 0) return MenuTipNext(step);
        int next = _tipIndex + step;
        if (next >= _tipLines.Count) { Say(T("End of tooltip", "Fin de la descripción"), true); return true; }
        if (next < 0) { Say(T($"Start of tooltip. {_tipLines[0]}", $"Inicio de la descripción. {_tipLines[0]}"), true); _tipIndex = 0; return true; }
        _tipIndex = next;
        Say(_tipLines[_tipIndex], true);
        return true;
    }

    private bool TooltipAll()
    {
        if (_tipLines.Count == 0) return MenuTipAll();
        Say(JoinSpeech(_tipLines), true);
        return true;
    }

    // ---- The whole bag ----

    private void SayWholeInventory()
    {
        ServerData server = GetServerData();
        if (server == null) { Say(T("No inventory data", "Sin datos del inventario"), true); return; }
        ServerInventory inv = Try(() => server.GetPlayerInventoryBySlot(InventorySlotE.MainInventory1), null);
        var items = Slots(server, InventorySlotE.MainInventory1).OrderBy(s => s.PosY).ThenBy(s => s.PosX).ToList();

        int cells = items.Sum(s => Math.Max(1, s.SizeX) * Math.Max(1, s.SizeY));
        int total = inv != null ? Try(() => inv.Columns * inv.Rows, 0) : 0;
        int count = items.Count, free = total - cells;
        string summary = count == 1 ? T("Inventory, 1 item", "Inventario, 1 objeto") : T($"Inventory, {count} items", $"Inventario, {count} objetos");
        if (total > 0) summary += T($", {free} of {total} cells free", $", {free} de {total} casillas libres");
        var parts = new List<string> { summary };
        foreach (var s in items)
        {
            string itemName = ItemTitle(s.Item);
            int col = s.PosX + 1, row = s.PosY + 1;
            parts.Add(T($"{itemName}, column {col}, row {row}", $"{itemName}, columna {col}, fila {row}"));
        }
        Say(JoinSpeech(parts), true);
    }

    // ---- Items in hand, equipping and unequipping ----

    private long _handItemAddr;
    private string _handItemTitle = "";
    private Dictionary<string, (long Addr, string Title)> _equipped;
    private string _equipmentOwner;
    private long _nextHandPollMs;

    private readonly Dictionary<string, (long SinceMs, string Title)> _gone = new Dictionary<string, (long, string)>();
    private readonly Dictionary<string, int> _flaskUses = new Dictionary<string, int>();

    // The same item by name (its first part: "Small Life Flask", not ", 3 uses").
    private static bool SameBase(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && a.Split(',')[0] == b.Split(',')[0];

    /// <summary>How many times a flask can be drunk now (its charges over the charges per
    /// use), or -1 when it is not a flask.</summary>
    private int FlaskUses(Entity item)
    {
        var c = GetComp<ExileCore.PoEMemory.Components.Charges>(item);
        if (c == null) return -1;
        int have = Try(() => c.NumCharges, -1), per = Try(() => c.ChargesPerUse, 0);
        if (have < 0 || per <= 0) return -1;
        return have / per;
    }

    /// <summary>"Small Life Flask, 3 uses, 30 of 40 charges".</summary>
    private string FlaskChargesText(Entity item)
    {
        var c = GetComp<ExileCore.PoEMemory.Components.Charges>(item);
        if (c == null) return "";
        int have = Try(() => c.NumCharges, -1), per = Try(() => c.ChargesPerUse, 0), max = Try(() => c.ChargesMax, 0);
        if (have < 0 || per <= 0) return "";
        int uses = have / per;
        string usesText = uses == 1 ? T("1 use", "1 uso") : T($"{uses} uses", $"{uses} usos");
        return max > 0 ? usesText + T($", {have} of {max} charges", $", {have} de {max} cargas") : usesText;
    }

    private void PollInventoryMoves(long now)
    {
        if (now < _nextHandPollMs) return;
        _nextHandPollMs = now + 200;
        ServerData server = GetServerData();
        if (server == null) return;

        // Another character (a new one, or one picked from the list): what the last one wore is
        // forgotten, or its gear would be said as unequipped on entering the first area (seen
        // 27 Nov 2026: "Unequipped: Rusted Sword", "Unequipped: Plate Vest" on a new Witch).
        string owner = Try(() => GetComp<ExileCore.PoEMemory.Components.Player>(GameController.Player)?.PlayerName, null);
        if (string.IsNullOrEmpty(owner)) return;
        if (owner != _equipmentOwner)
        {
            if (_equipmentOwner != null) PluginLog.Write("Equipment: now " + owner + ", the last character's gear forgotten");
            _equipmentOwner = owner;
            _equipped = null;
            _gone.Clear();
            _flaskUses.Clear();
        }

        // Worn gear by slot.
        var equipped = new Dictionary<string, (long, string)>();
        var flaskUses = new Dictionary<string, (int Uses, string Title)>();
        foreach (var (slot, label) in EquipmentSlots)
            foreach (var s in Slots(server, slot))
            {
                string key = slot == InventorySlotE.Flask1 ? label + " " + (s.PosX + 1) : label;
                string title = ItemTitle(s.Item);
                equipped[key] = (s.Item.Address, title);
                if (slot == InventorySlotE.Flask1) flaskUses[key] = (FlaskUses(s.Item), title);
            }

        // During an area load the lists come back empty for a moment: ignore that, or every
        // worn piece would be "unequipped".
        if (equipped.Count == 0 && _equipped != null && _equipped.Count > 2) return;

        // The item in hand (the cursor's inventory).
        var hand = Slots(server, InventorySlotE.Cursor1).FirstOrDefault();
        long handAddr = hand?.Item?.Address ?? 0;

        if (_equipped != null)
        {
            // Something left an equipment slot and is not simply in another one. The game
            // rebuilds an item's data when it changes (a flask drunk: its charges), giving it a
            // new address, so the same item in the same slot counts as still there; and a slot
            // must stay empty for half a second (the lists flicker) before it is said.
            foreach (var kv in _equipped)
            {
                bool stillThere = equipped.TryGetValue(kv.Key, out var now2) &&
                                  (now2.Item1 == kv.Value.Addr || now2.Item2 == kv.Value.Title || SameBase(now2.Item2, kv.Value.Title));
                bool movedSlot = equipped.Values.Any(v => v.Item1 == kv.Value.Addr);
                if (stillThere || movedSlot) { _gone.Remove(kv.Key); continue; }
                if (!_gone.ContainsKey(kv.Key)) _gone[kv.Key] = (now, kv.Value.Title);
            }
            foreach (var key in _gone.Keys.ToList())
            {
                if (equipped.ContainsKey(key)) { _gone.Remove(key); continue; }
                if (now - _gone[key].SinceMs >= 500)
                {
                    // Worn gear can be sold from a vendor's window too.
                    Say(SoldRecently(now) ? T($"Sold: {_gone[key].Title}", $"Vendido: {_gone[key].Title}")
                        : T($"Unequipped: {_gone[key].Title}", $"Desequipado: {_gone[key].Title}"), false);
                    _gone.Remove(key);
                }
            }
        }
        // Keep what was there before a slot went quiet, so its item is still known while waiting.
        foreach (var kv in _gone)
            if (!equipped.ContainsKey(kv.Key) && _equipped != null && _equipped.TryGetValue(kv.Key, out var was))
                equipped[kv.Key] = was;

        // Flasks: the uses left, said as one is drunk (or refills from empty). Two flasks of the
        // same kind refilling together are said once ("Small Life Flask ready again", not twice).
        var readyAgain = new List<string>();
        foreach (var kv in flaskUses)
        {
            if (_flaskUses.TryGetValue(kv.Key, out int before) && kv.Value.Uses >= 0 && before >= 0)
            {
                if (kv.Value.Uses < before)
                {
                    string flaskTitle = kv.Value.Title;
                    int usesLeft = kv.Value.Uses;
                    Say(usesLeft == 1 ? T($"{flaskTitle}, 1 use left", $"{flaskTitle}, 1 uso restante")
                        : T($"{flaskTitle}, {usesLeft} uses left", $"{flaskTitle}, {usesLeft} usos restantes"), false);
                }
                else if (before == 0 && kv.Value.Uses > 0 && !readyAgain.Contains(kv.Value.Title))
                    readyAgain.Add(kv.Value.Title);
            }
            _flaskUses[kv.Key] = kv.Value.Uses;
        }
        if (readyAgain.Count > 0)
        {
            string flasks = string.Join(", ", readyAgain);
            Say(readyAgain.Count == 1 ? T($"{flasks} ready again", $"{flasks} listo otra vez")
                : T($"{flasks} ready again", $"{flasks} listos otra vez"), false);
        }

        TrackBag(server, handAddr, _handItemAddr, _handItemTitle, _equipped, equipped, now);

        if (handAddr != _handItemAddr)
        {
            if (handAddr != 0)
            {
                _handItemTitle = ItemTitle(hand.Item);
                Say(T($"In hand: {_handItemTitle}. Choose a cell or a matching equipment slot to place it", $"En la mano: {_handItemTitle}. Elige una casilla o una ranura de equipamiento adecuada para colocarlo"), true);
            }
            else if (_handItemAddr != 0)
            {
                // Where did it go?
                string where = "";
                foreach (var kv in equipped)
                    if (kv.Value.Item1 == _handItemAddr) { string keyName = SlotName(kv.Key); where = T($"Equipped: {_handItemTitle}, {keyName}", $"Equipado: {_handItemTitle}, {keyName}"); }
                if (where.Length == 0)
                    foreach (var s in Slots(server, InventorySlotE.MainInventory1))
                        if (s.Item.Address == _handItemAddr) { int pc = s.PosX + 1, pr = s.PosY + 1; where = T($"Placed: {_handItemTitle}, column {pc}, row {pr}", $"Colocado: {_handItemTitle}, columna {pc}, fila {pr}"); }
                Say(where.Length > 0 ? where : T("You no longer hold an item", "Ya no sostienes ningún objeto"), true);
            }
            _handItemAddr = handAddr;
        }
        _equipped = equipped;
    }
}
