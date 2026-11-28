using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PoE1Access;

/// <summary>
/// A Path of Building build, unpacked from its build code: the text a "share" gives, which is
/// the build's XML file, zlib-compressed and written in URL-safe base64. What is kept is what
/// following the build in the game needs: the class and ascendancy, each passive tree of the
/// build (leveling guides have several, such as "Level 1-30"), each skill group with its gems,
/// the gear by slot, the bandit and the notes. Nothing here touches the game.
/// </summary>
internal sealed class BuildPlan
{
    public sealed class Gem
    {
        public string Name = "";
        public int Level, Quality;
        public bool Enabled = true, Support;
    }

    public sealed class SkillGroup
    {
        public string Label = "", Slot = "";
        public bool Enabled = true;
        public List<Gem> Gems = new List<Gem>();
        /// <summary>The group's name as said: its label, else its first active gem.</summary>
        public string Title => Label.Length > 0 ? Label
            : Gems.FirstOrDefault(g => !g.Support && g.Enabled)?.Name ?? Gems.FirstOrDefault()?.Name ?? "";
    }

    public sealed class Spec
    {
        public string Title = "";
        public List<ushort> Nodes = new List<ushort>();
    }

    public string ClassName = "", AscendancyName = "", Bandit = "", Notes = "";
    public int Level;
    public List<Spec> Specs = new List<Spec>();
    public int ActiveSpec;   // the tree the build was saved on
    public sealed class SkillSet
    {
        public string Title = "";
        public List<SkillGroup> Groups = new List<SkillGroup>();
    }

    public List<SkillSet> SkillSets = new List<SkillSet>();
    public int ActiveSkillSet;   // the skill set the build was saved on
    public List<(string Slot, string Item)> Gear = new List<(string, string)>();

    /// <summary>The XML inside a build code, or null when it is not one.</summary>
    public static string Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string s = new string(code.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
        while (s.Length % 4 != 0) s += "=";
        byte[] packed;
        try { packed = Convert.FromBase64String(s); } catch { return null; }
        try
        {
            using var input = new MemoryStream(packed);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch { return null; }
    }

    /// <summary>A build code made from build XML (for tests).</summary>
    public static string Encode(string xml)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, true)) z.Write(Encoding.UTF8.GetBytes(xml));
        return Convert.ToBase64String(output.ToArray()).Replace('+', '-').Replace('/', '_');
    }

    public static BuildPlan Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        if (root == null || root.Name.LocalName != "PathOfBuilding") return null;
        var plan = new BuildPlan();

        var build = root.Element("Build");
        if (build != null)
        {
            plan.ClassName = (string)build.Attribute("className") ?? "";
            plan.AscendancyName = (string)build.Attribute("ascendClassName") ?? "";
            if (plan.AscendancyName == "None") plan.AscendancyName = "";
            plan.Bandit = (string)build.Attribute("bandit") ?? "";
            int.TryParse((string)build.Attribute("level"), out plan.Level);
        }

        var tree = root.Element("Tree");
        if (tree != null)
        {
            foreach (var spec in tree.Elements("Spec"))
            {
                var sp = new Spec { Title = ((string)spec.Attribute("title") ?? "").Trim() };
                foreach (string n in ((string)spec.Attribute("nodes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    // Cluster jewel nodes are numbered above 65535; they are not on the game's tree.
                    if (int.TryParse(n.Trim(), out int id) && id > 0 && id <= ushort.MaxValue) sp.Nodes.Add((ushort)id);
                plan.Specs.Add(sp);
            }
            if (int.TryParse((string)tree.Attribute("activeSpec"), out int active)) plan.ActiveSpec = Math.Max(0, Math.Min(plan.Specs.Count - 1, active - 1));
        }

        var skills = root.Element("Skills");
        if (skills != null)
        {
            // Newer files keep skill sets ("Act 1", "Totem Time"); older ones list skills directly.
            var sets = skills.Elements("SkillSet").ToList();
            var sources = sets.Count > 0
                ? sets.Select(x => (Title: ((string)x.Attribute("title") ?? "").Trim(), Id: (string)x.Attribute("id") ?? "", Groups: x.Elements("Skill"))).ToList()
                : new List<(string Title, string Id, IEnumerable<XElement> Groups)> { ("", "1", skills.Elements("Skill")) };
            string activeId = (string)skills.Attribute("activeSkillSet") ?? "1";
            foreach (var src in sources)
            {
            var skillSet = new SkillSet { Title = src.Title };
            if (src.Id == activeId) plan.ActiveSkillSet = plan.SkillSets.Count;
            foreach (var g in src.Groups)
            {
                var group = new SkillGroup
                {
                    Label = ((string)g.Attribute("label") ?? "").Trim(),
                    Slot = ((string)g.Attribute("slot") ?? "").Trim(),
                    Enabled = !string.Equals((string)g.Attribute("enabled"), "false", StringComparison.OrdinalIgnoreCase),
                };
                foreach (var gem in g.Elements("Gem"))
                {
                    string name = ((string)gem.Attribute("nameSpec") ?? (string)gem.Attribute("skillId") ?? "").Trim();
                    if (name.Length == 0) continue;
                    string ids = ((string)gem.Attribute("gemId") ?? "") + " " + ((string)gem.Attribute("skillId") ?? "");
                    var x = new Gem
                    {
                        Name = name,
                        Enabled = !string.Equals((string)gem.Attribute("enabled"), "false", StringComparison.OrdinalIgnoreCase),
                        Support = ids.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0,
                    };
                    int.TryParse((string)gem.Attribute("level"), out x.Level);
                    int.TryParse((string)gem.Attribute("quality"), out x.Quality);
                    group.Gems.Add(x);
                }
                if (group.Gems.Count > 0) skillSet.Groups.Add(group);
            }
            plan.SkillSets.Add(skillSet);
            }
        }

        var items = root.Element("Items");
        if (items != null)
        {
            var byId = new Dictionary<string, string>();
            foreach (var it in items.Elements("Item"))
            {
                string id = (string)it.Attribute("id") ?? "";
                var lines = it.Value.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                // "Rarity: RARE", then the name, then (for magic and better with two lines) the base.
                int start = lines.Count > 0 && lines[0].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                string rarity = start == 1 ? lines[0].Substring(7).Trim() : "";
                var name = lines.Skip(start).Take(2).Where(l => !l.Contains(':')).ToList();
                string text = string.Join(", ", name);
                if (rarity.Length > 0 && !rarity.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                    text = char.ToUpperInvariant(rarity[0]) + rarity.Substring(1).ToLowerInvariant() + " " + text;
                if (id.Length > 0 && text.Length > 0) byId[id] = text;
            }
            var slotSets = items.Elements("ItemSet").ToList();
            IEnumerable<XElement> slots = items.Elements("Slot");
            if (slotSets.Count > 0)
            {
                string activeId = (string)items.Attribute("activeItemSet") ?? "1";
                var set = slotSets.FirstOrDefault(x => (string)x.Attribute("id") == activeId) ?? slotSets[0];
                slots = set.Elements("Slot");
            }
            foreach (var sl in slots)
            {
                string slot = (string)sl.Attribute("name") ?? "";
                string id = (string)sl.Attribute("itemId") ?? "0";
                if (slot.Length > 0 && id != "0" && byId.TryGetValue(id, out string item)) plan.Gear.Add((slot, item));
            }
        }

        plan.Notes = (root.Element("Notes")?.Value ?? "").Trim();
        // PoB colours notes with ^1 or ^xRRGGBB; those are not words.
        plan.Notes = System.Text.RegularExpressions.Regex.Replace(plan.Notes, @"\^(x[0-9A-Fa-f]{6}|[0-9])", "");
        return plan;
    }
}
