using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using static PoE1Access.Loc;

namespace PoE1Access;

// Alerts about you and the fight, after Poe2Access's (by the game's own buff names, found
// 21 Nov 2026 in Data/BuffDefinitions):
//  - Your harmful states as they begin and end, with a short low buzz: "Frozen", "No longer
//    frozen"; ignited, chilled, shocked, poisoned, bleeding, corrupted blood, maimed, blinded,
//    hindered, and curses ("Cursed: Temporal Chains").
//  - Harmful ground: two harsh low stabs repeating while you stand in it (burning, chilled,
//    shocked, tarred, desecrated or caustic ground, vortexes, spores, a monster's degeneration
//    aura), and its name the first time.
//  - Frenzy, power and endurance charges: a rising or falling pair of notes as they change, and
//    the count ("3 frenzy charges").
//  - A boss (unique monster) near you: its life as it falls past 75, 50, 25 and 10 percent, and
//    "defeated".
//  - Level up: three rising notes and "Level 12".
public partial class PoE1AccessCore
{
    private static readonly Dictionary<string, string> AilmentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ignited"] = "Ignited", ["chilled"] = "Chilled", ["frozen"] = "Frozen", ["shocked"] = "Shocked",
        ["poison"] = "Poisoned", ["poisoned"] = "Poisoned", ["bleeding"] = "Bleeding", ["corrupted_blood"] = "Corrupted blood",
        ["maim"] = "Maimed", ["blinded"] = "Blinded", ["hinder"] = "Hindered",
    };
    private static readonly string[] SafeGround = { "ground_consecration", "ground_haste", "ground_tar_gold" };
    private static readonly string[] ChargeIds = { "frenzy_charge", "power_charge", "endurance_charge" };
    private static readonly string[] ChargeWords = { "frenzy", "power", "endurance" };

    // The states are kept by their English names (AilmentNames, "Cursed: ..."); this is how one
    // is said when it starts and when it ends.
    private static string AilmentSaid(string state) => state switch
    {
        "Ignited" => T("Ignited", "En llamas"),
        "Chilled" => T("Chilled", "Helado"),
        "Frozen" => T("Frozen", "Congelado"),
        "Shocked" => T("Shocked", "Electrocutado"),
        "Poisoned" => T("Poisoned", "Envenenado"),
        "Bleeding" => T("Bleeding", "Sangrando"),
        "Corrupted blood" => T("Corrupted blood", "Sangre corrupta"),
        "Maimed" => T("Maimed", "Mutilado"),
        "Blinded" => T("Blinded", "Cegado"),
        "Hindered" => T("Hindered", "Entorpecido"),
        _ when state.StartsWith("Cursed: ") => CursedSaid(state.Substring(8)),
        _ => state,
    };

    private static string CursedSaid(string curse) => T($"Cursed: {curse}", $"Maldito: {curse}");

    private static string AilmentEnded(string state) => state switch
    {
        "Ignited" => T("No longer ignited", "Ya no estás en llamas"),
        "Chilled" => T("No longer chilled", "Ya no estás helado"),
        "Frozen" => T("No longer frozen", "Ya no estás congelado"),
        "Shocked" => T("No longer shocked", "Ya no estás electrocutado"),
        "Poisoned" => T("No longer poisoned", "Ya no estás envenenado"),
        "Bleeding" => T("No longer bleeding", "Ya no estás sangrando"),
        "Corrupted blood" => T("No longer corrupted blood", "Ya no tienes sangre corrupta"),
        "Maimed" => T("No longer maimed", "Ya no estás mutilado"),
        "Blinded" => T("No longer blinded", "Ya no estás cegado"),
        "Hindered" => T("No longer hindered", "Ya no estás entorpecido"),
        _ when state.StartsWith("Cursed") => T("No longer cursed", "Ya no estás maldito"),
        _ => state,
    };

    // "No frenzy charges", "1 power charge", "3 endurance charges".
    private static string ChargeSaid(int kind, int count) => (kind, count) switch
    {
        (0, 0) => T("No frenzy charges", "Sin cargas de frenesí"),
        (1, 0) => T("No power charges", "Sin cargas de poder"),
        (2, 0) => T("No endurance charges", "Sin cargas de aguante"),
        (0, 1) => T("1 frenzy charge", "1 carga de frenesí"),
        (1, 1) => T("1 power charge", "1 carga de poder"),
        (2, 1) => T("1 endurance charge", "1 carga de aguante"),
        (0, _) => T($"{count} frenzy charges", $"{count} cargas de frenesí"),
        (1, _) => T($"{count} power charges", $"{count} cargas de poder"),
        _ => T($"{count} endurance charges", $"{count} cargas de aguante"),
    };

    private HashSet<string> _states = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _groundNamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _charges = { -1, -1, -1 };
    private long _nextAlertsMs, _nextGroundCueMs;
    private int _lastLevel = -1;
    private readonly Dictionary<long, (string Name, int Step)> _bosses = new Dictionary<long, (string, int)>();
    private long _alertsArea = -1;

    private static bool IsHarmfulGround(string id) =>
        !SafeGround.Contains(id, StringComparer.OrdinalIgnoreCase) &&
        (id.StartsWith("ground_", StringComparison.OrdinalIgnoreCase) || id.StartsWith("caustic_cloud", StringComparison.OrdinalIgnoreCase) ||
         id.Contains("_ground_", StringComparison.OrdinalIgnoreCase) || id.EndsWith("_ground", StringComparison.OrdinalIgnoreCase) ||
         id.StartsWith("monster_aura_", StringComparison.OrdinalIgnoreCase) && (id.Contains("degen") || id.Contains("burning")));

    private void PollPlayerAlerts(long now)
    {
        if (now < _nextAlertsMs) return;
        _nextAlertsMs = now + 150;
        Entity player = Try(() => GameController.Player, null);
        if (player == null) return;
        float vol = Settings.EnemyVolume.Value / 100f;

        long area = Try(() => (long)GameController.Area.CurrentArea.Hash, 0L);
        bool newArea = area != _alertsArea;
        if (newArea) { _alertsArea = area; _bosses.Clear(); _groundNamed.Clear(); }

        var buffs = Try(() => player.GetComponent<Buffs>()?.BuffsList, null) ?? new List<Buff>();
        var names = buffs.Select(b => (Id: Try(() => b.Name, "") ?? "", Buff: b)).Where(b => b.Id.Length > 0).ToList();

        // Your harmful states.
        if (Settings.StatusAlerts.Value)
        {
            var now2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var said = new List<string>();
            foreach (var (id, b) in names)
            {
                string spoken = AilmentNames.TryGetValue(id, out string n) ? n
                    : id.StartsWith("curse_", StringComparison.OrdinalIgnoreCase) ? "Cursed: " + (Try(() => b.DisplayName, "") is string dn && dn.Length > 0 ? dn : id.Substring(6).Replace('_', ' '))
                    : null;
                if (spoken == null || !now2.Add(spoken)) continue;
                if (!_states.Contains(spoken) && !newArea) said.Add(spoken);
            }
            var ended = _states.Where(s => !now2.Contains(s)).ToList();
            if (said.Count > 0)
            {
                _audio?.Play("ailment", Synth.Ailment, vol);
                SayNews(News.StatusBegins, string.Join(". ", said.Select(AilmentSaid)), true);
            }
            if (ended.Count > 0) SayNews(News.StatusEnds, string.Join(". ", ended.Select(AilmentEnded)), true);
            _states = now2;
        }

        // Harmful ground: a repeating cue while you stand in it, its name the first time.
        if (Settings.HarmfulGroundSound.Value)
        {
            var ground = names.Where(n => IsHarmfulGround(n.Id)).ToList();
            if (ground.Count > 0 && now >= _nextGroundCueMs)
            {
                _nextGroundCueMs = now + 700;
                _audio?.Play("harmful-ground", Synth.HarmfulGround, vol);
                foreach (var (id, b) in ground)
                    if (_groundNamed.Add(id))
                    {
                        string n = Try(() => b.DisplayName, "") ?? "";
                        SayNews(News.StatusBegins, n.Length > 0 ? n : T("Harmful ground", "Suelo dañino"), true);
                    }
            }
        }

        // Charges.
        if (Settings.ChargeAlerts.Value)
            for (int k = 0; k < 3; k++)
            {
                var c = names.FirstOrDefault(n => n.Id.Equals(ChargeIds[k], StringComparison.OrdinalIgnoreCase));
                int count = c.Buff == null ? 0 : Try(() => (int)c.Buff.BuffCharges, 0);
                if (count == 0 && c.Buff != null) count = Try(() => (int)c.Buff.Charges, 1);
                if (_charges[k] >= 0 && count != _charges[k] && !newArea)
                {
                    bool gained = count > _charges[k];
                    int kind = k;
                    _audio?.Play("charge:" + k + ":" + gained, () => Synth.Charge(kind, gained), vol);
                    SayNews(News.Charges, ChargeSaid(k, count), false);
                }
                _charges[k] = count;
            }

        // Level up.
        int level = Try(() => player.GetComponent<ExileCore.PoEMemory.Components.Player>()?.Level ?? 0, 0);
        if (level > 0)
        {
            if (_lastLevel > 0 && level > _lastLevel && Settings.AnnounceLevelUp.Value)
            {
                _audio?.Play("levelup", Synth.LevelUp, vol);
                SayNews(News.LevelUp, T($"Level {level}", $"Nivel {level}"), true);
                // The Path of Building build followed: where its next passive is (BuildFollow.cs).
                if (_build != null && Settings.BuildNextPassive.Value)
                {
                    string next = Try(BuildNextPassiveText, "");
                    if (next.Length > 0) Say(next, false);
                }
            }
            _lastLevel = level;
        }

        if (Settings.AnnounceBossLife.Value) PollBossLife(player);
    }

    // A unique monster near you: its life past 75, 50, 25 and 10 percent once it has been hit,
    // and "defeated".
    private static readonly int[] BossSteps = { 75, 50, 25, 10 };

    private void PollBossLife(Entity player)
    {
        var me = Try(() => player.GridPosNum, System.Numerics.Vector2.Zero);
        var seen = new HashSet<long>();
        foreach (Entity e in Try(() => GameController.EntityListWrapper.OnlyValidEntities, null) ?? (ICollection<Entity>)Array.Empty<Entity>())
        {
            if (Try(() => e.Type, EntityType.Error) != EntityType.Monster) continue;
            if (Try(() => e.Rarity, MonsterRarity.White) != MonsterRarity.Unique || !Try(() => e.IsHostile, false)) continue;
            long id = Try(() => (long)e.Id, 0L);
            if (id == 0) continue;
            float dist = (Try(() => e.GridPosNum, System.Numerics.Vector2.Zero) - me).Length();
            bool alive = Try(() => e.IsAlive, true);
            if (!_bosses.TryGetValue(id, out var known))
            {
                if (!alive || dist > 90) continue;
                string name = Try(() => e.RenderName, "") ?? "";
                if (name.Length == 0) name = T("The boss", "El jefe");
                _bosses[id] = known = (name, 0);
            }
            seen.Add(id);
            if (!alive)
            {
                if (known.Step >= 0) SayNews(News.BossLife, T($"{known.Name} defeated", $"{known.Name} derrotado"), true);
                _bosses[id] = (known.Name, -1);
                continue;
            }
            if (known.Step < 0) continue;
            int pct = (int)Math.Round(Try(() => e.GetComponent<Life>()?.HPPercentage ?? 1f, 1f) * 100);
            int step = known.Step;
            while (step < BossSteps.Length && pct <= BossSteps[step]) step++;
            if (step != known.Step)
            {
                _bosses[id] = (known.Name, step);
                SayNews(News.BossLife, T($"{known.Name}, {pct} percent", $"{known.Name}, {pct} por ciento"), pct <= 30);
            }
        }
        // Bosses gone from the list (left behind or unloaded) are forgotten after a while.
        if (_bosses.Count > 50) foreach (long gone in _bosses.Keys.Where(k => !seen.Contains(k)).ToList()) _bosses.Remove(gone);
    }
}
