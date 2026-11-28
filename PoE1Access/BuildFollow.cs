using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ExileCore.PoEMemory.MemoryObjects;
using static PoE1Access.Loc;
using BaseComp = ExileCore.PoEMemory.Components.Base;
using SocketsComp = ExileCore.PoEMemory.Components.Sockets;

namespace PoE1Access;

// Following a Path of Building build in the game. Copy a build code, or a pobb.in or pastebin
// link to one, and choose "Load a build from the clipboard" in the mod menu's Build submenu. Then:
//   - the passive tree finder has a list "Your build": the build's passives you have not taken,
//     fewest points first, each with the way to it, as the other lists;
//   - on levelling up, the build's next passives are said ("Build: next passive, Life, down left");
//   - "Your gems against the build" says, for each of the build's skill setups, where you have it
//     and which of its gems are missing or not linked with it;
//   - "Passives the build does not take" lists what you took off the build's path.
// A leveling guide has several trees ("Act 1 End", "End Act 2"...) and skill sets: the one followed
// is chosen in the menu, or by itself, the first tree with passives you have not taken yet.
// Everything is read and said; nothing is allocated or socketed for you. The build is kept in
// app\build-code.txt and the choices in app\build-choice.txt.
public partial class PoE1AccessCore
{
    private BuildPlan _build;
    private int _buildSpecChoice = -1;   // -1: by itself
    private int _buildSkillSet;
    private volatile string _buildPendingCode, _buildPendingError;
    private bool _buildLoaded;

    private static string AppFile(string name) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", name));

    private void LoadSavedBuild()
    {
        if (_buildLoaded) return;
        _buildLoaded = true;
        try
        {
            string file = AppFile("build-code.txt");
            if (!File.Exists(file)) return;
            string xml = BuildPlan.Decode(File.ReadAllText(file));
            if (xml == null) return;
            _build = BuildPlan.Parse(xml);
            if (_build == null) return;
            _buildXml = xml;
            _buildSkillSet = _build.ActiveSkillSet;
            string choice = AppFile("build-choice.txt");
            if (File.Exists(choice))
            {
                var parts = File.ReadAllText(choice).Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int sp) && int.TryParse(parts[1], out int sk))
                {
                    _buildSpecChoice = sp >= -1 && sp < _build.Specs.Count ? sp : -1;
                    _buildSkillSet = sk >= 0 && sk < _build.SkillSets.Count ? sk : _build.ActiveSkillSet;
                }
            }
            PluginLog.Write("Build loaded from file: " + _build.ClassName + " " + _build.AscendancyName);
        }
        catch (Exception ex) { PluginLog.Write("Build file not read: " + ex.Message); }
    }

    private void SaveBuildChoice()
    {
        try { File.WriteAllText(AppFile("build-choice.txt"), _buildSpecChoice + "," + _buildSkillSet); } catch { }
    }

    // ---- Loading ----

    private void LoadBuildFromClipboard()
    {
        string text = (ClipboardText() ?? "").Trim();
        if (text.Length == 0) { Say(T("The clipboard has no text. Copy a Path of Building code or a pobb.in or pastebin link first.", "El portapapeles no tiene texto. Copia antes un código de Path of Building o un enlace de pobb.in o pastebin."), true); return; }
        string url = null;
        var pobb = Regex.Match(text, @"pobb\.in/(?:u/[^/\s]+/)?([A-Za-z0-9_-]+)");
        var paste = Regex.Match(text, @"pastebin\.com/(?:raw/)?([A-Za-z0-9]+)");
        if (pobb.Success) url = "https://pobb.in/" + pobb.Groups[1].Value + "/raw";
        else if (paste.Success) url = "https://pastebin.com/raw/" + paste.Groups[1].Value;
        if (url == null) { _buildPendingCode = text; return; }
        Say(T("Fetching the build.", "Descargando la build."), true);
        PluginLog.Write("Build: fetching " + url);
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE1Access");
                _buildPendingCode = await http.GetStringAsync(url);
            }
            catch (Exception ex) { _buildPendingError = ex.Message; }
        });
    }

    /// <summary>Applies a build fetched or pasted (from the tick).</summary>
    private void PollBuild()
    {
        LoadSavedBuild();
        PollPoB();
        string err = _buildPendingError;
        if (err != null)
        {
            _buildPendingError = null;
            PluginLog.Write("Build: fetch failed: " + err);
            Say(T($"Could not fetch the build: {err}", $"No se pudo descargar la build: {err}"), true);
        }
        string code = _buildPendingCode;
        if (code == null) return;
        _buildPendingCode = null;
        string xml = BuildPlan.Decode(code);
        BuildPlan plan = null;
        try { if (xml != null) plan = BuildPlan.Parse(xml); } catch (Exception ex) { PluginLog.Write("Build: not read: " + ex.Message); }
        if (plan == null) { Say(T("That is not a Path of Building build code.", "Eso no es un código de build de Path of Building."), true); return; }
        _build = plan;
        _buildXml = xml;
        _pobBuildResult = null;
        _buildSpecChoice = -1;
        _buildSkillSet = plan.ActiveSkillSet;
        try { File.WriteAllText(AppFile("build-code.txt"), code.Trim()); } catch { }
        SaveBuildChoice();
        // How many of its passives the game's own list knows (they should all be known).
        LearnPassiveRows();
        var ids = new HashSet<ushort>(_passiveIdOfRow.Values);
        var all = plan.Specs.SelectMany(s => s.Nodes).Distinct().ToList();
        PluginLog.Write($"Build loaded: {plan.ClassName} {plan.AscendancyName}, {plan.Specs.Count} trees, {plan.SkillSets.Count} skill sets; {all.Count(ids.Contains)} of {all.Count} passives found in the game's list");
        Say(T("Build loaded. ", "Build cargada. ") + BuildHeadline(), true);
    }

    private string BuildHeadline()
    {
        var b = _build;
        var parts = new List<string>();
        string who = b.ClassName + (b.AscendancyName.Length > 0 ? ", " + b.AscendancyName : "");
        if (who.Length > 0) parts.Add(b.Level > 0 ? T($"{who}, level {b.Level}", $"{who}, nivel {b.Level}") : who);
        var spec = FollowedSpec();
        if (b.Specs.Count > 0)
            parts.Add(b.Specs.Count == 1 ? T("1 passive tree", "1 árbol de pasivas")
                : T($"{b.Specs.Count} passive trees, following {SpecName(spec)}", $"{b.Specs.Count} árboles de pasivas, siguiendo {SpecName(spec)}"));
        if (b.SkillSets.Count > 1) parts.Add(T($"{b.SkillSets.Count} skill sets, following {SkillSetName(_buildSkillSet)}", $"{b.SkillSets.Count} conjuntos de habilidades, siguiendo {SkillSetName(_buildSkillSet)}"));
        parts.Add(T("The rest is in the mod menu, under Build.", "El resto está en el menú del mod, en Build."));
        return JoinSpeech(parts);
    }

    // ---- Which tree and skill set ----

    /// <summary>The tree followed: the one chosen, or the first with passives not taken yet.</summary>
    private int FollowedSpec()
    {
        if (_build == null || _build.Specs.Count == 0) return -1;
        if (_buildSpecChoice >= 0) return _buildSpecChoice;
        var have = AllocatedPassives();
        if (have.Count == 0) return 0;
        for (int i = 0; i < _build.Specs.Count; i++)
            if (_build.Specs[i].Nodes.Any(n => !have.Contains(n))) return i;
        return _build.Specs.Count - 1;
    }

    private string SpecName(int i) =>
        i < 0 || _build == null ? "" : _build.Specs[i].Title.Length > 0 ? _build.Specs[i].Title : T($"tree {i + 1}", $"árbol {i + 1}");

    private string SkillSetName(int i) =>
        _build == null || i < 0 || i >= _build.SkillSets.Count ? "" : _build.SkillSets[i].Title.Length > 0 ? _build.SkillSets[i].Title : T($"skill set {i + 1}", $"conjunto {i + 1}");

    private HashSet<ushort> BuildNodes()
    {
        int i = FollowedSpec();
        return i < 0 ? new HashSet<ushort>() : new HashSet<ushort>(_build.Specs[i].Nodes);
    }

    private void CycleBuildSpec()
    {
        if (_build == null || _build.Specs.Count == 0) { Say(NoBuildText(), true); return; }
        _buildSpecChoice = _buildSpecChoice + 1 >= _build.Specs.Count ? -1 : _buildSpecChoice + 1;
        SaveBuildChoice();
        Say(DescribeBuildSpec(), true);
    }

    private string DescribeBuildSpec()
    {
        if (_build == null || _build.Specs.Count == 0) return NoBuildText();
        int i = FollowedSpec();
        string name = SpecName(i);
        return _buildSpecChoice < 0
            ? T($"Passive tree to follow: by itself, now {name}", $"Árbol a seguir: automático, ahora {name}")
            : T($"Passive tree to follow: {name}, {i + 1} of {_build.Specs.Count}", $"Árbol a seguir: {name}, {i + 1} de {_build.Specs.Count}");
    }

    private void CycleBuildSkillSet()
    {
        if (_build == null || _build.SkillSets.Count == 0) { Say(NoBuildText(), true); return; }
        _buildSkillSet = (_buildSkillSet + 1) % _build.SkillSets.Count;
        SaveBuildChoice();
        Say(DescribeBuildSkillSet(), true);
    }

    private string DescribeBuildSkillSet() =>
        _build == null || _build.SkillSets.Count == 0 ? NoBuildText()
            : T($"Skill set to follow: {SkillSetName(_buildSkillSet)}, {_buildSkillSet + 1} of {_build.SkillSets.Count}", $"Conjunto a seguir: {SkillSetName(_buildSkillSet)}, {_buildSkillSet + 1} de {_build.SkillSets.Count}");

    private static string NoBuildText() => T("No build loaded. Copy a Path of Building code or link, then choose Load a build from the clipboard.", "No hay build cargada. Copia un código o enlace de Path of Building y elige Cargar una build del portapapeles.");

    // ---- Reading the build ----

    private void SayBuildSummary()
    {
        if (_build == null) { Say(NoBuildText(), true); return; }
        var b = _build;
        var parts = new List<string> { BuildHeadline().Replace(T("The rest is in the mod menu, under Build.", "El resto está en el menú del mod, en Build."), "").Trim() };
        if (b.Bandit.Length > 0) parts.Add(T($"Bandit: {(b.Bandit == "None" ? "kill all three" : "help " + b.Bandit)}", $"Bandido: {(b.Bandit == "None" ? "matar a los tres" : "ayudar a " + b.Bandit)}"));
        if (b.SkillSets.Count > 0)
            foreach (var g in b.SkillSets[_buildSkillSet].Groups.Where(x => x.Enabled))
            {
                var gems = g.Gems.Where(x => x.Enabled).Select(x => x.Name).ToList();
                string where = g.Slot.Length > 0 ? T($" in the {g.Slot}", $" en {g.Slot}") : "";
                parts.Add($"{g.Title}{where}: " + string.Join(", ", gems));
            }
        foreach (var (slot, item) in b.Gear) parts.Add($"{slot}: {item}");
        if (b.Specs.Count > 1) parts.Add(T("Trees: ", "Árboles: ") + string.Join(", ", b.Specs.Select((s, i) => SpecName(i))));
        if (b.Notes.Length > 0) parts.Add(T("The build has notes: Read the notes, in this menu.", "La build tiene notas: Leer las notas, en este menú."));
        Say(JoinSpeech(parts), true);
    }

    private void SayBuildNotes()
    {
        if (_build == null) { Say(NoBuildText(), true); return; }
        Say(_build.Notes.Length > 0 ? _build.Notes : T("The build has no notes.", "La build no tiene notas."), true);
    }

    private void ForgetBuild()
    {
        _build = null;
        _buildXml = null;
        _pobBuildResult = null;
        try { File.Delete(AppFile("build-code.txt")); File.Delete(AppFile("build-choice.txt")); } catch { }
        Say(T("Build forgotten.", "Build olvidada."), true);
    }

    // ---- Passives ----

    /// <summary>The build's passives next to yours ("Life, Strength"), or the nearest one with
    /// how far it is; "" when there is nothing to take.</summary>
    private string BuildNextPassiveText()
    {
        if (_build == null) return "";
        LearnPassiveRows();
        var links = PassiveLinks(_clock.ElapsedMilliseconds);
        var have = AllocatedPassives();
        if (links == null || have.Count == 0) return "";
        var plan = BuildNodes();
        var rowOf = new Dictionary<ushort, long>();
        foreach (var kv in _passiveIdOfRow) rowOf[kv.Value] = kv.Key;
        var haveRows = have.Where(rowOf.ContainsKey).Select(h => rowOf[h]).ToHashSet();
        bool InPlan(long row) => _passiveIdOfRow.TryGetValue(row, out ushort id) && plan.Contains(id) && !have.Contains(id);
        // Next to your nodes.
        var next = haveRows.SelectMany(r => links.TryGetValue(r, out var l) ? l : new List<long>()).Where(InPlan).Distinct().ToList();
        if (next.Count > 0)
        {
            var names = next.Select(NodeName).Distinct().Take(3).ToList();
            return names.Count == 1 ? T($"Build: next passive, {names[0]}", $"Build: siguiente pasiva, {names[0]}")
                : T($"Build: next passives, {string.Join(", ", names)}", $"Build: siguientes pasivas, {string.Join(", ", names)}");
        }
        // Off the build's path: the nearest of its passives.
        var dist = new Dictionary<long, int>();
        var parent = new Dictionary<long, long>();
        var q = new Queue<long>();
        foreach (long r in haveRows) { dist[r] = 0; q.Enqueue(r); }
        while (q.Count > 0)
        {
            long cur = q.Dequeue();
            if (InPlan(cur))
            {
                var path = new List<long>();
                for (long x = cur; parent.TryGetValue(x, out long p) && !haveRows.Contains(p); x = p) path.Add(p);
                path.Reverse();
                string first = path.Count > 0 ? T($", allocate first {NodeName(path[0])}", $", asigna primero {NodeName(path[0])}") : "";
                return T($"Build: nearest passive, {NodeName(cur)}, {dist[cur]} points away{first}", $"Build: pasiva más cercana, {NodeName(cur)}, a {dist[cur]} puntos{first}");
            }
            if (!links.TryGetValue(cur, out var l)) continue;
            foreach (long n in l)
                if (!dist.ContainsKey(n)) { dist[n] = dist[cur] + 1; parent[n] = cur; q.Enqueue(n); }
        }
        return T($"Build: every passive of {SpecName(FollowedSpec())} is taken", $"Build: todas las pasivas de {SpecName(FollowedSpec())} están tomadas");
    }

    private void SayBuildNextPassive()
    {
        if (_build == null) { Say(NoBuildText(), true); return; }
        string t = BuildNextPassiveText();
        Say(t.Length > 0 ? t : T("Enter the game with your character to compare its passives.", "Entra en el juego con tu personaje para comparar sus pasivas."), true);
    }

    private void SayPassivesOffBuild()
    {
        if (_build == null) { Say(NoBuildText(), true); return; }
        LearnPassiveRows();
        var plan = BuildNodes();
        var have = AllocatedPassives();
        var rowOf = new Dictionary<ushort, long>();
        foreach (var kv in _passiveIdOfRow) rowOf[kv.Value] = kv.Key;
        ushort start = ClassStartPassive();
        var off = have.Where(h => h != start && !plan.Contains(h) && rowOf.ContainsKey(h)).Select(h => NodeName(rowOf[h])).ToList();
        Say(off.Count == 0 ? T($"Every passive you have is in {SpecName(FollowedSpec())}.", $"Todas tus pasivas están en {SpecName(FollowedSpec())}.")
            : T($"{off.Count} passives you have are not in {SpecName(FollowedSpec())}: {string.Join(", ", off)}", $"{off.Count} pasivas tuyas no están en {SpecName(FollowedSpec())}: {string.Join(", ", off)}"), true);
    }

    // ---- Gems ----

    private static string GemKey(string name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        if (n.EndsWith(" support")) n = n.Substring(0, n.Length - 8);
        return n;
    }

    private void SayGemsAgainstBuild()
    {
        if (_build == null || _build.SkillSets.Count == 0) { Say(NoBuildText(), true); return; }
        ServerData server = GetServerData();
        if (server == null) { Say(T("Enter the game with your character to compare its gems.", "Entra en el juego con tu personaje para comparar sus gemas."), true); return; }
        // Every linked group of your worn gear: where it is, and its gems.
        var yours = new List<(string Where, List<string> Gems)>();
        foreach (var (slot, label) in EquipmentSlots)
        {
            if (slot == ExileCore.Shared.Enums.InventorySlotE.Flask1) continue;
            foreach (var s in Slots(server, slot))
            {
                var sockets = GetComp<SocketsComp>(s.Item);
                var groups = Try(() => sockets?.SocketGroup, null);
                if (groups == null) continue;
                var gemAt = new Dictionary<int, string>();
                var socketed = Try(() => sockets.SocketedGems, null);
                if (socketed != null)
                foreach (var g in socketed)
                {
                    Entity gem = Try(() => g.GemEntity, null);
                    string name = gem == null ? "" : CleanText(Try(() => GetComp<BaseComp>(gem)?.Name, "") ?? "");
                    if (name.Length > 0) gemAt[Try(() => g.SocketIndex, -1)] = name;
                }
                int index = 0;
                foreach (string group in groups)
                {
                    if (string.IsNullOrEmpty(group)) continue;
                    var names = new List<string>();
                    for (int k = 0; k < group.Length; k++, index++) if (gemAt.TryGetValue(index, out string n)) names.Add(n);
                    if (names.Count > 0) yours.Add((EquipmentWord(label), names));
                }
            }
        }

        var lines = new List<string>();
        foreach (var g in _build.SkillSets[_buildSkillSet].Groups.Where(x => x.Enabled))
        {
            var want = g.Gems.Where(x => x.Enabled).Select(x => x.Name).ToList();
            if (want.Count == 0) continue;
            var best = yours.OrderByDescending(y => want.Count(w => y.Gems.Any(h => GemKey(h) == GemKey(w)))).FirstOrDefault();
            int got = best.Gems == null ? 0 : want.Count(w => best.Gems.Any(h => GemKey(h) == GemKey(w)));
            if (got == 0) { lines.Add(T($"{g.Title}: not in your gear. Its gems: {string.Join(", ", want)}", $"{g.Title}: no está en tu equipo. Sus gemas: {string.Join(", ", want)}")); continue; }
            var missing = want.Where(w => !best.Gems.Any(h => GemKey(h) == GemKey(w))).ToList();
            lines.Add(missing.Count == 0
                ? T($"{g.Title}: all {want.Count} gems linked, in your {best.Where}", $"{g.Title}: las {want.Count} gemas enlazadas, en {best.Where}")
                : T($"{g.Title}: in your {best.Where}, {got} of {want.Count}. Missing or not linked: {string.Join(", ", missing)}", $"{g.Title}: en {best.Where}, {got} de {want.Count}. Faltan o no están enlazadas: {string.Join(", ", missing)}"));
        }
        Say(lines.Count > 0 ? T($"{SkillSetName(_buildSkillSet)}. ", $"{SkillSetName(_buildSkillSet)}. ") + JoinSpeech(lines) : T("The skill set has no gems.", "El conjunto no tiene gemas."), true);
    }

    private static string EquipmentWord(string label) => label switch
    {
        "Main hand" => T("main hand", "mano principal"),
        "Off hand" => T("off hand", "mano secundaria"),
        "Helmet" => T("helmet", "casco"),
        "Body armour" => T("body armour", "armadura"),
        "Gloves" => T("gloves", "guantes"),
        "Boots" => T("boots", "botas"),
        "Swap main hand" => T("swap main hand", "mano principal alternativa"),
        "Swap off hand" => T("swap off hand", "mano secundaria alternativa"),
        _ => label.ToLowerInvariant(),
    };

    // ---- The menu ----

    private MenuItem BuildMenu_()
    {
        MenuItem A(string label, string hint, Action act, Func<string> describe = null) =>
            new MenuItem { Label = label, Text = hint, StartPractice = act, Describe = describe ?? (() => label) };
        string right = T("Move right.", "Muévete a la derecha.");
        return new MenuItem
        {
            Label = T("Build to follow (Path of Building)", "Build a seguir (Path of Building)"),
            Children = new List<MenuItem>
            {
                A(T("Load a build from the clipboard", "Cargar una build del portapapeles"),
                  T("Copy a Path of Building code, or a pobb.in or pastebin link, then move right.", "Copia un código de Path of Building, o un enlace de pobb.in o pastebin, y muévete a la derecha."), LoadBuildFromClipboard),
                A(T("About the build", "Sobre la build"), right, SayBuildSummary),
                A(T("Passive tree to follow", "Árbol de pasivas a seguir"), T("Move right for the next tree.", "Muévete a la derecha para el siguiente árbol."), CycleBuildSpec, DescribeBuildSpec),
                A(T("Skill set to follow", "Conjunto de habilidades a seguir"), T("Move right for the next skill set.", "Muévete a la derecha para el siguiente conjunto."), CycleBuildSkillSet, DescribeBuildSkillSet),
                A(T("The build's next passive", "La siguiente pasiva de la build"), right, SayBuildNextPassive),
                A(T("Your gems against the build", "Tus gemas frente a la build"), right, SayGemsAgainstBuild),
                A(T("Passives you have that the build does not take", "Pasivas tuyas que la build no toma"), right, SayPassivesOffBuild),
                A(T("Read the notes", "Leer las notas"), right, SayBuildNotes),
                // Path of Building's own numbers (PoBCalc.cs); Path of Building must be installed.
                A(T("Path of Building: the build's numbers", "Path of Building: los números de la build"), T("Move right. It takes a few seconds.", "Muévete a la derecha. Tarda unos segundos."), CalculateBuildWithPoB),
                A(T("Path of Building: your character's numbers", "Path of Building: los números de tu personaje"), T("Move right. It takes a few seconds.", "Muévete a la derecha. Tarda unos segundos."), CalculateCharacterWithPoB),
                A(T("Forget the build", "Olvidar la build"), right, ForgetBuild),
            },
        };
    }

    // ---- The clipboard (read only, without a window) ----

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);

    private static string ClipboardText()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { System.Threading.Thread.Sleep(20); continue; }
            try
            {
                IntPtr h = GetClipboardData(13);   // CF_UNICODETEXT
                if (h == IntPtr.Zero) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(p); } finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        }
        return null;
    }
}
