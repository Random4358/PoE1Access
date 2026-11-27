using System;
using ExileCore.Shared.Nodes;
using static PoE1Access.Loc;

namespace PoE1Access;

/// <summary>The kinds of news the mod says by itself, each with its own speech priority.</summary>
public enum News { StatusBegins, StatusEnds, LifeAndMana, BossLife, EnemyNames, Quest, Places, Bag, Arrivals, Charges, LevelUp }

// Speech priority (as in Poe2Access, after the D2R mod): how each kind of news is delivered.
//   0 waits its turn: said after what is being said.
//   1 cuts in when urgent: cuts in only when it matters now (life under half, a boss at 30 percent
//     or defeated, the enemy you aim at, a rare or unique coming into view), else waits.
//   2 cuts in: stops what is being said.
//   3 cuts in and cannot be cut off: other speech queues behind it for 1.5 s. What you ask for
//     yourself with the controller still cuts in.
// "Speech priority" in the mod menu sets each one.
public partial class PoE1AccessCore
{
    private long _holdUntilMs;
    private bool _fromInput;

    private RangeNode<int> NewsLevel(News kind) => kind switch
    {
        News.StatusBegins => Settings.SpeechStatusBegins,
        News.StatusEnds => Settings.SpeechStatusEnds,
        News.LifeAndMana => Settings.SpeechLifeAndMana,
        News.BossLife => Settings.SpeechBossLife,
        News.EnemyNames => Settings.SpeechEnemyNames,
        News.Quest => Settings.SpeechQuest,
        News.Places => Settings.SpeechPlaces,
        News.Bag => Settings.SpeechBag,
        News.Arrivals => Settings.SpeechArrivals,
        News.Charges => Settings.SpeechCharges,
        _ => Settings.SpeechLevelUp,
    };

    private static string NewsLevelWords(int v) => v switch
    {
        0 => T("waits its turn", "espera su turno"),
        1 => T("cuts in when urgent", "interrumpe si es urgente"),
        2 => T("cuts in", "interrumpe"),
        _ => T("cuts in and cannot be cut off", "interrumpe y no se puede cortar"),
    };

    /// <summary>Says news of one kind at its chosen priority; `urgent` is when it matters now.</summary>
    private void SayNews(News kind, string text, bool urgent)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        int level = Try(() => NewsLevel(kind).Value, 0);
        bool interrupt = level >= 2 || (level == 1 && urgent);
        long now = _clock.ElapsedMilliseconds;
        if (now < _holdUntilMs) interrupt = false;   // a line that cannot be cut off is being said
        Say(text, interrupt);
        if (level == 3) _holdUntilMs = now + 1500;
    }
}
