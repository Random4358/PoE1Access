using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using static PoE1Access.Loc;
using BaseComp = ExileCore.PoEMemory.Components.Base;
using GemComp = ExileCore.PoEMemory.Components.SkillGem;
using ModsComp = ExileCore.PoEMemory.Components.Mods;
using QualityComp = ExileCore.PoEMemory.Components.Quality;
using SocketsComp = ExileCore.PoEMemory.Components.Sockets;

namespace PoE1Access;

// Path of Building's own numbers, said: the followed build's (from its build code) or your
// character's (described to Path of Building from the game: class, level, passives, worn gear
// with its modifiers, and socketed gems). Path of Building Community must be installed; its
// calculations run without its window in app\pobcalc\PoBCalc.exe, a few seconds, while you play.
// Your character's numbers are close, not exact: jewels, mastery choices, the pantheon and the
// configuration (such as charges or the enemy) are not given.
public partial class PoE1AccessCore
{
    private volatile string _pobSay;
    private volatile bool _pobRunning;
    private Dictionary<string, string> _pobBuildResult;
    private string _buildXml;

    // Where Path of Building Community is installed: where its installer recorded it in the
    // registry (its uninstall entry), else the usual folders. Not found inside Exile API on
    // 28 Nov 2026 although installed in AppData\Roaming, so every place is tried and, when none
    // holds it, each is written to the log.
    private static string PoBFolder()
    {
        var tried = new List<string>();
        foreach (string d in PoBFolderCandidates())
        {
            if (string.IsNullOrWhiteSpace(d) || tried.Contains(d)) continue;
            tried.Add(d);
            try { if (File.Exists(Path.Combine(d, "Launch.lua")) && File.Exists(Path.Combine(d, "lua51.dll"))) return d; } catch { }
        }
        PluginLog.Write("Path of Building not found in: " + string.Join(" | ", tried));
        return null;
    }

    private static IEnumerable<string> PoBFolderCandidates()
    {
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            string loc = null;
            try
            {
                using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Path of Building Community");
                loc = (key?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                if (string.IsNullOrEmpty(loc)) loc = Path.GetDirectoryName((key?.GetValue("UninstallString") as string)?.Trim().Trim('"') ?? "");
            }
            catch { }
            if (!string.IsNullOrEmpty(loc)) yield return loc;
        }
        string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
        string appData = Safe(() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        string envAppData = Safe(() => Environment.GetEnvironmentVariable("APPDATA"));
        string profile = Safe(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (appData.Length > 0) yield return Path.Combine(appData, "Path of Building Community");
        if (envAppData.Length > 0) yield return Path.Combine(envAppData, "Path of Building Community");
        if (profile.Length > 0) yield return Path.Combine(profile, "AppData", "Roaming", "Path of Building Community");
        string local = Safe(() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (local.Length > 0) yield return Path.Combine(local, "Programs", "Path of Building Community");
        string pf = Safe(() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        if (pf.Length > 0) yield return Path.Combine(pf, "Path of Building Community");
    }

    private void CalculateBuildWithPoB()
    {
        if (_build == null || _buildXml == null) { Say(NoBuildText(), true); return; }
        // The tree and skill set followed, not only the ones the build was saved on.
        string xml = _buildXml;
        try
        {
            var doc = XDocument.Parse(_buildXml);
            var tree = doc.Root?.Element("Tree");
            int spec = FollowedSpec();
            if (tree != null && spec >= 0) tree.SetAttributeValue("activeSpec", spec + 1);
            var skills = doc.Root?.Element("Skills");
            var sets = skills?.Elements("SkillSet").ToList();
            if (sets != null && _buildSkillSet < sets.Count) skills.SetAttributeValue("activeSkillSet", (string)sets[_buildSkillSet].Attribute("id") ?? "1");
            xml = doc.ToString();
        }
        catch { }
        RunPoB(xml, "build", T($"The build, {SpecName(FollowedSpec())}", $"La build, {SpecName(FollowedSpec())}"));
    }

    private void CalculateCharacterWithPoB()
    {
        string xml = Try(CharacterXml, null);
        if (xml == null) { Say(T("Enter the game with your character first.", "Entra antes en el juego con tu personaje."), true); return; }
        RunPoB(xml, "character", T("Your character", "Tu personaje"));
    }

    private void RunPoB(string xml, string mode, string who)
    {
        string pob = PoBFolder();
        if (pob == null) { Say(T("Path of Building Community is not installed. Install it from its GitHub page, then try again.", "Path of Building Community no está instalado. Instálalo desde su página de GitHub y vuelve a intentarlo."), true); return; }
        string exe = AppFile(Path.Combine("pobcalc", "PoBCalc.exe"));
        string driver = AppFile(Path.Combine("pobcalc", "driver.lua"));
        if (!File.Exists(exe) || !File.Exists(driver)) { Say(T("The mod's Path of Building helper is missing. Reinstall the mod.", "Falta el ayudante de Path of Building del mod. Reinstala el mod."), true); return; }
        if (_pobRunning) { Say(T("Path of Building is still calculating.", "Path of Building aún está calculando."), true); return; }
        _pobRunning = true;
        Say(T("Calculating with Path of Building.", "Calculando con Path of Building."), true);
        PluginLog.Write("Path of Building: calculating " + mode);
        Task.Run(() =>
        {
            string xmlFile = Path.Combine(Path.GetTempPath(), "poe1access-pob-" + mode + ".xml");
            string outFile = Path.Combine(Path.GetTempPath(), "poe1access-pob-" + mode + ".txt");
            try
            {
                File.WriteAllText(xmlFile, xml, new UTF8Encoding(false));
                File.Delete(outFile);
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (string a in new[] { pob, driver, xmlFile, outFile, mode }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(90000)) { try { p.Kill(); } catch { } throw new Exception("it took too long"); }
                var result = new Dictionary<string, string>();
                if (File.Exists(outFile))
                    foreach (string line in File.ReadAllLines(outFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) result[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                if (result.TryGetValue("error", out string err)) throw new Exception(err.Replace("^1", "").Replace("^0", "").Replace("^8", "").Trim());
                if (!result.ContainsKey("ok")) throw new Exception("no result");
                PluginLog.Write("Path of Building " + mode + ": " + string.Join(", ", result.Select(kv => kv.Key + "=" + kv.Value)));
                string text = PoBWords(result, who);
                if (mode == "build") _pobBuildResult = result;
                else if (_pobBuildResult != null) text += " " + PoBCompare(result, _pobBuildResult);
                _pobSay = text;
            }
            catch (Exception ex)
            {
                PluginLog.Write("Path of Building failed: " + ex.Message);
                _pobSay = T($"Path of Building could not calculate: {ex.Message}", $"Path of Building no pudo calcular: {ex.Message}");
            }
            finally
            {
                _pobRunning = false;
                try { File.Delete(xmlFile); } catch { }
            }
        });
    }

    /// <summary>Said from the tick once Path of Building has finished.</summary>
    private void PollPoB()
    {
        string t = _pobSay;
        if (t == null) return;
        _pobSay = null;
        Say(t, true);
    }

    private static double Num(Dictionary<string, string> r, string k) =>
        r.TryGetValue(k, out string v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

    private static string Whole(double d) => Math.Round(d).ToString("N0", CultureInfo.CurrentCulture);

    private static string Signed(double d) => d < 0 ? T($"minus {Whole(-d)}", $"menos {Whole(-d)}") : Whole(d);

    private static double Dps(Dictionary<string, string> r)
    {
        double full = Num(r, "FullDPS");
        return full > 0 ? full : Num(r, "CombinedDPS");
    }

    private static string PoBWords(Dictionary<string, string> r, string who)
    {
        var parts = new List<string>();
        string head = who;
        if (r.TryGetValue("Ascendancy", out string asc)) head += ", " + asc;
        parts.Add(T($"Path of Building. {head}", $"Path of Building. {head}"));
        string skill = r.TryGetValue("MainSkill", out string s) ? s : "";
        double dps = Dps(r);
        if (skill.Length > 0) parts.Add(T($"{skill}: {Whole(dps)} damage per second", $"{skill}: {Whole(dps)} de daño por segundo"));
        parts.Add(T($"Life {Whole(Num(r, "Life"))}, energy shield {Whole(Num(r, "EnergyShield"))}, mana {Whole(Num(r, "Mana"))}",
                    $"Vida {Whole(Num(r, "Life"))}, escudo de energía {Whole(Num(r, "EnergyShield"))}, maná {Whole(Num(r, "Mana"))}"));
        parts.Add(T($"Resistances: fire {Signed(Num(r, "FireResist"))}, cold {Signed(Num(r, "ColdResist"))}, lightning {Signed(Num(r, "LightningResist"))}, chaos {Signed(Num(r, "ChaosResist"))}",
                    $"Resistencias: fuego {Signed(Num(r, "FireResist"))}, frío {Signed(Num(r, "ColdResist"))}, rayo {Signed(Num(r, "LightningResist"))}, caos {Signed(Num(r, "ChaosResist"))}"));
        parts.Add(T($"Armour {Whole(Num(r, "Armour"))}, evasion {Whole(Num(r, "Evasion"))}", $"Armadura {Whole(Num(r, "Armour"))}, evasión {Whole(Num(r, "Evasion"))}"));
        double block = Num(r, "BlockChance"), spellBlock = Num(r, "SpellBlockChance");
        if (block > 0 || spellBlock > 0) parts.Add(T($"Block {Whole(block)} percent, spell block {Whole(spellBlock)}", $"Bloqueo {Whole(block)} por ciento, bloqueo de hechizos {Whole(spellBlock)}"));
        parts.Add(T($"Effective health {Whole(Num(r, "TotalEHP"))}. Biggest hit you survive: physical {Whole(Num(r, "PhysicalMaximumHitTaken"))}, fire {Whole(Num(r, "FireMaximumHitTaken"))}, cold {Whole(Num(r, "ColdMaximumHitTaken"))}, lightning {Whole(Num(r, "LightningMaximumHitTaken"))}, chaos {Whole(Num(r, "ChaosMaximumHitTaken"))}",
                    $"Vida efectiva {Whole(Num(r, "TotalEHP"))}. Mayor golpe que aguantas: físico {Whole(Num(r, "PhysicalMaximumHitTaken"))}, fuego {Whole(Num(r, "FireMaximumHitTaken"))}, frío {Whole(Num(r, "ColdMaximumHitTaken"))}, rayo {Whole(Num(r, "LightningMaximumHitTaken"))}, caos {Whole(Num(r, "ChaosMaximumHitTaken"))}"));
        parts.Add(T($"Strength {Whole(Num(r, "Str"))}, dexterity {Whole(Num(r, "Dex"))}, intelligence {Whole(Num(r, "Int"))}", $"Fuerza {Whole(Num(r, "Str"))}, destreza {Whole(Num(r, "Dex"))}, inteligencia {Whole(Num(r, "Int"))}"));
        foreach (var (have, need, en, es) in new[] { ("Str", "ReqStr", "strength", "fuerza"), ("Dex", "ReqDex", "dexterity", "destreza"), ("Int", "ReqInt", "intelligence", "inteligencia") })
            if (Num(r, need) > Num(r, have)) parts.Add(T($"Not enough {en}: {Whole(Num(r, need))} needed", $"Falta {es}: se necesitan {Whole(Num(r, need))}"));
        return JoinSpeech(parts);
    }

    private static string PoBCompare(Dictionary<string, string> you, Dictionary<string, string> build) =>
        T($"The build has {Whole(Dps(build))} damage per second, life {Whole(Num(build, "Life"))}, energy shield {Whole(Num(build, "EnergyShield"))}, effective health {Whole(Num(build, "TotalEHP"))}.",
          $"La build tiene {Whole(Dps(build))} de daño por segundo, vida {Whole(Num(build, "Life"))}, escudo de energía {Whole(Num(build, "EnergyShield"))}, vida efectiva {Whole(Num(build, "TotalEHP"))}.");

    // ---- Your character, as a Path of Building file ----

    private static readonly Dictionary<string, string> PoBSlotOf = new Dictionary<string, string>
    {
        ["Main hand"] = "Weapon 1", ["Off hand"] = "Weapon 2", ["Helmet"] = "Helmet", ["Body armour"] = "Body Armour",
        ["Gloves"] = "Gloves", ["Boots"] = "Boots", ["Belt"] = "Belt", ["Amulet"] = "Amulet", ["Ring 1"] = "Ring 1",
        ["Ring 2"] = "Ring 2", ["Swap main hand"] = "Weapon 1 Swap", ["Swap off hand"] = "Weapon 2 Swap",
    };

    private string CharacterXml()
    {
        ServerData server = GetServerData();
        var player = GameController.Player;
        if (server == null || player == null) return null;
        int level = Try(() => GetComp<ExileCore.PoEMemory.Components.Player>(player)?.Level ?? 0, 0);
        if (level <= 0) return null;
        string path = Try(() => player.Path, "") ?? "";
        int classId = Array.FindIndex(Classes, c => path.StartsWith(c.Id, StringComparison.OrdinalIgnoreCase));
        if (classId < 0) classId = 0;
        string nodes = string.Join(",", AllocatedPassives().Select(i => i.ToString(CultureInfo.InvariantCulture)));

        var items = new StringBuilder();
        var slots = new StringBuilder();
        var skills = new StringBuilder();
        int itemId = 0, group = 0, mainGroup = 1, mainSize = 0;
        foreach (var (slot, label) in EquipmentSlots)
            foreach (var s in Slots(server, slot))
            {
                Entity item = s.Item;
                string pobSlot = slot == InventorySlotE.Flask1 ? "Flask " + (s.PosX + 1) : PoBSlotOf.TryGetValue(label, out string ps) ? ps : null;
                if (pobSlot == null || item == null) continue;
                string text = Try(() => ItemPoBText(item), null);
                if (text == null) continue;
                itemId++;
                items.Append("<Item id=\"").Append(itemId).Append("\">").Append(SecurityElement.Escape(text)).Append("</Item>\n");
                slots.Append("<Slot name=\"").Append(pobSlot).Append("\" itemId=\"").Append(itemId).Append("\"/>\n");
                // Its gems, a skill group per linked group of sockets.
                var sockets = GetComp<SocketsComp>(item);
                var groups = Try(() => sockets?.SocketGroup, null);
                if (groups == null) continue;
                var gemAt = new Dictionary<int, (string Name, int Level, int Quality, bool Support)>();
                var socketed = Try(() => sockets.SocketedGems, null);
                if (socketed != null)
                    foreach (var g in socketed)
                    {
                        Entity gem = Try(() => g.GemEntity, null);
                        string name = gem == null ? "" : CleanText(Try(() => GetComp<BaseComp>(gem)?.Name, "") ?? "");
                        if (name.Length == 0) continue;
                        bool support = name.EndsWith(" Support", StringComparison.OrdinalIgnoreCase);
                        if (support) name = name.Substring(0, name.Length - 8);
                        gemAt[Try(() => g.SocketIndex, -1)] = (name, Try(() => GetComp<GemComp>(gem)?.Level ?? 1, 1), Try(() => GetComp<QualityComp>(gem)?.ItemQuality ?? 0, 0), support);
                    }
                int index = 0;
                foreach (string linked in groups)
                {
                    if (string.IsNullOrEmpty(linked)) continue;
                    var gems = new List<(string Name, int Level, int Quality, bool Support)>();
                    for (int k = 0; k < linked.Length; k++, index++) if (gemAt.TryGetValue(index, out var gm)) gems.Add(gm);
                    if (gems.Count == 0) continue;
                    group++;
                    // The main skill: the biggest group with a skill gem in it.
                    if (gems.Any(x => !x.Support) && gems.Count > mainSize) { mainSize = gems.Count; mainGroup = group; }
                    skills.Append("<Skill slot=\"").Append(pobSlot).Append("\" enabled=\"true\" mainActiveSkill=\"1\">");
                    foreach (var gm in gems.OrderBy(x => x.Support))
                        skills.Append("<Gem nameSpec=\"").Append(SecurityElement.Escape(gm.Name)).Append("\" level=\"").Append(gm.Level)
                              .Append("\" quality=\"").Append(gm.Quality).Append("\" enabled=\"true\"/>");
                    skills.Append("</Skill>\n");
                }
            }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<PathOfBuilding>\n" +
               $"<Build level=\"{level}\" className=\"{Classes[classId].English}\" ascendClassName=\"None\" mainSocketGroup=\"{mainGroup}\" targetVersion=\"3_0\" bandit=\"None\"/>\n" +
               $"<Tree activeSpec=\"1\"><Spec treeVersion=\"LATEST\" classId=\"{classId}\" ascendClassId=\"0\" nodes=\"{nodes}\"/></Tree>\n" +
               $"<Skills activeSkillSet=\"1\"><SkillSet id=\"1\">\n{skills}</SkillSet></Skills>\n" +
               $"<Items activeItemSet=\"1\">\n{items}<ItemSet id=\"1\">\n{slots}</ItemSet></Items>\n</PathOfBuilding>\n";
    }

    // An item as the game copies it, which Path of Building reads: rarity, name, base, item level,
    // quality, sockets, implicits and the other modifiers.
    private string ItemPoBText(Entity item)
    {
        var mods = GetComp<ModsComp>(item);
        string baseName = CleanText(Try(() => GetComp<BaseComp>(item)?.Name, "") ?? "");
        if (baseName.Length == 0) return null;
        var rarity = mods != null ? Try(() => mods.ItemRarity, ItemRarity.Normal) : ItemRarity.Normal;
        var sb = new StringBuilder();
        sb.Append("Rarity: ").Append(rarity switch { ItemRarity.Magic => "MAGIC", ItemRarity.Rare => "RARE", ItemRarity.Unique => "UNIQUE", _ => "NORMAL" }).Append('\n');
        string unique = mods != null ? CleanText(Try(() => mods.UniqueName, "") ?? "") : "";
        if ((rarity == ItemRarity.Rare || rarity == ItemRarity.Unique) && unique.Length > 0) sb.Append(unique).Append('\n');
        sb.Append(baseName).Append('\n');
        int ilvl = mods != null ? Try(() => mods.ItemLevel, 0) : 0;
        if (ilvl > 0) sb.Append("Item Level: ").Append(ilvl).Append('\n');
        int quality = Try(() => GetComp<QualityComp>(item)?.ItemQuality ?? 0, 0);
        if (quality > 0) sb.Append("Quality: +").Append(quality).Append("%\n");
        var groups = Try(() => GetComp<SocketsComp>(item)?.SocketGroup, null);
        if (groups != null && groups.Count > 0)
            sb.Append("Sockets: ").Append(string.Join(" ", groups.Where(g => !string.IsNullOrEmpty(g)).Select(g => string.Join("-", g.ToCharArray())))).Append('\n');
        var imp = mods != null ? Try(() => mods.HumanImpStats?.ToList(), null) ?? new List<string>() : new List<string>();
        var exp = mods != null && Try(() => mods.Identified, true) ? Try(() => mods.HumanStats?.ToList(), null) ?? new List<string>() : new List<string>();
        sb.Append("Implicits: ").Append(imp.Count).Append('\n');
        foreach (string l in imp) sb.Append(CleanText(l)).Append('\n');
        foreach (string l in exp) sb.Append(CleanText(l)).Append('\n');
        if (Try(() => GetComp<BaseComp>(item)?.isCorrupted ?? false, false)) sb.Append("Corrupted\n");
        return sb.ToString();
    }
}
