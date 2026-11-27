using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using static PoE1Access.Loc;

namespace PoE1Access;

// Tutorials that start by themselves.
//
// A few seconds into the game with a new character (level 1 or 2, not answered for yet), and on the
// first game with the mod, it asks whether you want tutorials: tap L3 for yes, double tap for no (no
// answer in 30 seconds asks again next time). The answer is kept for that character
// ("answer:yes:<name>"); characters made before keep the first answer ("answer:yes"). With yes, each lesson
// starts the first time you meet what it is about: how the mod works at once; the scanner when you
// start moving; the sounds when an enemy first comes near; passive points when you level up; the
// passive tree when you open it; gems when one comes into your bag; the inventory, the stash, a
// vendor, the character sheet and skill binding when you first open them; your life and flasks
// when your life first runs low; towns, waypoints, the bandits, your hideout, the trials, the
// Labyrinth, resistances, maps and the Covens when you reach them.
//
// A lesson is read page by page: tap L3 for the next page, double tap to stop (the rest wait for
// another time), hold L3 to hear the page again. While a lesson plays these three are the
// lesson's; everything else works as usual. Lessons wait while an enemy is near or the mod menu is
// open, and come one at a time. A lesson heard once is not started again, on any character
// (app\mechanics-seen.txt); all of them stay in the tutorial menu. "Tutorials start as you meet
// each part of the game" in the Speech submenu turns this on or off later.
//
// Like the rest of the mod, it only reads and listens: nothing is pressed for you.
public partial class PoE1AccessCore
{
    private readonly List<string> _lessonQueue = new List<string>();
    private Lesson _lesson;
    private int _lessonPage;
    private long _nextLessonMs, _askedAtMs = -1, _inGameSinceMs = -1, _nextTriggerMs;
    private bool _askPending, _askedThisSession, _howToSaid;
    private Vector2 _startPos = new Vector2(-1, -1);
    private int _tutLevel = -1;

    private bool AutoTutorials => Settings.AutoTutorials.Value && TutorialAnswer() == true;

    private string _tutChar;   // the character the question was last looked at for

    private string TutorialCharacter() =>
        Try(() => GetComp<ExileCore.PoEMemory.Components.Player>(GameController.Player)?.PlayerName, null) ?? "";

    private bool NewCharacter() =>
        Try(() => GetComp<ExileCore.PoEMemory.Components.Player>(GameController.Player)?.Level ?? 0, 0) is > 0 and <= 2;

    /// <summary>This character's answer: its own, else (an older character) the first answer
    /// given; null when it is to be asked.</summary>
    private bool? TutorialAnswer()
    {
        string who = TutorialCharacter();
        if (who.Length > 0 && SeenHas("answer:yes:" + who)) return true;
        if (who.Length > 0 && SeenHas("answer:no:" + who)) return false;
        if (who.Length > 0 && NewCharacter()) return null;   // a new character is asked
        if (SeenHas("answer:yes")) return true;
        if (SeenHas("answer:no")) return false;
        return null;
    }

    private void SaveTutorialAnswer(bool yes)
    {
        string who = TutorialCharacter();
        SeenAdd(yes ? "answer:yes" : "answer:no");
        if (who.Length > 0) SeenAdd((yes ? "answer:yes:" : "answer:no:") + who);
    }

    private void LoadMechanicsSeen()
    {
        if (_mechanicsSeen != null) return;
        _mechanicsSeen = new HashSet<string>();
        try
        {
            _mechanicsFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "mechanics-seen.txt"));
            if (File.Exists(_mechanicsFile)) foreach (string l in File.ReadAllLines(_mechanicsFile)) _mechanicsSeen.Add(l.Trim());
        }
        catch { }
    }

    private bool SeenHas(string key) { LoadMechanicsSeen(); return _mechanicsSeen.Contains(key); }

    private void SeenAdd(string key)
    {
        LoadMechanicsSeen();
        if (!_mechanicsSeen.Add(key)) return;
        try { if (_mechanicsFile != null) File.AppendAllText(_mechanicsFile, key + Environment.NewLine); } catch { }
    }

    private IEnumerable<Lesson> AllLessons() => Lessons().Concat(Guides()).Concat(MechanicsLessons());

    /// <summary>Queues lessons (by key) not heard yet.</summary>
    private void Meet(params string[] keys)
    {
        if (!AutoTutorials) return;
        foreach (string k in keys)
            if (!SeenHas("lesson:" + k) && !_lessonQueue.Contains(k) && _lesson?.Key != k) _lessonQueue.Add(k);
    }

    // The tips of Mechanics.cs and Hideout.cs, as lessons when tutorials are on.
    private static string[] LessonsForTip(string tip) => tip switch
    {
        "town" => new[] { "towns" },
        "waypoint" => new[] { "waypoints", "areas" },
        "passives" => new[] { "levels" },
        "bandits" => new[] { "bandits" },
        "trial" => new[] { "trials", "lab traps" },
        "labyrinth" or "labyrinth run" => new[] { "lab rules", "lab chambers", "lab end" },
        "resistances" => new[] { "later" },
        "maps" => new[] { "maps", "map speed" },
        "covens" => new[] { "covens" },
        "hideout" or "hideout visit" => new[] { "hideout" },
        _ when tip.StartsWith("trial:") => new[] { "trials", "lab traps" },
        _ => null,
    };

    // ---- The question ----

    private void AskTutorials(long now)
    {
        if (_inGameSinceMs < 0) _inGameSinceMs = now;
        // Another character: it may be asked again, a few seconds after it enters.
        string who = TutorialCharacter();
        if (who.Length == 0) return;
        if (who != _tutChar)
        {
            if (_tutChar != null) { _askedThisSession = false; _askPending = false; _inGameSinceMs = now; _lessonQueue.Clear(); _lesson = null; }
            _tutChar = who;
        }
        if (_askPending && now - _askedAtMs > 30000)
        {
            _askPending = false;
            Say(T("No answer. You can turn tutorials on in the mod menu, under Speech, or answer next time.", "Sin respuesta. Puedes activar los tutoriales en el menú del mod, en Voz, o responder la próxima vez."), false);
        }
        if (_askedThisSession || TutorialAnswer() != null || !Settings.AutoTutorials.Value) return;
        if (now - _inGameSinceMs < 8000 || (_nav != null && _nav.EnemyNear(45f))) return;
        _askedThisSession = true;
        _askPending = true;
        _askedAtMs = now;
        PluginLog.Write("Tutorials asked");
        bool heardSome = _mechanicsSeen.Any(k => k.StartsWith("lesson:", StringComparison.Ordinal));
        Say(T("Welcome to Path of Exile access. Would you like tutorials for this character? Each one starts by itself the first time you meet what it is about, and you can stop any of them. Tap L3 for yes, double tap L3 for no.",
              "Bienvenido a Path of Exile access. ¿Quieres tutoriales para este personaje? Cada uno empieza solo la primera vez que encuentras aquello de lo que trata, y puedes pararlos. Toca L3 para sí, toca L3 dos veces para no.") +
            (heardSome ? T(" Lessons you have already heard do not play again; they are all in the mod menu, under Tutorial.", " Las lecciones que ya has oído no se repiten; están todas en el menú del mod, en Tutorial.") : ""), true);
    }

    /// <summary>L3 while the question or a lesson waits: true when it was theirs.</summary>
    private bool TutorialInput(ControllerInput.Command cmd)
    {
        if (_askPending)
        {
            if (cmd == ControllerInput.Command.Tap)
            {
                _askPending = false;
                SaveTutorialAnswer(true);
                PluginLog.Write("Tutorials: yes");
                Say(T("Tutorials on. The first starts now.", "Tutoriales activados. El primero empieza ahora."), true);
                Meet("basics");
                _nextLessonMs = 0;
                return true;
            }
            if (cmd == ControllerInput.Command.DoubleTap)
            {
                _askPending = false;
                SaveTutorialAnswer(false);
                PluginLog.Write("Tutorials: no");
                Say(T("No tutorials. They are all in the mod menu, under Tutorial, whenever you want them.", "Sin tutoriales. Están todos en el menú del mod, en Tutorial, cuando los quieras."), true);
                return true;
            }
            return false;
        }
        if (_lesson == null) return false;
        switch (cmd)
        {
            case ControllerInput.Command.Tap: NextLessonPage(); return true;
            case ControllerInput.Command.DoubleTap:
                PluginLog.Write("Lesson stopped: " + _lesson.Key);
                // Not marked heard: it starts again another time.
                _lesson = null;
                _nextLessonMs = _clock.ElapsedMilliseconds + 120000;
                Say(T("Lesson stopped. It will come back later, and it is in the mod menu, under Tutorial.", "Lección parada. Volverá más tarde, y está en el menú del mod, en Tutorial."), true);
                return true;
            case ControllerInput.Command.Hold:
                Say(LessonPageText(), true);
                return true;
        }
        return false;
    }

    // ---- Playing a lesson ----

    private string LessonPageText() =>
        _lesson == null ? "" : _lesson.Pages[_lessonPage] + " " + T($"Page {_lessonPage + 1} of {_lesson.Pages.Length}.", $"Página {_lessonPage + 1} de {_lesson.Pages.Length}.");

    private void NextLessonPage()
    {
        if (_lesson == null) return;
        if (_lessonPage + 1 < _lesson.Pages.Length) { _lessonPage++; Say(LessonPageText(), true); return; }
        var done = _lesson;
        _lesson = null;
        SeenAdd("lesson:" + done.Key);
        PluginLog.Write("Lesson done: " + done.Key);
        _nextLessonMs = _clock.ElapsedMilliseconds + 4000;
        if (done.Practice.Length > 0) { Say(T("End of lesson. Now try it.", "Fin de la lección. Ahora pruébalo."), true); StartPractice(done); }
        else Say(T("End of lesson.", "Fin de la lección."), true);
    }

    private void PollAutoTutorial(long now, bool uiBusy)
    {
        if (!Settings.AutoTutorials.Value) return;
        AskTutorials(now);
        if (!AutoTutorials) return;
        PollTutorialTriggers(now, uiBusy);
        if (_lesson != null || _lessonQueue.Count == 0 || now < _nextLessonMs || MenuOpen || _practice.Count > 0) return;
        if (_nav != null && _nav.EnemyNear(45f)) { _nextLessonMs = now + 2000; return; }
        string key = _lessonQueue[0];
        _lessonQueue.RemoveAt(0);
        var lesson = AllLessons().FirstOrDefault(l => l.Key == key);
        if (lesson == null || lesson.Pages.Length == 0) return;
        _lesson = lesson;
        _lessonPage = 0;
        PluginLog.Write("Lesson started: " + key);
        string how = _howToSaid ? "" : " " + T("Tap L3 for the next page, double tap to stop, hold to hear it again.", "Toca L3 para la página siguiente, dos veces para parar, mantén para oírla otra vez.");
        _howToSaid = true;
        Say(T($"Lesson: {lesson.Title}.", $"Lección: {lesson.Title}.") + how + " " + LessonPageText(), false);
    }

    // ---- What starts a lesson ----

    private void PollTutorialTriggers(long now, bool uiBusy)
    {
        if (now < _nextTriggerMs) return;
        _nextTriggerMs = now + 1000;
        var server = GetServerData();

        // Moving: the scanner, then the guide.
        Vector2 here = _nav != null ? Try(() => _nav.PlayerCell(), new Vector2(-1, -1)) : new Vector2(-1, -1);
        if (here.X >= 0)
        {
            if (_startPos.X < 0) _startPos = here;
            else if ((here - _startPos).Length() > 25f) Meet("scanner", "guide");
        }
        if (_nav != null && _nav.EnemyNear(60f)) Meet("sounds");

        // Levelling up: passive points.
        int level = Try(() => GameController.Player.GetComponent<ExileCore.PoEMemory.Components.Player>().Level, 0);
        if (_tutLevel > 0 && level > _tutLevel) Meet("levels");
        if (level > 0) _tutLevel = level;

        // Life running low: you and your character, then flasks.
        var life = GetComp<ExileCore.PoEMemory.Components.Life>(GameController.Player);
        if (life != null && Try(() => life.HPPercentage, 1f) < 0.5f) Meet("character", "flasks");

        // A gem in your bag: gems, socketing, links.
        if (server != null && Slots(server, InventorySlotE.MainInventory1).Any(s => (Try(() => s.Item.Path, "") ?? "").StartsWith("Metadata/Items/Gems/", StringComparison.OrdinalIgnoreCase)))
            Meet("gems", "socketing", "links");

        // Windows, the first time each opens.
        if (uiBusy) Meet("menus");
        if (_openPanels != null)
        {
            if (_openPanels.Any(t => IsGameWord(t, InventoryTitle))) Meet("inventory");
            if (_openPanels.Any(t => IsGameWord(t, PassivesTitle))) Meet("tree reading", "tree finder");
            if (_openPanels.Any(t => IsGameWord(t, CharacterTitle))) Meet("sheet");
            if (_openPanels.Any(t => IsGameWord(t, StashTitle))) Meet("stash");
        }
        if (_bindingMode) Meet("binding guide");
        if (PurchaseOpen() || _offerSig.Length > 0) Meet("currency", "vendors");
        if (AtlasOpen()) Meet("maps", "map speed");
    }
}
