using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ExileCore.PoEMemory.MemoryObjects;
using static PoE1Access.Loc;
using BaseComp = ExileCore.PoEMemory.Components.Base;
using GemComp = ExileCore.PoEMemory.Components.SkillGem;

namespace PoE1Access;

// Crafting your own build with Path of Building's numbers, from the mod. Each is a question asked
// on purpose (a few seconds each), never while moving:
//   - Triple tap L3 on an item (bag, stash, merchant): what wearing it would change, slot by slot
//     for a ring ("In ring 1: damage per second +12%, life +30, fire resistance minus 20").
//   - Triple tap L3 on a passive node: what taking it, with the points on the way, would change
//     ("Taking Resolute Technique, 4 points: ..."), or for one you have, what refunding it would.
//   - L3 and the right stick right on a node: plan it (with the nodes on the way) or take it out
//     of the plan, without spending a point. Planned nodes say so, and the finder lists them
//     (Your plan). The Build crafting menu says the plan's numbers against yours, follows it as
//     your build, copies it or your character as a build code, or clears it.
//   - Skill plan: which skill group Path of Building calculates, and every support gem to try on
//     it, added or instead of each support it has.
// Nothing is allocated, equipped or socketed for you.
public partial class PoE1AccessCore
{
    // What the selection was last on, for the triple tap.
    private Entity _craftItem;
    private long _craftItemMs = -100000, _craftRow, _craftRowMs = -100000;

    private void NoteCraftItem(Entity item) { _craftItem = item; _craftItemMs = _clock.ElapsedMilliseconds; }
    private void NoteCraftNode(long row) { _craftRow = row; _craftRowMs = _clock.ElapsedMilliseconds; }

    /// <summary>The triple tap in a window: true when there was an item or node to ask about.</summary>
    private bool CraftWhatIf()
    {
        long now = _clock.ElapsedMilliseconds;
        bool node = _craftRow != 0 && now - _craftRowMs < 60000 && _craftRowMs >= _craftItemMs && _passiveHover != null;
        bool item = _craftItem != null && now - _craftItemMs < 60000 && _craftItemMs > _craftRowMs;
        if (node) { Guard("what if", () => WhatIfNode(_craftRow)); return true; }
        if (item) { Guard("what if", () => WhatIfItem(_craftItem)); return true; }
        return false;
    }

    // ---- Comparing versions of your character ----

    private void RunPoBCompare(List<string> xmls, Func<List<Dictionary<string, string>>, string> words)
    {
        string pob = PoBFolder();
        if (pob == null) { Say(T("Path of Building Community is not installed. Install it from its GitHub page, then try again.", "Path of Building Community no está instalado. Instálalo desde su página de GitHub y vuelve a intentarlo."), true); return; }
        string exe = AppFile(Path.Combine("pobcalc", "PoBCalc.exe"));
        string driver = AppFile(Path.Combine("pobcalc", "driver.lua"));
        if (!File.Exists(exe) || !File.Exists(driver)) { Say(T("The mod's Path of Building helper is missing. Reinstall the mod.", "Falta el ayudante de Path of Building del mod. Reinstala el mod."), true); return; }
        if (_pobRunning) { Say(T("Path of Building is still calculating.", "Path of Building aún está calculando."), true); return; }
        _pobRunning = true;
        Say(T("Asking Path of Building.", "Preguntando a Path of Building."), true);
        Task.Run(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "poe1access-pob-compare");
            try
            {
                Directory.CreateDirectory(dir);
                var paths = new List<string>();
                for (int i = 0; i < xmls.Count; i++)
                {
                    string f = Path.Combine(dir, "v" + (i + 1) + ".xml");
                    File.WriteAllText(f, xmls[i], new UTF8Encoding(false));
                    paths.Add(f);
                }
                string list = Path.Combine(dir, "list.txt"), outFile = Path.Combine(dir, "out.txt");
                File.WriteAllLines(list, paths);
                File.Delete(outFile);
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string a in new[] { pob, driver, list, outFile, "compare" }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                p.StandardOutput.ReadToEndAsync();
                var errText = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000 + 15000 * xmls.Count)) { try { p.Kill(); } catch { } throw new Exception("it took too long"); }
                var results = Enumerable.Range(0, xmls.Count).Select(_ => new Dictionary<string, string>()).ToList();
                var top = new Dictionary<string, string>();
                if (File.Exists(outFile))
                    foreach (string line in File.ReadAllLines(outFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                        int dot = key.IndexOf('.');
                        if (key.StartsWith("v") && dot > 1 && int.TryParse(key.Substring(1, dot - 1), out int vi) && vi >= 1 && vi <= results.Count)
                            results[vi - 1][key.Substring(dot + 1)] = value;
                        else top[key] = value;
                    }
                if (top.TryGetValue("error", out string err)) throw new Exception(err.Replace("^1", "").Replace("^0", "").Replace("^8", "").Trim());
                string bad = results.Select(r => r.TryGetValue("error", out string e) ? e : null).FirstOrDefault(e => e != null);
                if (!top.ContainsKey("ok") || bad != null || !results[0].ContainsKey("ok"))
                {
                    string err2 = bad ?? "";
                    try { if (err2.Length == 0 && errText.Wait(2000)) err2 = errText.Result.Trim(); } catch { }
                    if (err2.Length > 300) err2 = err2.Substring(0, 300);
                    throw new Exception("no result, exit code " + p.ExitCode + (err2.Length > 0 ? ": " + err2 : ""));
                }
                PluginLog.Write("Path of Building compare: " + string.Join(" || ", results.Select(r => string.Join(", ", r.Where(kv => kv.Key is "CombinedDPS" or "FullDPS" or "Life" or "TotalEHP").Select(kv => kv.Key + "=" + kv.Value)))));
                _pobSay = words(results);
            }
            catch (Exception ex)
            {
                PluginLog.Write("Path of Building compare failed: " + ex.Message);
                _pobSay = T($"Path of Building could not calculate: {ex.Message}", $"Path of Building no pudo calcular: {ex.Message}");
            }
            finally { _pobRunning = false; }
        });
    }

    private static string SignedNum(double d) => d >= 0 ? "+" + Whole(d) : T($"minus {Whole(-d)}", $"menos {Whole(-d)}");

    /// <summary>What changes from one set of numbers to another, the biggest things first.</summary>
    private static string DeltaWords(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var parts = new List<string>();
        double da = Dps(a), db = Dps(b);
        if (da > 0 && Math.Abs(db - da) / da >= 0.005) parts.Add(T($"damage per second {SignedNum((db - da) / da * 100)}%", $"daño por segundo {SignedNum((db - da) / da * 100)}%"));
        else if (da <= 0 && db > 0) parts.Add(T($"damage per second from 0 to {Whole(db)}", $"daño por segundo de 0 a {Whole(db)}"));
        double ea = Num(a, "TotalEHP"), eb = Num(b, "TotalEHP");
        if (ea > 0 && Math.Abs(eb - ea) / ea >= 0.005) parts.Add(T($"effective health {SignedNum((eb - ea) / ea * 100)}%", $"vida efectiva {SignedNum((eb - ea) / ea * 100)}%"));
        foreach (var (k, en, es) in new[] { ("Life", "life", "vida"), ("EnergyShield", "energy shield", "escudo de energía"), ("Mana", "mana", "maná"),
                                             ("FireResist", "fire resistance", "resistencia al fuego"), ("ColdResist", "cold resistance", "resistencia al frío"),
                                             ("LightningResist", "lightning resistance", "resistencia al rayo"), ("ChaosResist", "chaos resistance", "resistencia al caos"),
                                             ("Armour", "armour", "armadura"), ("Evasion", "evasion", "evasión"), ("BlockChance", "block", "bloqueo"),
                                             ("SpellBlockChance", "spell block", "bloqueo de hechizos") })
        {
            double d = Num(b, k) - Num(a, k);
            if (Math.Abs(d) >= 1) parts.Add(T($"{en} {SignedNum(d)}", $"{es} {SignedNum(d)}"));
        }
        double ms = (Num(b, "MovementSpeedMod") - Num(a, "MovementSpeedMod")) * 100;
        if (Math.Abs(ms) >= 1) parts.Add(T($"movement speed {SignedNum(ms)}%", $"velocidad de movimiento {SignedNum(ms)}%"));
        foreach (var (have, need, en, es) in new[] { ("Str", "ReqStr", "strength", "fuerza"), ("Dex", "ReqDex", "dexterity", "destreza"), ("Int", "ReqInt", "intelligence", "inteligencia") })
            if (Num(b, need) > Num(b, have) && !(Num(a, need) > Num(a, have))) parts.Add(T($"not enough {en}: {Whole(Num(b, need))} needed", $"falta {es}: se necesitan {Whole(Num(b, need))}"));
        return parts.Count > 0 ? string.Join(", ", parts) : T("no change to the main numbers", "sin cambios en los números principales");
    }

    // ---- An item: what wearing it would change ----

    private static readonly string[] OneHandClasses = { "Claw", "Dagger", "Rune Dagger", "Wand", "One Hand Sword", "Thrusting One Hand Sword", "One Hand Axe", "One Hand Mace", "Sceptre" };
    private static readonly string[] TwoHandClasses = { "Bow", "Staff", "Warstaff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace", "FishingRod" };

    private void WhatIfItem(Entity item)
    {
        var baseModel = CharacterModel();
        if (baseModel == null) { Say(T("Enter the game with your character first.", "Entra antes en el juego con tu personaje."), true); return; }
        var (place, _, _, _) = Locate(item);
        if (place == ItemPlace.Equipped) { Say(T("You are wearing this. Triple tap another item to compare it with this one.", "Llevas esto puesto. Toca L3 tres veces sobre otro objeto para compararlo con este."), true); return; }
        string cls = Try(() => GameController.Files.BaseItemTypes.Translate(item.Path)?.ClassName, "") ?? "";
        string text = Try(() => ItemPoBText(item), null);
        if (text == null) { Say(T("This item cannot be described to Path of Building.", "Este objeto no se puede describir a Path of Building."), true); return; }
        var slots = cls == "Ring" ? new[] { "Ring 1", "Ring 2" }
            : cls is "Amulet" or "Belt" or "Helmet" or "Gloves" or "Boots" or "Body Armour" ? new[] { cls }
            : cls is "Shield" or "Quiver" ? new[] { "Weapon 2" }
            : OneHandClasses.Contains(cls) || TwoHandClasses.Contains(cls) ? new[] { "Weapon 1" }
            : null;
        if (slots == null) { Say(T("Path of Building compares only gear you can wear: not flasks or jewels yet.", "Path of Building solo compara equipo que se lleva puesto: aún no frascos ni joyas."), true); return; }
        var xmls = new List<string> { ModelXml(baseModel) };
        foreach (string slot in slots)
        {
            var m = baseModel.Copy();
            m.Items[slot] = text;
            // A two-handed weapon leaves no room in the off hand, but for a bow's quiver.
            if (TwoHandClasses.Contains(cls) && !(cls == "Bow" && Try(() => m.Items.TryGetValue("Weapon 2", out string off) && off.Contains("Quiver"), false))) m.Items.Remove("Weapon 2");
            xmls.Add(ModelXml(m));
        }
        string name = ItemTitle(item);
        RunPoBCompare(xmls, r =>
        {
            var lines = new List<string>();
            for (int i = 0; i < slots.Length; i++)
            {
                string where = slots.Length > 1 ? T($"In {slots[i].ToLowerInvariant()}: ", $"En {slots[i].ToLowerInvariant()}: ") : "";
                lines.Add(where + DeltaWords(r[0], r[i + 1]));
            }
            return T($"Wearing {name}, with your gems moved to it: ", $"Llevando {name}, con tus gemas en él: ") + string.Join(". ", lines);
        });
    }

    // ---- A passive: what taking or refunding it would change ----

    private void WhatIfNode(long row)
    {
        var baseModel = CharacterModel();
        if (baseModel == null) { Say(T("Enter the game with your character first.", "Entra antes en el juego con tu personaje."), true); return; }
        if (!_passiveIdOfRow.TryGetValue(row, out ushort pid)) return;
        string name = NodeName(row);
        var m = baseModel.Copy();
        string head;
        if (baseModel.Nodes.Contains(pid))
        {
            if (pid == ClassStartPassive()) { Say(T("Your class's start cannot be refunded.", "El inicio de tu clase no se puede devolver."), true); return; }
            m.Nodes.Remove(pid);
            head = T($"Refunding {name}: ", $"Devolviendo {name}: ");
        }
        else
        {
            var path = PlanPathTo(row, baseModel.Nodes);
            if (path == null) { Say(T($"No way found to {name}.", $"No hay camino hasta {name}."), true); return; }
            foreach (long r in path) if (_passiveIdOfRow.TryGetValue(r, out ushort id)) m.Nodes.Add(id);
            m.Nodes.Add(pid);
            int points = path.Count + 1;
            head = points == 1 ? T($"Taking {name}: ", $"Tomando {name}: ") : T($"Taking {name}, {points} points: ", $"Tomando {name}, {points} puntos: ");
        }
        RunPoBCompare(new List<string> { ModelXml(baseModel), ModelXml(m) }, r => head + DeltaWords(r[0], r[1]));
    }

    /// <summary>The nodes to take before `target`, nearest first, from the nodes in `from` along
    /// the tree's links; null without a way.</summary>
    private List<long> PlanPathTo(long target, HashSet<ushort> from)
    {
        LearnPassiveRows();
        var links = PassiveLinks(_clock.ElapsedMilliseconds);
        if (links == null) return null;
        var parent = new Dictionary<long, long>();
        var queue = new Queue<long>();
        foreach (var kv in _passiveIdOfRow)
            if (from.Contains(kv.Value) && links.ContainsKey(kv.Key)) { parent[kv.Key] = 0; queue.Enqueue(kv.Key); }
        while (queue.Count > 0)
        {
            long cur = queue.Dequeue();
            if (cur == target)
            {
                var path = new List<long>();
                for (long x = parent[cur]; x != 0 && !(_passiveIdOfRow.TryGetValue(x, out ushort xid) && from.Contains(xid)); x = parent[x]) path.Add(x);
                path.Reverse();
                return path;
            }
            if (!links.TryGetValue(cur, out var near)) continue;
            foreach (long n in near)
                if (!parent.ContainsKey(n)) { parent[n] = cur; queue.Enqueue(n); }
        }
        return null;
    }

    // ---- The plan: nodes marked without spending points ----

    private HashSet<ushort> _plan;
    private string _planOwner;

    private string PlanFile(string who) => AppFile(Path.Combine("plans", string.Concat(who.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".txt"));

    private HashSet<ushort> Plan()
    {
        string who = TutorialCharacter();
        if (_plan != null && who == _planOwner) return _plan;
        _planOwner = who;
        _plan = new HashSet<ushort>();
        try
        {
            string f = PlanFile(who);
            if (who.Length > 0 && File.Exists(f))
                foreach (string l in File.ReadAllLines(f)) if (ushort.TryParse(l.Trim(), out ushort id)) _plan.Add(id);
        }
        catch { }
        return _plan;
    }

    private void SavePlan()
    {
        if (string.IsNullOrEmpty(_planOwner)) return;
        try
        {
            string f = PlanFile(_planOwner);
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllLines(f, _plan.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        }
        catch { }
    }

    /// <summary>L3 and right on a node: plan it with the nodes on the way, or take it out.</summary>
    private void TogglePlanned()
    {
        long row = _passiveHoverRow;
        if (row == 0 || !_passiveIdOfRow.TryGetValue(row, out ushort pid)) { Say(T("Move onto a passive node first", "Ponte primero sobre un nodo pasivo"), true); return; }
        var plan = Plan();
        var have = AllocatedPassives();
        string name = NodeName(row);
        if (have.Contains(pid)) { Say(T($"You have {name} already.", $"Ya tienes {name}."), true); return; }
        if (plan.Remove(pid)) { SavePlan(); Say(T($"{name} out of your plan, {PlanLeft()} planned", $"{name} fuera de tu plan, {PlanLeft()} planeadas"), true); return; }
        var from = new HashSet<ushort>(have);
        from.UnionWith(plan);
        var path = PlanPathTo(row, from);
        if (path == null) { Say(T($"No way found to {name}.", $"No hay camino hasta {name}."), true); return; }
        foreach (long r in path) if (_passiveIdOfRow.TryGetValue(r, out ushort id)) plan.Add(id);
        plan.Add(pid);
        SavePlan();
        string way = path.Count > 0 ? T($" with {path.Count} on the way", $" con {path.Count} de camino") : "";
        Say(T($"{name} planned{way}, {PlanLeft()} planned", $"{name} planeada{way}, {PlanLeft()} planeadas"), true);
    }

    private int PlanLeft()
    {
        var have = AllocatedPassives();
        return Plan().Count(id => !have.Contains(id));
    }

    /// <summary>", planned" for a node of the plan you do not have yet, else "".</summary>
    private string PlanNote(ushort pid, bool allocated) =>
        !allocated && _plan != null && Plan().Contains(pid) ? T("planned", "planeada") : "";

    private CharModel PlannedModel(CharModel baseModel)
    {
        var m = baseModel.Copy();
        m.Nodes.UnionWith(Plan());
        return m;
    }

    private void SayPlanNumbers()
    {
        var baseModel = CharacterModel();
        if (baseModel == null) { Say(T("Enter the game with your character first.", "Entra antes en el juego con tu personaje."), true); return; }
        int left = PlanLeft();
        if (left == 0) { Say(T("Your plan is empty. On the passive tree, hold L3 and push the right stick right on a node to plan it.", "Tu plan está vacío. En el árbol de pasivas, mantén L3 y empuja el stick derecho a la derecha sobre un nodo para planearlo."), true); return; }
        RunPoBCompare(new List<string> { ModelXml(baseModel), ModelXml(PlannedModel(baseModel)) },
            r => T($"Your plan, {left} passives: ", $"Tu plan, {left} pasivas: ") + DeltaWords(r[0], r[1]) + ". " + PoBWords(r[1], T("With the plan", "Con el plan")));
    }

    private void FollowPlan()
    {
        var baseModel = CharacterModel();
        if (baseModel == null) { Say(T("Enter the game with your character first.", "Entra antes en el juego con tu personaje."), true); return; }
        if (PlanLeft() == 0) { Say(T("Your plan is empty.", "Tu plan está vacío."), true); return; }
        string xml = ModelXml(PlannedModel(baseModel)).Replace("treeVersion=\"LATEST\"", "");
        _buildPendingCode = BuildPlan.Encode(xml);
        Say(T("Your plan becomes the build to follow.", "Tu plan pasa a ser la build a seguir."), true);
    }

    private void ClearPlan()
    {
        Plan().Clear();
        SavePlan();
        Say(T("Plan cleared.", "Plan borrado."), true);
    }

    private void CopyCode(bool withPlan)
    {
        var baseModel = CharacterModel();
        if (baseModel == null) { Say(T("Enter the game with your character first.", "Entra antes en el juego con tu personaje."), true); return; }
        var m = withPlan ? PlannedModel(baseModel) : baseModel;
        string code = BuildPlan.Encode(ModelXml(m).Replace("treeVersion=\"LATEST\"", ""));
        Say(SetClipboardText(code)
            ? T("Build code copied. Paste it in Path of Building with Import, or send it to a friend.", "Código de build copiado. Pégalo en Path of Building con Importar, o envíalo a un amigo.")
            : T("The build code could not be copied.", "No se pudo copiar el código de build."), true);
    }

    // ---- Skills: which group is calculated, and support gems to try ----

    private string MainSkillName(CharModel m, int group)
    {
        if (group < 1 || group > m.Groups.Count) return "";
        var g = m.Groups[group - 1];
        return g.Gems.FirstOrDefault(x => !x.Support)?.Name ?? g.Gems.First().Name;
    }

    private void CycleMainSkill()
    {
        var m = CharacterModel();
        if (m == null || m.Groups.Count == 0) { Say(T("No socketed skills found. Enter the game with your character first.", "No hay habilidades engarzadas. Entra antes en el juego con tu personaje."), true); return; }
        var choices = Enumerable.Range(1, m.Groups.Count).Where(i => m.Groups[i - 1].Gems.Any(x => !x.Support)).ToList();
        int at = choices.IndexOf(_pobMainChoice);
        _pobMainChoice = at < 0 ? choices.FirstOrDefault() : at + 1 < choices.Count ? choices[at + 1] : 0;
        Say(DescribeMainSkill(), true);
    }

    private string DescribeMainSkill()
    {
        var m = CharacterModel();
        if (m == null) return T("Skill Path of Building calculates: enter the game first", "Habilidad que calcula Path of Building: entra antes en el juego");
        string name = MainSkillName(m, m.MainGroup);
        return _pobMainChoice <= 0
            ? T($"Skill Path of Building calculates: by itself, now {name}", $"Habilidad que calcula Path of Building: automática, ahora {name}")
            : T($"Skill Path of Building calculates: {name}", $"Habilidad que calcula Path of Building: {name}");
    }

    private List<string> _supportGems;

    /// <summary>Every support gem in the game, by the name Path of Building knows it by.</summary>
    private List<string> SupportGems()
    {
        if (_supportGems != null && _supportGems.Count > 0) return _supportGems;
        _supportGems = Try(() => GameController.Files.BaseItemTypes.Contents.Values
            .Where(b => b != null && (b.ClassName ?? "").IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0 && (b.ClassName ?? "").IndexOf("Gem", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(b => (b.BaseName ?? "").Trim())
            .Where(n => n.Length > 0 && !n.StartsWith("[", StringComparison.Ordinal))
            .Select(n => n.EndsWith(" Support", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 8) : n)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), new List<string>());
        return _supportGems;
    }

    private void TrySupport(string support)
    {
        var baseModel = CharacterModel();
        if (baseModel == null || baseModel.Groups.Count == 0) { Say(T("No socketed skills found.", "No hay habilidades engarzadas."), true); return; }
        int g = baseModel.MainGroup;
        if (g < 1 || g > baseModel.Groups.Count) return;
        var gems = baseModel.Groups[g - 1].Gems;
        string skill = MainSkillName(baseModel, g);
        int level = Math.Max(1, Math.Min(20, gems.Where(x => !x.Support).Select(x => x.Level).DefaultIfEmpty(1).Max()));
        var gem = new PoBGem { Name = support, Level = level, Support = true };
        var xmls = new List<string> { ModelXml(baseModel) };
        var labels = new List<string> { T($"Adding {support} to {skill}", $"Añadiendo {support} a {skill}") };
        var added = baseModel.Copy();
        added.Groups[g - 1].Gems.Add(gem.Copy());
        xmls.Add(ModelXml(added));
        for (int i = 0; i < gems.Count; i++)
        {
            if (!gems[i].Support) continue;
            var swap = baseModel.Copy();
            swap.Groups[g - 1].Gems[i] = gem.Copy();
            xmls.Add(ModelXml(swap));
            labels.Add(T($"Instead of {gems[i].Name}", $"En lugar de {gems[i].Name}"));
        }
        RunPoBCompare(xmls, r => string.Join(". ", labels.Select((l, i) => l + ": " + DeltaWords(r[0], r[i + 1]))) +
                                 T($". At gem level {level}.", $". Con la gema a nivel {level}."));
    }

    // ---- The menu ----

    private MenuItem CraftMenu()
    {
        MenuItem A(string label, string hint, Action act, Func<string> describe = null) =>
            new MenuItem { Label = label, Text = hint, StartPractice = act, Describe = describe ?? (() => label) };
        string right = T("Move right. It takes a few seconds.", "Muévete a la derecha. Tarda unos segundos.");
        var supports = new MenuItem
        {
            Label = T("Try a support gem on the calculated skill", "Probar una gema de apoyo en la habilidad calculada"),
            Children = new List<MenuItem>(),
        };
        FillSupportMenu(supports);
        return new MenuItem
        {
            Label = T("Build crafting (Path of Building)", "Crear tu build (Path of Building)"),
            Children = new List<MenuItem>
            {
                A(T("How to compare and plan", "Cómo comparar y planear"), T("Move right to hear it.", "Muévete a la derecha para oírlo."), () => Say(T(
                    "Triple tap L3 on any item, in your bag, the stash or a merchant's stock, to hear what wearing it would change. Triple tap L3 on a passive node to hear what taking it, or refunding it, would change. On the passive tree, hold L3 and push the right stick right on a node to plan it, with the nodes on the way, or to take it out of your plan; nothing is allocated. Then use this menu for your plan's numbers, to follow it as your build, or to copy it as a build code. Every answer comes from Path of Building and takes a few seconds.",
                    "Toca L3 tres veces sobre cualquier objeto, en tu bolsa, el alijo o la tienda de un mercader, para oír qué cambiaría llevarlo. Toca L3 tres veces sobre un nodo pasivo para oír qué cambiaría tomarlo o devolverlo. En el árbol de pasivas, mantén L3 y empuja el stick derecho a la derecha sobre un nodo para planearlo, con los nodos del camino, o para sacarlo de tu plan; no se asigna nada. Después usa este menú para los números de tu plan, para seguirlo como tu build o para copiarlo como código. Cada respuesta viene de Path of Building y tarda unos segundos."), true)),
                A(T("Your plan's numbers against yours", "Los números de tu plan frente a los tuyos"), right, SayPlanNumbers),
                A(T("Follow your plan as your build", "Seguir tu plan como tu build"), T("Move right.", "Muévete a la derecha."), FollowPlan),
                A(T("Copy your plan as a build code", "Copiar tu plan como código de build"), T("Move right.", "Muévete a la derecha."), () => CopyCode(true)),
                A(T("Copy your character as a build code", "Copiar tu personaje como código de build"), T("Move right.", "Muévete a la derecha."), () => CopyCode(false)),
                A(T("Clear your plan", "Borrar tu plan"), T("Move right.", "Muévete a la derecha."), ClearPlan),
                A(T("Skill Path of Building calculates", "Habilidad que calcula Path of Building"), T("Move right for the next of your skills.", "Muévete a la derecha para la siguiente de tus habilidades."), CycleMainSkill, DescribeMainSkill),
                // The enemy your character is calculated against (PoBCalc.cs).
                new MenuItem { Label = T("Enemy Path of Building calculates against", "Enemigo contra el que calcula Path of Building"), Range = Settings.PoBEnemyType, Step = 1, Words = EnemyTypeWords },
                new MenuItem { Label = T("Enemy level", "Nivel del enemigo"), Range = Settings.PoBEnemyLevel, Step = 1, Words = EnemyLevelWords },
                supports,
            },
        };
    }

    /// <summary>The support gem list, filled the first time the menu reaches it (the game's files
    /// are read then).</summary>
    private void FillSupportMenu(MenuItem supports)
    {
        if (supports.Children.Count > 0) return;
        foreach (string name in SupportGems())
        {
            string n = name;
            supports.Children.Add(new MenuItem { Label = n, Text = T("Move right to try it.", "Muévete a la derecha para probarla."), StartPractice = () => TrySupport(n), Describe = () => n });
        }
        if (supports.Children.Count == 0)
            supports.Children.Add(new MenuItem { Label = T("No support gems read yet", "Aún no se han leído gemas de apoyo"), Text = T("Enter the game first.", "Entra antes en el juego."), StartPractice = () => { }, Describe = () => T("No support gems read yet", "Aún no se han leído gemas de apoyo") });
    }

    // ---- The clipboard, written ----

    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    private static bool SetClipboardText(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { System.Threading.Thread.Sleep(20); continue; }
            try
            {
                EmptyClipboard();
                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                IntPtr h = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);   // GMEM_MOVEABLE
                if (h == IntPtr.Zero) return false;
                IntPtr p = GlobalLock(h);
                Marshal.Copy(bytes, 0, p, bytes.Length);
                GlobalUnlock(h);
                return SetClipboardData(13, h) != IntPtr.Zero;   // CF_UNICODETEXT; the clipboard owns it now
            }
            finally { CloseClipboard(); }
        }
        return false;
    }
}
