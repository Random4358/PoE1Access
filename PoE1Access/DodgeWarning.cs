using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;

namespace PoE1Access;

// Dodge warning (after Poe2Access's): when a monster starts an attack or a spell aimed at you,
// a sharp click and two quick metal tings play from its side, louder the closer it is.
// By default only rare and unique monsters (the bosses among them) warn, a rare within about a
// screen and a unique from anywhere on screen; with "every enemy" any monster close to you
// does. It warns at the wind-up: when the monster's action turns into a new attack or spell (the
// Actor component's current action and its skill), once per action. Behind a wall it is silent,
// as the enemy sounds are.
internal sealed partial class Navigator
{
    public bool DodgeWarnings = true, DodgeWarningsAll;
    private readonly Dictionary<long, (ushort Action, float Progress)> _enemyAction = new Dictionary<long, (ushort, float)>();
    // Skills that only move the monster (seen 21 Nov 2026: a boss's flame_dash warned every
    // 3 seconds), by whole words of the skill's name, so "discharge" or a leap slam that lands
    // on you still warn.
    private static readonly HashSet<string> TravelWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "dash", "blink", "teleport", "walk", "move", "burrow", "emerge", "roll", "evade", "retreat" };
    private long _lastDodgeMs;
    private const float DodgeAllCells = 30f;   // "every enemy": only those this close (a few steps)

    private void TickDodge(List<(Entity E, long Id, Vector2 G, float Dist, MonsterRarity Rarity)> near, Vector2 p, long now)
    {
        if (!DodgeWarnings) { _enemyAction.Clear(); return; }
        long me = Try(() => _gc.Player.Address, 0L);
        foreach (var (e, id, g, dist, rarity) in near)
        {
            bool big = rarity == MonsterRarity.Rare || rarity == MonsterRarity.Unique;
            if (!big && !(DodgeWarningsAll && dist <= DodgeAllCells)) continue;
            var actor = Try(() => e.GetComponent<Actor>(), null);
            if (actor == null) continue;
            bool usingAbility = Try(() => (actor.Action & ActionFlags.UsingAbility) != 0, false);
            ushort actionId = usingAbility ? Try(() => (ushort)actor.ActionId, (ushort)0) : (ushort)0;
            // A new attack: a new action, or the same one's animation starting over (a monster that
            // keeps swinging), not merely time passing in one long action.
            float progress = usingAbility ? Try(() => actor.AnimationController?.AnimationProgress ?? 0f, 0f) : 0f;
            bool had = _enemyAction.TryGetValue(id, out var last);
            _enemyAction[id] = (actionId, progress);
            if (!usingAbility) continue;
            bool newSwing = !had || actionId != last.Action || progress + 0.25f < last.Progress;
            if (!newSwing) continue;

            var action = Try(() => actor.CurrentAction, null);
            var skill = Try(() => action?.Skill, null);
            bool attack = Try(() => skill != null && (skill.IsAttack || skill.IsSpell), false) || Try(() => actor.isAttacking, false);
            if (!attack) continue;
            string internalName = Try(() => skill?.InternalName, "") ?? "";
            if (internalName.Split('_', ' ').Any(TravelWords.Contains)) continue;
            // Aimed at you, or with no target (a slam or a cone where it stands).
            long target = Try(() => action?.Target?.Address ?? 0L, 0L);
            if (target != 0 && target != me) continue;
            if (!InSight(p, g)) continue;
            if (rarity == MonsterRarity.Rare && dist > EnemyCells) continue;
            if (now - _lastDodgeMs < 150) continue;
            _lastDodgeMs = now;

            float pan = 0f;
            if (dist >= 2.5f)
            {
                Vector2 sd = ScreenDir(p, g);
                pan = (float)Math.Round(Math.Max(-0.9f, Math.Min(0.9f, sd.X * 0.9f)), 1);
            }
            // Louder the closer, never faint for a unique.
            float near01 = 1f - Math.Min(1f, dist / EnemyCells);
            float gain = (rarity == MonsterRarity.Unique ? 0.75f : 0.45f) + 0.55f * near01;
            // Well above the other sounds, which are turned down while it plays.
            float vol = (EnemyVolume?.Invoke() ?? 0.7f) * Math.Min(1f, gain) * 1.6f;
            _audio.PlayPriority("dodge3:" + pan, () => Synth.DodgeWarning(pan), vol, 250);
            PluginLog.Write("Dodge warning: " + rarity + " " + internalName + " at " + dist.ToString("0") + " cells");
        }
        if (_enemyAction.Count > 400) _enemyAction.Clear();
    }
}
