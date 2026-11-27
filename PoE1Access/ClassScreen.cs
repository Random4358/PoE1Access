using System;
using System.Collections.Generic;
using System.Linq;
using static PoE1Access.Loc;

namespace PoE1Access;

// The class screen of character creation: the classes are Exiles standing in the scene, and the
// left stick moves an outline from one to the next. Which one has it is kept by the scene, a
// game state below CreateCharacterState: [state +0x238] is the line-up, with the number of
// classes (7) at +0xCC and the outlined one at +0xD0, by class number (0 Scion, 1 Marauder,
// 2 Ranger, 3 Witch, 4 Duelist, 5 Templar, 6 Shadow). Found 20 Nov 2026 by copying the state
// around 26 picks of all seven classes (ClassProbe): the number always named the Exile picked
// next, and the count was 7 throughout.
//
// The names are the game's own, in the client's language, from its Characters table (row Id
// at +0, name at +8, by the class's metadata path); English if that cannot be read.
public partial class PoE1AccessCore
{
    private const int ClassLineUpOffset = 0x238, ClassCountOffset = 0xCC, ClassOutlinedOffset = 0xD0;

    // By class number: the Characters row Id, and the English name.
    private static readonly (string Id, string English)[] Classes =
    {
        ("Metadata/Characters/StrDexInt/StrDexInt", "Scion"), ("Metadata/Characters/Str/Str", "Marauder"),
        ("Metadata/Characters/Dex/Dex", "Ranger"), ("Metadata/Characters/Int/Int", "Witch"),
        ("Metadata/Characters/StrDex/StrDex", "Duelist"), ("Metadata/Characters/StrInt/StrInt", "Templar"),
        ("Metadata/Characters/DexInt/DexInt", "Shadow"),
    };

    private int _outlinedClass = -2;      // -2: not read yet on this visit of the class screen
    private long _nextOutlinePollMs;
    private string[] _classNames;

    private void PollClassOutline(long now)
    {
        if (now < _nextOutlinePollMs) return;
        _nextOutlinePollMs = now + 100;
        // Only while the screen asks for a class (not once one is picked and named).
        if (_preGameState != "CreateCharacterState" || _classScreenLine.Length == 0) { _outlinedClass = -2; _classNoteSaid = false; return; }

        int outlined = OutlinedClass();
        if (outlined == _outlinedClass) return;
        bool first = _outlinedClass == -2;
        _outlinedClass = outlined;
        // On arriving, the scene still holds the last outlined class but shows no outline yet.
        if (first || outlined < 0) return;
        string name = ClassName(outlined);
        PluginLog.Write("Class outlined: " + outlined + " " + name);
        Say(name, true);
        // How it plays (ClassDescriptions.cs), after the name; moving on cuts it.
        if (Settings.ClassDescriptions.Value)
        {
            string how = ClassDescription(outlined);
            if (how.Length > 0 && !_classNoteSaid) { how += AnyClassNote; _classNoteSaid = true; }
            if (how.Length > 0) Say(how, false);
        }
    }

    private bool _classNoteSaid;

    /// <summary>The class number of the outlined Exile, or -1.</summary>
    private int OutlinedClass()
    {
        var states = Try<IList<ExileCore.PoEMemory.MemoryObjects.GameState>>(() => Game.CurrentGameStates, null);
        if (states == null) return -1;
        var mem = Mem;
        foreach (var st in states)
        {
            long addr = Try(() => st.Address, 0L);
            if (addr == 0) continue;
            long lineUp = Try(() => mem.Read<long>(addr + ClassLineUpOffset), 0L);
            if (lineUp < 0x10000 || lineUp > 0x7FFF_FFFF_FFFF) continue;
            int count = Try(() => mem.Read<int>(lineUp + ClassCountOffset), 0);
            int outlined = Try(() => mem.Read<int>(lineUp + ClassOutlinedOffset), -1);
            if (count == Classes.Length && outlined >= 0 && outlined < count) return outlined;
        }
        return -1;
    }

    private string ClassName(int number)
    {
        _classNames ??= Try(ReadClassNames, null);
        if (_classNames != null && _classNames[number].Length > 0) return _classNames[number];
        // The English fallback names are the keys; spoken in the mod's language.
        return number switch
        {
            0 => T("Scion", "Heredera"),
            1 => T("Marauder", "Merodeador"),
            2 => T("Ranger", "Exploradora"),
            3 => T("Witch", "Bruja"),
            4 => T("Duelist", "Duelista"),
            5 => T("Templar", "Templario"),
            6 => T("Shadow", "Sombra"),
            _ => Classes[number].English,
        };
    }

    // The client's Characters table ("es:Data/Characters.dat" in a Spanish client).
    private string[] ReadClassNames()
    {
        var all = Game?.Files?.AllFiles;
        if (all == null) return null;
        // Keys name the language first in other clients: "es:Data/Characters.dat".
        var keys = all.Keys.Where(k => k.EndsWith("Data/Characters.dat", StringComparison.OrdinalIgnoreCase) ||
                                       k.EndsWith("/Characters.datc64", StringComparison.OrdinalIgnoreCase)).ToList();
        string key = keys.FirstOrDefault(k => !k.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) ?? keys.FirstOrDefault();
        if (key == null) { PluginLog.Write("Class names: no Characters table among " + all.Count + " files (" + string.Join(", ", all.Keys.Where(k => k.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0).Take(8)) + ")"); return null; }
        var file = new DatFile(Mem, all[key].Ptr);
        if (file.Count <= 0 || file.Count > 64 || file.Length < 0x10) { PluginLog.Write("Class names: " + key + " has " + file.Count + " rows of " + file.Length); return null; }
        var mem = Mem;
        string Str(long row, int off)
        {
            long p = mem.Read<long>(row + off);
            if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) return "";
            string s = mem.ReadStringU(p, 200) ?? "";
            return s.All(ch => ch >= 0x20 && ch < 0xFFF0 && !char.IsSurrogate(ch)) ? s : "";
        }
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < file.Count; i++)
        {
            long row = file.First + i * file.Length;
            string id = Str(row, 0);
            if (id.Length > 0) byId[id] = CleanText(Str(row, 8));
        }
        var names = Classes.Select(c => byId.TryGetValue(c.Id, out string n) ? n : "").ToArray();
        PluginLog.Write("Class names from " + key + ": " + string.Join(", ", names));
        return names.Any(n => n.Length > 0) ? names : null;
    }
}
