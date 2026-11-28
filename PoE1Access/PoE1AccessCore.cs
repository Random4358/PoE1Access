using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared.Nodes;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using BaseComp = ExileCore.PoEMemory.Components.Base;
using ModsComp = ExileCore.PoEMemory.Components.Mods;
using QualityComp = ExileCore.PoEMemory.Components.Quality;
using SocketsComp = ExileCore.PoEMemory.Components.Sockets;
using StackComp = ExileCore.PoEMemory.Components.Stack;
using NormalInventoryItem = ExileCore.PoEMemory.Elements.InventoryElements.NormalInventoryItem;
using TreePassiveElement = ExileCore.PoEMemory.Elements.TreePassiveElement;
using static PoE1Access.Loc;

namespace PoE1Access;

/// <summary>
/// Menu reading for a Path of Exile 1 accessibility plugin, built on ExileApi.
/// Read-only: it never sends input and never writes to the game.
///
/// What it does:
///  * Hover reader: speaks the menu element, item or passive node under the cursor / focus.
///  * Panel announcements: says when the inventory, passive tree, stash and so on open or close.
///  * Item browser: hotkeys that step through your inventory or equipment, reading each item.
///  * Dump tools: write the real UI and item data to text files, so the readers can be tuned.
///
/// Every ExileCore name used here was checked against the metadata of the ExileCore.dll that
/// ships with ExileApi-Compiled. The behaviour has NOT been run: it needs a first test in game.
/// </summary>
public partial class PoE1AccessCore : BaseSettingsPlugin<PoE1AccessSettings>
{
    private const int MaxTextParts = 80;
    private const int MaxVisitedNodes = 400;
    private const int MaxDumpNodes = 5000;
    private const int MaxSpeechChars = 2500;

    private static readonly Regex TagRegex = new Regex("<[^<>]{1,60}>", RegexOptions.Compiled);
    private static readonly Regex KeywordRegex = new Regex(@"\[([^\[\]|]{1,60})(?:\|([^\[\]]{1,80}))?\]", RegexOptions.Compiled);
    // Grammar marks of the translated texts: "<<set:M>><<set:S>><<set:MS>>Introduce el nombre de tu Templario".
    private static readonly Regex GrammarRegex = new Regex("<<set:[^<>]{0,20}>>", RegexOptions.Compiled);
    private static readonly Regex ControllerRegex = new Regex("<<\\s*([A-Za-z0-9_]{1,40})\\s*>>", RegexOptions.Compiled);

    // "xbox_button_a" -> "A button", "xbox_dpad_up" -> "D-pad up", "xbox_lt" -> "left trigger".
    private static string PromptName(string token)
    {
        string t = token.ToLowerInvariant();
        foreach (string prefix in new[] { "xbox_", "ps_", "playstation_", "controller_" })
            if (t.StartsWith(prefix)) { t = t.Substring(prefix.Length); break; }

        switch (t)
        {
            case "lt": case "left_trigger": return T("left trigger", "LT");
            case "rt": case "right_trigger": return T("right trigger", "RT");
            case "lb": case "left_bumper": return T("left bumper", "LB");
            case "rb": case "right_bumper": return T("right bumper", "RB");
            case "ls": case "left_stick": return T("left stick", "stick izquierdo");
            case "rs": case "right_stick": return T("right stick", "stick derecho");
        }
        if (t.StartsWith("button_")) t = t.Substring(7);
        switch (t)
        {
            case "a": case "b": case "x": case "y": return t.ToUpperInvariant();
            case "lb": return T("left bumper", "LB");
            case "rb": return T("right bumper", "RB");
            case "lt": return T("left trigger", "LT");
            case "rt": return T("right trigger", "RT");
            case "lstick": return "L3";
            case "rstick": return "R3";
            case "menu": case "start": return "Start";
            case "view": case "back": case "select": return "View";
        }
        if (t.StartsWith("dpad_"))
        {
            string dir = t.Substring(5).Replace('_', ' ');
            return T($"D-pad {CoreDirection(dir)}", $"Cruceta {CoreDirection(dir)}");
        }
        return t.Replace('_', ' ');
    }

    // A direction word of a controller prompt ("up", "down left"), in the mod's language.
    private static string CoreDirection(string dir)
    {
        switch (dir)
        {
            case "up": return T("up", "arriba");
            case "down": return T("down", "abajo");
            case "left": return T("left", "izquierda");
            case "right": return T("right", "derecha");
            case "up left": return T("up left", "arriba a la izquierda");
            case "up right": return T("up right", "arriba a la derecha");
            case "down left": return T("down left", "abajo a la izquierda");
            case "down right": return T("down right", "abajo a la derecha");
        }
        return dir;
    }

    /// <summary>The spoken name of an equipment slot, from its English label in EquipmentSlots
    /// (those stay English: other code compares against them).</summary>
    internal static string EquipmentSlotName(string label)
    {
        switch (label)
        {
            case "Main hand": return T("Main hand", "Mano principal");
            case "Off hand": return T("Off hand", "Mano secundaria");
            case "Helmet": return T("Helmet", "Casco");
            case "Body armour": return T("Body armour", "Armadura corporal");
            case "Gloves": return T("Gloves", "Guantes");
            case "Boots": return T("Boots", "Botas");
            case "Belt": return T("Belt", "Cinturón");
            case "Amulet": return T("Amulet", "Amuleto");
            case "Ring 1": return T("Ring 1", "Anillo 1");
            case "Ring 2": return T("Ring 2", "Anillo 2");
            case "Flask": return T("Flask", "Frasco");
            case "Swap main hand": return T("Swap main hand", "Mano principal alternativa");
            case "Swap off hand": return T("Swap off hand", "Mano secundaria alternativa");
        }
        return label;
    }

    private ISpeech _speech;

    // ===================================================================
    // Game and memory: from ExileApi normally, or from the launcher in standalone mode (the
    // login and character screens before ExileApi has started; see StandaloneHost).
    // ===================================================================

    private TheGame _standaloneGame;
    private ExileCore.Shared.Interfaces.IMemory _standaloneMem;
    private bool Standalone => _standaloneGame != null;

    private TheGame Game => _standaloneGame ?? GameController?.Game;
    private ExileCore.Shared.Interfaces.IMemory Mem => _standaloneMem ?? GameController?.Memory;

    // Held while this plugin runs inside ExileApi, so the launcher's standalone copy stays quiet.
    public const string InExileApiMutexName = "PoE1Access.InExileApi";
    private System.Threading.Mutex _inExileApiMutex;

    /// <summary>Standalone mode: read the pre-game screens with the launcher's game and memory.</summary>
    public void StartStandalone(TheGame game, ExileCore.Shared.Interfaces.IMemory mem)
    {
        _standaloneGame = game;
        _standaloneMem = mem;
        if (Settings == null)
        {
            // ExileApi normally creates the settings; here the defaults are used.
            var p = typeof(BaseSettingsPlugin<PoE1AccessSettings>).GetProperty("_Settings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            p?.SetValue(this, new PoE1AccessSettings());
        }
        Loc.UseGameLanguage(PluginLog.Write);
        _speech = SpeechFactory.Create();
        PluginLog.Write("Standalone mode started (launcher). Speech: " + _speech.Name);
    }

    /// <summary>Standalone mode, called about twenty times a second by the launcher.</summary>
    public void StandaloneTick()
    {
        lock (_tickLock)
        {
            if (_speech == null || Settings == null) return;
            long now = _clock.ElapsedMilliseconds;
            if (Pressed("repeat", Settings.RepeatHotkey, Keys.F9) && _lastSpoken.Length > 0) _speech.Speak(_lastSpoken, true);

            // Only the screens before the game; gameplay is ExileApi's.
            if (Try(() => Game.IsInGameState, false) && !Try(() => Game.IsSelectCharacterState, false))
            {
                _lastInGameMs = now;
                return;
            }
            if (Try(() => Game.IsLoadingState, false)) return;
            Guard("standalone", () => PreGameTick(now));
        }
    }

    /// <summary>Standalone mode: a fresh game object (the old one's state list went stale).</summary>
    public void SetStandaloneGame(TheGame game)
    {
        lock (_tickLock)
        {
            _standaloneGame = game;
            _preGameStateAddr = 0;
            _preGameRoot = null;
        }
    }

    /// <summary>Standalone mode paused (ExileApi took over): forget the screen, so it is
    /// announced afresh if the launcher has to take over again.</summary>
    public void StandalonePause()
    {
        lock (_tickLock) _preGameState = "";
    }
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Hover reading.
    private long _nextHoverPollMs;
    private long _hoverKeyHover;
    private long _hoverKeyTip;
    private long _hoverCachedAtMs;
    private string _hoverText = "";
    private string _pendingText = "";
    private long _pendingSinceMs;
    private string _lastHoverSpoken = "";
    private bool _hoverQuiet;   // the hovered passive node is the one the finder was used on
    private string _lastSpoken = "";

    // Panel announcements.
    private long _nextPanelPollMs;

    // Item browser.
    private bool _browseEquipment;
    private int _browseIndex = -1;

    private readonly Dictionary<string, long> _errorLastMs = new Dictionary<string, long>();

    private sealed class BrowseEntry
    {
        public string Label = "";
        public Entity Item;
        public int Col;
        public int Row;
    }

    private static readonly (InventorySlotE Slot, string Label)[] EquipmentSlots =
    {
        (InventorySlotE.Weapon1, "Main hand"),
        (InventorySlotE.Offhand1, "Off hand"),
        (InventorySlotE.Helm1, "Helmet"),
        (InventorySlotE.BodyArmour1, "Body armour"),
        (InventorySlotE.Gloves1, "Gloves"),
        (InventorySlotE.Boots1, "Boots"),
        (InventorySlotE.Belt1, "Belt"),
        (InventorySlotE.Amulet1, "Amulet"),
        (InventorySlotE.Ring1, "Ring 1"),
        (InventorySlotE.Ring2, "Ring 2"),
        (InventorySlotE.Flask1, "Flask"),
        (InventorySlotE.Weapon2, "Swap main hand"),
        (InventorySlotE.Offhand2, "Swap off hand"),
    };

    // ===================================================================
    // Plugin entry points
    // ===================================================================

    public override bool Initialise()
    {
        try
        {
            PluginLog.Write("Initialise. Base folder: " + AppContext.BaseDirectory);
            Loc.UseGameLanguage(PluginLog.Write);
            _speech = SpeechFactory.Create();
            PluginLog.Write("Speech: " + _speech.Name);

            _audio = new AudioEngine();
            PluginLog.Write("Audio: " + (_audio.Available ? "XAudio2 ready" : "unavailable: " + _audio.Error));
            _gameSounds = new GameSounds();
            PluginLog.Write("Game sounds: " + (_gameSounds.Available ? "the game's FMOD banks are open" : "not used (" + _gameSounds.Error + ")"));
            _soundPack = new SoundPack();
            PluginLog.Write("Sound pack: " + (_soundPack.Available ? _soundPack.Folder + " (" + string.Join(", ", _soundPack.Names) + ")" : "none (no sounds folder beside PoE1Access.exe)"));
            _nav = new Navigator(GameController, _audio, t => Say(t, true), t => Say(t, false),
                () => Settings.RadarVolume.Value / 100f, () => Settings.GuideVolume.Value / 100f);
            _nav.News = SayNews;

            Say(T($"Path of Exile access plugin loaded. Using {_speech.Name}.", $"Complemento de accesibilidad de Path of Exile cargado. Usando {_speech.Name}.") +
                (_audio.Available ? "" : T(" Sounds are not available.", " Los sonidos no están disponibles.")), true);
            StartBackgroundLoop();

            // Tell the launcher that ExileApi now runs the plugin.
            try { _inExileApiMutex = new System.Threading.Mutex(true, InExileApiMutexName); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Write("Initialise failed: " + ex);
            LogError("PoE1Access failed to start: " + ex.Message);
            return false;
        }
    }

    // Diagnostics: log state changes once, not every frame.
    private bool _loggedFirstTick;
    private bool? _lastForeground;
    private bool? _lastInGame;

    // ===================================================================
    // Background loop: ExileApi stops calling Tick outside gameplay (character selection,
    // login), so a loop of our own takes over whenever Tick has not run for a moment.
    // ===================================================================

    private readonly object _tickLock = new object();
    private long _lastTickMs = -100000;
    private System.Threading.Thread _bgThread;
    private volatile bool _bgStop;
    private bool _bgActive;

    private void StartBackgroundLoop()
    {
        if (_bgThread != null) return;
        _bgStop = false;
        _bgThread = new System.Threading.Thread(BackgroundLoop) { IsBackground = true, Name = "PoE1Access background" };
        _bgThread.Start();
    }

    private void StopBackgroundLoop()
    {
        _bgStop = true;
        try { _bgThread?.Join(500); } catch { }
        _bgThread = null;
    }

    private void BackgroundLoop()
    {
        while (!_bgStop)
        {
            System.Threading.Thread.Sleep(50);
            if (_clock.ElapsedMilliseconds - System.Threading.Interlocked.Read(ref _lastTickMs) < 400)
            {
                if (_bgActive) { _bgActive = false; PluginLog.Write("Background loop: ExileApi is ticking again"); }
                continue;
            }
            lock (_tickLock)
            {
                if (!_bgActive)
                {
                    _bgActive = true;
                    PluginLog.Write("Background loop took over (ExileApi stopped calling Tick). In-game state: " +
                                    Try(() => Game.IsInGameState, false) + ", select character: " +
                                    Try(() => Game.IsSelectCharacterState, false) + ", login: " +
                                    Try(() => Game.IsLoginState, false));
                }
                try { BackgroundTick(); }
                catch (Exception ex) { Report("background", ex); }
            }
        }
    }

    private void BackgroundTick()
    {
        if (!Settings.Enable.Value || _speech == null) return;
        if (!Try(() => GameController.Window.IsForeground(), false)) return;

        HandleHotkeys();

        // In gameplay ExileApi ticks us itself; only cover the screens it skips.
        if (Try(() => Game.IsInGameState, false) && !Try(() => Game.IsSelectCharacterState, false))
            return;
        if (Try(() => Game.IsLoadingState, false)) return;

        long now = _clock.ElapsedMilliseconds;
        PreGameTick(now);
    }

    private void ReleaseInExileApiMutex()
    {
        try { _inExileApiMutex?.ReleaseMutex(); _inExileApiMutex?.Dispose(); } catch { }
        _inExileApiMutex = null;
    }

    public override void OnClose()
    {
        ReleaseInExileApiMutex();
        StopBackgroundLoop();
        try { _audio?.Dispose(); } catch { }
        try { _gameSounds?.Dispose(); } catch { }
        base.OnClose();
    }

    public override void OnUnload()
    {
        ReleaseInExileApiMutex();
        StopBackgroundLoop();
        base.OnUnload();
    }

    public override void OnPluginDestroyForHotReload()
    {
        ReleaseInExileApiMutex();
        StopBackgroundLoop();
        try { _audio?.Dispose(); } catch { }
        base.OnPluginDestroyForHotReload();
    }

    // Called every frame. It draws nothing; it only reads game state and speaks.
    public override Job Tick()
    {
        System.Threading.Interlocked.Exchange(ref _lastTickMs, _clock.ElapsedMilliseconds);
        lock (_tickLock) return TickBody();
    }

    private Job TickBody()
    {
        if (!Settings.Enable.Value || _speech == null) return null;

        try
        {
            if (!_loggedFirstTick)
            {
                _loggedFirstTick = true;
                PluginLog.Write("First tick.");
            }

            bool foreground = GameController.Window.IsForeground();
            _nav?.LoopWatchdog(_clock.ElapsedMilliseconds);
            if (_lastForeground != foreground)
            {
                _lastForeground = foreground;
                PluginLog.Write("Game window in foreground: " + foreground);
            }
            if (!foreground) return null;

            HandleHotkeys();

            _gameSounds?.Update();
            bool inGame = GameController.InGame && !GameController.IsLoading;
            if (_lastInGame != inGame)
            {
                _lastInGame = inGame;
                PluginLog.Write("In game (not loading): " + inGame);
            }
            long now = _clock.ElapsedMilliseconds;

            // Outside gameplay (character selection, character creation, login once ExileApi is
            // running): announce the screen and read its menus with the focus reader.
            if (!inGame)
            {
                if (!Try(() => GameController.IsLoading, false)) Guard("pre-game", () => PreGameTick(now));
                return null;
            }
            _preGameState = "";
            _lastInGameMs = now;
            long t0 = _clock.ElapsedMilliseconds;
            PollEscape();
            NoteSlow("escape", t0); t0 = _clock.ElapsedMilliseconds;
            if (Settings.AnnouncePanels.Value) PollPanels(now);
            NoteSlow("panels", t0); t0 = _clock.ElapsedMilliseconds;
            if (Settings.ReadFocus.Value) PollFocus(now);
            NoteSlow("focus", t0); t0 = _clock.ElapsedMilliseconds;
            if (Settings.ReadOnHover.Value) PollHover(now);
            NoteSlow("hover", t0);
            Guard("socket selection", () => PollSocketSelection(now));

            // Navigation: silent while a panel, window or menu is open.
            bool uiBusy = _escWasOpen ||
                          (_openPanels != null && _openPanels.Count > 0) ||
                          now - _lastFocusEventMs < 1500 ||
                          now - _lastHoverTextMs < 600;
            if (uiBusy != _lastUiBusy)
            {
                _lastUiBusy = uiBusy;
                PluginLog.Write("Navigation " + (uiBusy ? "paused (menu, panel or dialogue)" : "resumed"));
            }
            HandleNavigationInput(now, uiBusy);
            _fromInput = false;
            Guard("inventory moves", () => PollInventoryMoves(now));
            Guard("sell offer", () => PollSellOffer(now));
            Guard("map device", () => PollMapDevice(now));
            Guard("monsters left", PollMonstersLeft);
            Guard("equipment selection", () => PollEquipmentSelection(now));
            Guard("stat panels", () => PollStatPanels(now));
            Guard("resources", () => PollResources(now));
            Guard("player alerts", () => PollPlayerAlerts(now));
            Guard("dialogue", () => PollDialogue(now));
            Guard("fates", () => PollFates(now));
            Guard("mechanic tips", () => PollMechanicTips(now, uiBusy || _bindingMode || MenuOpen));
            Guard("tutorials", () => PollAutoTutorial(now, uiBusy));
            Guard("quest route", () => PollQuestRoute(now, uiBusy));
            Guard("build", PollBuild);
            if (!uiBusy && !MenuOpen) Guard("hideout", () => PollHideout(now));
            Guard("world map", () => PollWorldMap(now));
            Guard("control hints", () => PollControlHints(now));
            Guard("button legend", () => PollButtonLegend(now, uiBusy));
            if (_nav != null)
            {
                _nav.AnnounceNearby = Settings.AnnounceNearby.Value;
                _nav.RoutePrompts = Settings.RoutePrompts.Value;
                _nav.UnexploredMark = Settings.UnexploredMark.Value;
                _nav.LootKind = Settings.LootSounds.Value ? LootKindOf : null;
                _nav.GameAudio = Settings.GameLootSounds.Value ? _gameSounds : null;
                _nav.Pack = Settings.SoundPackSounds.Value ? _soundPack : null;
                _nav.PackAudio = _gameSounds;
                _nav.LootEvent ??= LootEventOf;
                _nav.QuestObjectives ??= QuestObjectives;
                _nav.LootVolume ??= () => Settings.LootVolume.Value / 100f;
                _nav.EnemyVolume ??= () => Settings.EnemyVolume.Value / 100f;
                _nav.EnemySounds = Settings.EnemySounds.Value;
                _nav.NpcSounds = Settings.NpcSounds.Value;
                _nav.PlayerSounds = Settings.PlayerSounds.Value;
                _nav.ReachPing = Settings.ReachPing.Value;
                _nav.ReachDistance = Settings.ReachDistance.Value;
                _nav.AnnounceEnemies = Settings.AnnounceEnemies.Value;
                _nav.DodgeWarnings = Settings.DodgeWarnings.Value;
                _nav.DodgeWarningsAll = Settings.DodgeWarningsAll.Value;
                _nav.ObjectSounds = Settings.ObjectSounds.Value;
                _nav.EnemyStateSounds = Settings.EnemyStateSounds.Value;
                _nav.DescribeAreas = Settings.DescribeAreas.Value;
                _nav.EnemyDirections = Settings.EnemyDirections.Value;
                _nav.CrosshairBeep = Settings.CrosshairBeep.Value;
                _nav.AnnounceTarget = Settings.AnnounceTarget.Value;
                _nav.FollowLootFilter = Settings.FollowLootFilter.Value;
                _nav.ExploreMode = Settings.ExploreMode.Value;
            }
            // Skill binding mode (R3) keeps the guide's
            // directions quiet like a window does, so they do not talk over the skills.
            Guard("navigation", () => _nav?.Tick(now, uiBusy || _bindingMode, Settings.Radar.Value, ItemName));
        }
        catch (Exception ex)
        {
            Report("tick", ex);
        }
        return null;
    }

    // ===================================================================
    // Navigation input (controller L3 gestures and keyboard)
    // ===================================================================

    private AudioEngine _audio;
    private Navigator _nav;
    private readonly ControllerInput _pad = new ControllerInput();
    private long _lastFocusEventMs = -100000;
    private long _lastHoverTextMs = -100000;
    private bool _lastUiBusy;

    private void HandleNavigationInput(long now, bool uiBusy)
    {
        if (_nav == null) return;

        ControllerInput.Command cmd = ControllerInput.Command.None;
        if (Settings.ControllerCommands.Value)
        {
            _pad.Game ??= new GameXInput(() => Mem);
            _pad.AllowPlayStation = Settings.ReadPlayStationController.Value;
            if (!_pad.AllowPlayStation) _pad.ClosePlayStation();
            cmd = _pad.Poll(now);
            _nav.LeftStick = _pad.LeftStick;
        }

        if (cmd != ControllerInput.Command.None) PluginLog.Write("Controller: " + cmd);
        // What the player asks for cuts in even while news that cannot be cut off is said.
        _fromInput = cmd != ControllerInput.Command.None;

        if (Pressed("scan previous category", Settings.ScanPrevCategoryHotkey, Keys.PageUp)) cmd = ControllerInput.Command.ScanUp;
        if (Pressed("scan next category", Settings.ScanNextCategoryHotkey, Keys.PageDown)) cmd = ControllerInput.Command.ScanDown;
        if (Pressed("scan previous", Settings.ScanPrevHotkey, Keys.Home)) cmd = ControllerInput.Command.ScanLeft;
        if (Pressed("scan next", Settings.ScanNextHotkey, Keys.End)) cmd = ControllerInput.Command.ScanRight;
        if (Pressed("where", Settings.WhereHotkey, Keys.Insert)) cmd = ControllerInput.Command.Tap;
        if (Pressed("guide", Settings.GuideHotkey, Keys.Delete)) cmd = ControllerInput.Command.Guide;
        if (Pressed("status", Settings.StatusHotkey, Keys.F1)) cmd = ControllerInput.Command.DoubleTap;

        // The passive tree finder on the D-pad (Path of Exile 2 Access's): only with the tree open.
        int dpad = Settings.ControllerCommands.Value ? _pad.DPadPressed : 0;
        if (dpad != 0 && !MenuOpen && Settings.PassiveFinder.Value && _openPanels != null && _openPanels.Any(t => IsGameWord(t, PassivesTitle)))
            Guard("passive finder", () => FinderStep(dpad));
        // The Atlas finder on the D-pad, with the Atlas open (Atlas.cs).
        if (dpad != 0 && !MenuOpen && Settings.AtlasFinder.Value && AtlasOpen())
            Guard("atlas finder", () => AtlasStep(dpad));

        // The mod's settings menu: while open it takes the mod's gestures and the arrow keys.
        if (MenuOpen)
        {
            if (_keys.PressedOnce(Keys.Up)) cmd = ControllerInput.Command.ScanUp;
            if (_keys.PressedOnce(Keys.Down)) cmd = ControllerInput.Command.ScanDown;
            if (_keys.PressedOnce(Keys.Left)) cmd = ControllerInput.Command.ScanLeft;
            if (_keys.PressedOnce(Keys.Right)) cmd = ControllerInput.Command.ScanRight;
            bool back = _keys.PressedOnce(Keys.Back);
            if (_openPanels != null && _openPanels.Count > 0) { CloseMenu(); return; }   // a game panel opened
            switch (cmd)
            {
                case ControllerInput.Command.ScanUp: Guard("menu", () => MenuStep(-1, 0)); break;
                case ControllerInput.Command.ScanDown: Guard("menu", () => MenuStep(1, 0)); break;
                case ControllerInput.Command.ScanLeft: Guard("menu", () => MenuStep(0, -1)); break;
                case ControllerInput.Command.ScanRight: Guard("menu", () => MenuStep(0, 1)); break;
                case ControllerInput.Command.Tap: Guard("menu", MenuBack); break;
                case ControllerInput.Command.DoubleTap:
                case ControllerInput.Command.LongHold: Guard("menu", CloseMenu); break;
            }
            if (back) Guard("menu", MenuBack);
            return;
        }
        if (!uiBusy && cmd == ControllerInput.Command.LongHold)
        {
            Guard("menu", OpenMenu);
            return;
        }
        // The tutorials' question, or a lesson playing, takes tap, double tap and hold of L3.
        if (cmd != ControllerInput.Command.None && Try(() => TutorialInput(cmd), false)) return;

        // Character sheet open: L3 held + right stick (or PageUp/PageDown/Home/End) moves through
        // its stats: up/down one stat, left/right one section. The controller itself moves in the
        // inventory beside it, so the sheet has its own keys.
        if (uiBusy && _charLines.Count > 0)
        {
            int stat = cmd == ControllerInput.Command.ScanUp ? -1 : cmd == ControllerInput.Command.ScanDown ? 1 : 0;
            int section = cmd == ControllerInput.Command.ScanLeft ? -1 : cmd == ControllerInput.Command.ScanRight ? 1 : 0;
            if (stat != 0 || section != 0)
            {
                Guard("character sheet", () => CharacterStep(stat, section));
                return;
            }
        }

        // In menus a triple tap asks Path of Building what an item or passive would change
        // (BuildCraft.cs); with nothing to ask about it is a hold, as the keyboard's Delete.
        if (uiBusy && cmd == ControllerInput.Command.Guide)
        {
            if (Try(CraftWhatIf, false)) return;
            cmd = ControllerInput.Command.Hold;
        }
        // On the passive tree, L3 and right plans the node under the cursor (BuildCraft.cs).
        if (uiBusy && cmd == ControllerInput.Command.ScanRight && _passiveHover != null && _passiveHoverRow != 0 &&
            _craftRowMs >= _craftItemMs && Try(() => UiText.Visible(Mem, _passiveHover), false))
        {
            Guard("plan", TogglePlanned);
            return;
        }
        if (uiBusy && cmd == ControllerInput.Command.ScanRight && _tipLines.Count > 0) cmd = ControllerInput.Command.LongHold;

        bool inventoryOpen = _openPanels != null && _openPanels.Any(t => IsGameWord(t, InventoryTitle));
        switch (cmd)
        {
            case ControllerInput.Command.Tap:
                // In menus: the next tooltip line (else repeat); in play: where the scanner's choice is.
                if (uiBusy ? !TooltipNext(1) : !_nav.SayChoice(ItemName))
                    if (_lastSpoken.Length > 0) _speech.Speak(_lastSpoken, true);
                if (!uiBusy) TutorialDid("where");
                break;
            case ControllerInput.Command.DoubleTap:
                // In menus: the previous tooltip line; in play: life, mana and the area.
                if (!uiBusy || !TooltipNext(-1)) Guard("status", SayStatus);
                if (!uiBusy) TutorialDid("status");
                break;
            case ControllerInput.Command.Hold:
                // The Fate window (Covens league): the selected card again.
                if (FateWindowText() is string fate) { Say(fate, true); break; }
                // In menus: the whole tooltip.
                // Nothing to read there: the window's controls.
                if (uiBusy && !TooltipAll() && !SayControlsAgain() && _lastSpoken.Length > 0) _speech.Speak(_lastSpoken, true);
                // In play: the game's button prompts for what is around you (pick up, talk...).
                if (!uiBusy && !SayControlsAgain()) Say(T("No button prompts on screen", "No hay indicaciones de botones en pantalla"), true);
                if (!uiBusy) TutorialDid("prompts");
                break;
            case ControllerInput.Command.LongHold:
                // In the inventory: every item in the bag, or every equipment slot when the
                // selection is on the equipment.
                if (inventoryOpen && _lastFocusWasEquipment) Guard("equipment", SayAllEquipment);
                else if (inventoryOpen) Guard("inventory", SayWholeInventory);
                break;
            case ControllerInput.Command.ScanUp:
                if (!uiBusy) { Guard("scan", () => _nav.Scan(-1, 0, now, ItemName)); TutorialDid("category"); }
                else Guard("price", SayPriceAgain);   // in a window: the selected item's price again
                break;
            case ControllerInput.Command.ScanDown:
                if (!uiBusy) { Guard("scan", () => _nav.Scan(1, 0, now, ItemName)); TutorialDid("category"); }
                else Guard("currency", SayCurrency);   // in a window: your gold and currency
                break;
            case ControllerInput.Command.ScanLeft:
                if (!uiBusy) { Guard("scan", () => _nav.Scan(0, -1, now, ItemName)); TutorialDid("thing"); }
                break;
            case ControllerInput.Command.ScanRight:
                if (!uiBusy) { Guard("scan", () => _nav.Scan(0, 1, now, ItemName)); TutorialDid("thing"); }
                break;
            case ControllerInput.Command.Guide:
                _guideCommandMs = now;
                if (!uiBusy) { Guard("guide", () => _nav.ToggleGuide(ItemName)); TutorialDid("guide"); }
                break;
        }
    }

    // Name of an item lying on the ground: "Rare Iron Ring", "Scroll of Wisdom".
    private string ItemName(Entity worldItem)
    {
        var wi = GetComp<ExileCore.PoEMemory.Components.WorldItem>(worldItem);
        Entity item = wi != null ? Try(() => wi.ItemEntity, null) : null;
        if (item == null || item.Address == 0) return "";

        BaseComp baseC = GetComp<BaseComp>(item);
        ModsComp mods = GetComp<ModsComp>(item);
        string baseName = baseC != null ? CleanText(Try(() => baseC.Name, "")) : "";
        ItemRarity rarity = mods != null ? Try(() => mods.ItemRarity, ItemRarity.Normal) : ItemRarity.Normal;
        string unique = mods != null && (rarity == ItemRarity.Unique || rarity == ItemRarity.Rare)
            ? CleanText(Try(() => mods.UniqueName, "")) : "";

        string body = unique.Length > 0 && !string.Equals(unique, baseName, StringComparison.OrdinalIgnoreCase)
            ? unique + ", " + baseName
            : MagicFullName(item, baseName);
        return rarity switch
        {
            ItemRarity.Magic => T($"Magic {body}", $"Mágico, {body}"),
            ItemRarity.Rare => T($"Rare {body}", $"Raro, {body}"),
            ItemRarity.Unique => T($"Unique {body}", $"Único, {body}"),
            _ => body,
        };
    }

    /// <summary>
    /// The name the game shows for an identified magic item: its prefix, the base, its suffix
    /// ("Heavy Rusted Sword of Needling"). Each affix's display name is its part of the name; a
    /// suffix's starts with "of". Anything else (normal, unidentified) keeps the base name.
    /// </summary>
    private string MagicFullName(Entity item, string baseName)
    {
        ModsComp mods = GetComp<ModsComp>(item);
        if (mods == null || baseName.Length == 0) return baseName;
        if (Try(() => mods.ItemRarity, ItemRarity.Normal) != ItemRarity.Magic) return baseName;
        if (!Try(() => mods.Identified, true)) return baseName;
        var list = Try(() => mods.ItemMods, null);
        if (list == null) return baseName;
        string pre = "", suf = "";
        // Gendered affix names take the base item's form ("sana" for "Almilla raída").
        string inflection = Try(() => Inflection(item), null);
        foreach (var m in list)
        {
            string d = CleanText(PickForm(Try(() => m.DisplayName, "") ?? "", inflection));
            if (d.Length == 0) continue;   // implicit mods carry no name
            // An English suffix's name starts with "of"; in other client languages the mod's
            // record says whether it is a prefix or a suffix.
            var affix = Try(() => (ModType?)m.ModRecord?.AffixType, null);
            bool suffix = d.StartsWith("of ", StringComparison.OrdinalIgnoreCase) || affix == ModType.Suffix;
            if (suffix) { if (suf.Length == 0) suf = d; }
            else if (pre.Length == 0) pre = d;
        }
        return MagicNameInOrder(baseName, pre, suf);
    }

    // Life, mana and energy shield, as percentages.
    private void SayStatus()
    {
        var life = GetComp<ExileCore.PoEMemory.Components.Life>(GameController.Player);
        if (life == null) { Say(T("No player data", "Sin datos del jugador"), true); return; }

        static int Pct(int cur, int max) => max > 0 ? (int)Math.Round(100.0 * cur / max) : 0;
        var parts = new List<string>();
        string level = LevelText();
        if (level.Length > 0) parts.Add(level);
        int lifePct = Pct(life.CurHP, life.MaxHP), manaPct = Pct(life.CurMana, life.MaxMana);
        parts.AddRange(new[]
        {
            T($"Life {lifePct} percent", $"Vida {lifePct} por ciento"),
            T($"Mana {manaPct} percent", $"Maná {manaPct} por ciento"),
        });
        if (life.MaxES > 0)
        {
            int esPct = Pct(life.CurES, life.MaxES);
            parts.Add(T($"Energy shield {esPct} percent", $"Escudo de energía {esPct} por ciento"));
        }

        // The flasks' uses left, in slot order: "Small Life Flask, 3 uses".
        ServerData srv = GetServerData();
        if (srv != null)
            foreach (var f in Slots(srv, InventorySlotE.Flask1).OrderBy(s => s.PosX))
            {
                int uses = FlaskUses(f.Item);
                if (uses >= 0)
                {
                    string flaskName = CleanText(Try(() => GetComp<BaseComp>(f.Item)?.Name, "") ?? T("flask", "frasco"));
                    parts.Add(uses == 1 ? T($"{flaskName}, {uses} use", $"{flaskName}, {uses} uso") : T($"{flaskName}, {uses} uses", $"{flaskName}, {uses} usos"));
                }
            }

        int gold = Try(() => GameController.IngameState.ServerData.Gold, -1);
        if (gold > 0) parts.Add(T($"Gold {gold}", $"Oro {gold}"));

        string area = Try(() => GameController.Area.CurrentArea.Name, "") ?? "";
        if (area.Length == 0) area = Try(() => GameController.Area.CurrentArea.DisplayName, "") ?? "";
        int areaLevel = Try(() => GameController.Area.CurrentArea.RealLevel, 0);
        if (area.Length > 0) parts.Add(areaLevel > 0 ? T($"{area}, area level {areaLevel}", $"{area}, nivel de área {areaLevel}") : area);

        bool town = Try(() => GameController.Area.CurrentArea.IsTown || GameController.Area.CurrentArea.IsHideout, false);
        if (!town && _nav != null)
        {
            var explored = _nav.ExploredPercent();
            parts.Add(T($"explored {explored} percent", $"explorado {explored} por ciento"));
        }
        // The area's description after the rest, without its name and level again.
        string described = _nav != null ? Try(() => _nav.DescribeArea(), "") : "";
        int firstStop = described.IndexOf(". ", StringComparison.Ordinal);
        if (firstStop > 0) parts.Add(described.Substring(firstStop + 2));
        Say(JoinSpeech(parts), true);
    }

    // "Level 12, 37 percent of the way to level 13", or "Level 100".
    // The table (Constants.PlayerXpLevels) holds the total experience each level starts at.
    private string LevelText()
    {
        var player = GetComp<ExileCore.PoEMemory.Components.Player>(GameController.Player);
        if (player == null) return "";
        int level = Try(() => player.Level, 0);
        long xp = Try(() => (long)player.XP, -1L);
        if (level <= 0) return "";
        uint[] table = ExileCore.Shared.Constants.PlayerXpLevels;
        if (xp < 0 || table == null || level + 1 >= table.Length) return T($"Level {level}", $"Nivel {level}");
        long start = table[level], next = table[level + 1];
        if (next <= start) return T($"Level {level}", $"Nivel {level}");
        // How far through the level you are, so the number rises as you gain experience
        // (saying what was left made it fall, which read as losing progress).
        double done = Math.Max(0, Math.Min(1, (xp - start) / (double)(next - start)));
        int pct = (int)Math.Floor(100 * done);
        int nextLevel = level + 1;
        return T($"Level {level}, {pct} percent of the way to level {nextLevel}", $"Nivel {level}, {pct} por ciento del camino al nivel {nextLevel}");
    }

    // ---- Life, mana and energy shield: spoken as they fall past 75, 50 and 25 percent ----

    private readonly Dictionary<string, int> _resourceStep = new Dictionary<string, int>();
    private long _nextResourcePollMs;

    private void PollResources(long now)
    {
        if (!Settings.AnnounceResources.Value || now < _nextResourcePollMs) return;
        _nextResourcePollMs = now + 150;
        var life = GetComp<ExileCore.PoEMemory.Components.Life>(GameController.Player);
        if (life == null) return;
        Check("Life", Try(() => life.CurHP, 0), Try(() => life.MaxHP, 0), true);
        Check("Mana", Try(() => life.CurMana, 0), Try(() => life.MaxMana, 0), false);
        Check("Energy shield", Try(() => life.CurES, 0), Try(() => life.MaxES, 0), false);

        void Check(string name, int cur, int max, bool urgent)
        {
            if (max <= 0) { _resourceStep.Remove(name); return; }
            double pct = 100.0 * cur / max;
            int step = pct >= 75 ? 3 : pct >= 50 ? 2 : pct >= 25 ? 1 : 0;   // which quarter it is in
            if (!_resourceStep.TryGetValue(name, out int last)) { _resourceStep[name] = step; return; }
            if (step < last)
            {
                // Fell past one or more lines: say the lowest one crossed.
                _resourceStep[name] = step;
                if (cur > 0 || name != "Life")
                {
                    string spokenName = name == "Life" ? T("Life", "Vida") : name == "Mana" ? T("Mana", "Maná") : T("Energy shield", "Escudo de energía");
                    int below = (step + 1) * 25;
                    SayNews(News.LifeAndMana, T($"{spokenName} below {below} percent", $"{spokenName} por debajo del {below} por ciento"), urgent && step <= 1);
                }
            }
            else if (step > last && pct >= step * 25 + 5)
                _resourceStep[name] = step;   // recovered (5 points past the line, so it does not chatter)
        }
    }

    // Hotkeys are detected here, not with HotkeyNodeV2.PressedOnce(): ExileApi only tracks keys
    // that are registered with its Input class, and the first test showed no hotkey firing.
    // This only reads key state (GetAsyncKeyState); it never sends input.
    private readonly KeyWatcher _keys = new KeyWatcher();

    private bool Pressed(string name, HotkeyNodeV2 node, Keys fallback)
    {
        Keys key = fallback;
        try
        {
            Keys k = node?.Value?.Key ?? Keys.None;
            if (k != Keys.None) key = k;
        }
        catch { }

        if (!_keys.PressedOnce(key)) return false;
        PluginLog.Write("Hotkey " + name + " (" + key + ") pressed.");
        return true;
    }

    private long _f3DownAtMs = -1;
    private bool _f3Held;

    private void HandleHotkeys()
    {
        if (Pressed("repeat", Settings.RepeatHotkey, Keys.F9))
        {
            if (_lastSpoken.Length > 0) _speech.Speak(_lastSpoken, true);
            else Say(T("Nothing to repeat yet", "Nada que repetir todavía"), true);
        }

        if (Pressed("previous item", Settings.PreviousItemHotkey, Keys.F5)) Guard("browse", () => BrowseStep(-1));
        if (Pressed("next item", Settings.NextItemHotkey, Keys.F6)) Guard("browse", () => BrowseStep(1));
        if (Pressed("switch list", Settings.SwitchListHotkey, Keys.F7)) Guard("browse", SwitchList);
        if (Pressed("currency", Settings.CurrencyHotkey, Keys.None)) Guard("currency", SayCurrency);

        if (Pressed("dump items", Settings.DumpItemsHotkey, Keys.F8)) Guard("dump items", DumpItems);
        if (Pressed("dump panels", Settings.DumpPanelsHotkey, Keys.F10)) Guard("dump panels", DumpPanels);
        if (Pressed("dump hover", Settings.DumpHoverHotkey, Keys.F11)) Guard("dump hover", DumpHovered);
        // F3: hold a second for the mod's settings menu (Poe2Access); a short press is the text
        // probe (developer). A different key chosen for the probe works as before.
        Keys probeKey = Try(() => Settings.TextProbeHotkey.Value.Key, Keys.F3);
        if (probeKey == Keys.None) probeKey = Keys.F3;
        if (probeKey != Keys.F3 && Pressed("text probe", Settings.TextProbeHotkey, Keys.F3)) Guard("text probe", RunTextProbe);
        bool f3 = _keys.IsDown(Keys.F3);
        long nowMs = _clock.ElapsedMilliseconds;
        if (f3 && _f3DownAtMs < 0) { _f3DownAtMs = nowMs; _f3Held = false; }
        if (f3 && !_f3Held && nowMs - _f3DownAtMs >= 1000)
        {
            _f3Held = true;
            if (_lastInGame == true) Guard("menu", () => { if (MenuOpen) CloseMenu(); else OpenMenu(); });
        }
        if (!f3 && _f3DownAtMs >= 0)
        {
            if (!_f3Held && nowMs - _f3DownAtMs < 600 && probeKey == Keys.F3)
            {
                PluginLog.Write("Hotkey text probe (F3) pressed.");
                Guard("text probe", RunTextProbe);
            }
            _f3DownAtMs = -1;
        }
        AudioEngine.Master = Settings.MasterVolume.Value / 100f;
        ApplySpeechRate(false);
        if (Pressed("focus snapshot", Settings.FocusProbeHotkey, Keys.F2)) Guard("focus snapshot", StartFocusSnapshot);
        if (_focusPendingSnap != null && _clock.ElapsedMilliseconds >= _focusPendingDueMs) Guard("focus snapshot", FinishFocusSnapshot);

        if (Pressed("change logging", Settings.ChangeLogHotkey, Keys.F4)) Guard("change logging", ToggleChangeLogging);
        if (_changeLogger != null && _clock.ElapsedMilliseconds >= _nextChangePollMs)
        {
            _nextChangePollMs = _clock.ElapsedMilliseconds + 300;
            // With a settings row selected, watch only that row, 4 KB per element (values live
            // past the first 1 KB); otherwise the whole visible UI, 1 KB per element.
            // On a pre-game screen also watch that screen's own data (not only its menu), and
            // note the class shown in the class info box at the time.
            if (_preGameState.Length > 0)
            {
                var (stType, stAddr) = TopPreGameState();
                string note = _classInfo.Length > 0 ? "class shown: " + _classInfo.Split('.')[0] : "";
                Guard("change logging", () => _changeLogger.PollRegion(Mem, stType, stAddr, 0x2000, note));
                Guard("change logging", () => _changeLogger.PollRegionHop(Mem, stType, stAddr, 0x800, 0x400, note));
            }

            Element row = _lastFocusElement != null ? FindRow(_lastFocusElement) : null;
            if (row != null && row.Address != 0 && Try(() => UiText.Visible(Mem, row), false))
                Guard("change logging", () => _changeLogger.Poll(Mem, row, DescribeFocused, 0x1000));
            else
                Guard("change logging", () => _changeLogger.Poll(Mem, ProbeRoot(), DescribeFocused));
        }
    }

    // F4: change logger on/off (developer).
    private ChangeLogger _changeLogger;
    private long _nextChangePollMs;

    private void ToggleChangeLogging()
    {
        if (_changeLogger == null)
        {
            _changeLogger = new ChangeLogger();
            _changeLogger.Start();
            Say("Change logging on. Wait one second, then use the menu.", true);
        }
        else
        {
            _changeLogger.Stop();
            _changeLogger = null;
            Say("Change logging off. Saved.", true);
        }
    }

    // F2: focus probe. Press, move the menu selection one step, press again, and so on.
    private FocusProbe.Snapshot _focusPendingSnap;
    private long _focusPendingDueMs;
    private FocusProbe.Snapshot _focusPrevSnap;
    private Dictionary<long, HashSet<int>> _focusPrevNoise;
    private int _focusNumber;

    private void StartFocusSnapshot()
    {
        if (_focusPendingSnap != null) return;
        _focusPendingSnap = FocusProbe.Take(Mem, ProbeRoot());
        _focusPendingDueMs = _clock.ElapsedMilliseconds + 300;
    }

    private void FinishFocusSnapshot()
    {
        FocusProbe.Snapshot first = _focusPendingSnap;
        _focusPendingSnap = null;

        // Second snapshot with no input: whatever changed in between changes by itself.
        FocusProbe.Snapshot second = FocusProbe.Take(Mem, ProbeRoot());
        var noise = FocusProbe.Noise(first, second);

        _focusNumber++;
        string report = FocusProbe.Report(Mem, ProbeRoot(), _focusNumber, _focusPrevSnap, second, _focusPrevNoise, noise);
        WriteDump("focus" + _focusNumber, new StringBuilder(report));

        _focusPrevSnap = second;
        _focusPrevNoise = noise;
        Say("Focus snapshot " + _focusNumber + " saved. Move the selection one step, then press F2 again.", true);
    }

    // F3: search the hovered element, its tooltip and their neighbours for where the game
    // really stores UI text. Hover an inventory item first: its name is then known, and the
    // probe marks the offsets that contain it.
    private void RunTextProbe()
    {
        IngameState state = GameController?.IngameState;
        if (state == null) { Say("No game state available", true); return; }

        Element hover = state.UIHover;
        Element tip = state.UIHoverTooltip;

        string expected = "";
        if (IsUsable(hover) && Try(() => UiText.TypeOf(Mem, hover), ElementType.MiscGroundLabel) == ElementType.InventoryItem)
        {
            Entity item = Try(() => hover.AsObject<NormalInventoryItem>()?.Item, null);
            if (item != null && item.Address != 0)
            {
                BaseComp baseC = GetComp<BaseComp>(item);
                expected = baseC != null ? CleanText(Try(() => baseC.Name, "")) : "";
            }
        }

        var roots = new List<(string, Element)>();
        if (IsUsable(hover)) roots.Add(("hover", hover));
        if (IsUsable(tip)) roots.Add(("tooltip", tip));

        Say("Probing text, the game may pause for a few seconds", true);
        var sw = Stopwatch.StartNew();

        int candidates = 0;
        var sb = new StringBuilder();
        if (roots.Count > 0)
            sb.Append(TextProbe.Run(Mem, roots, expected, out candidates));
        else
            sb.AppendLine("PoE1Access text probe " + DateTime.Now + "\n(nothing hovered)");

        Element uiRoot = ProbeRoot();
        sb.Append(TextProbe.RunGlobal(Mem, uiRoot, IsUsable(hover) ? hover : null, expected, 20000));
        // Deep scan of the selected settings row, 4 KB per element: control values (dropdown
        // choice, slider position, checkbox state) live past the first 1 KB.
        Element top = _lastFocusElement != null ? (FindRow(_lastFocusElement) ?? _lastFocusElement) : null;
        sb.Append(TextProbe.RunDeep(Mem, top, _lastFocusAddr, 400, 0x1000, 0x200));

        // Character creation's class screen: the class name and description are not plain text;
        // search two pointer hops from those elements for where their text really is.
        if (uiRoot != null)
        {
            var named = new Stack<Element>();
            named.Push(uiRoot);
            int seen = 0;
            while (named.Count > 0 && seen++ < 2000)
            {
                Element x = named.Pop();
                string n = UiText.Name(Mem, x);
                if (n == "class_name" || n == "class_description")
                    sb.Append(TextProbe.RunTwoHop(Mem, x, n));
                IList<Element> kids = Try<IList<Element>>(() => x.Children, null);
                if (kids != null && kids.Count < 300) foreach (var k in kids) named.Push(k);
            }
        }

        // Two-hop string search from the selected row's control (for dropdown choice names).
        Element row = _lastFocusElement != null ? FindRow(_lastFocusElement) : null;
        if (row != null)
        {
            Element control = Try<Element>(() => row.GetChildAtIndex(1)?.GetChildAtIndex(0)?.GetChildAtIndex(0), null);
            string label = CleanText(FirstText(Try<Element>(() => row.GetChildAtIndex(0), null), 5, null));
            sb.Append(TextProbe.RunTwoHop(Mem, control, label));
        }

        sb.AppendLine();
        sb.AppendLine("Probe time: " + sw.ElapsedMilliseconds + " ms");

        WriteDump("textprobe", sb);
        Say("Text probe saved" + (expected.Length > 0 ? ", looking for " + expected : "") + ".", true);
    }

    // ===================================================================
    // Panel announcements
    // ===================================================================

    private static (string Label, Func<Element> Get)[] PanelTable(IngameUIElements ui)
    {
        return new (string, Func<Element>)[]
        {
            ("Inventory", () => ui.InventoryPanel),
            ("Passive tree", () => ui.TreePanel),
            ("Atlas passive tree", () => ui.AtlasTreePanel),
            ("World map", () => ui.WorldMap),
            ("Stash", () => ui.StashElement),
            ("Vendor", () => ui.SellWindow),
            ("Shop", () => ui.PurchaseWindow),
            ("Trade", () => ui.TradeWindow),
            ("Dialogue", () => ui.NpcDialog),
            ("Quest reward", () => ui.QuestRewardWindow),
            ("Crafting bench", () => ui.CraftBench),
            ("Map device", () => ui.MapDeviceWindow),
            ("Gem level up", () => ui.GemLvlUpPanel),
            ("Options", () => ui.SettingsPanel),
        };
    }

    // Panels are found by the title the game stores in them (UiText.TitleOffset), not by
    // ExileApi's named panel pointers, which point at the wrong panels on this game version.
    private HashSet<string> _openPanels;
    private const int PanelScanDepth = 5;
    private const int PanelScanMaxElements = 2500;

    private Element UiRoot()
    {
        // ExileApi's InGame can be stale once it stops ticking, so ask the game states too.
        bool preGame = !Try(() => GameController.InGame, false) ||
                       !Try(() => Game.IsInGameState, true) ||
                       Try(() => Game.IsSelectCharacterState, false) ||
                       Try(() => Game.IsLoginState, false);
        if (preGame)
        {
            // The options window opened from the login screen lies in a tree of its own (PreGameOptions.cs).
            Element overlay = PreGameOverlayRoot();
            if (overlay != null) return overlay;
            Element pre = PreGameRoot();
            if (pre != null) return pre;
        }
        IngameState state = GameController?.IngameState;
        if (state == null) return null;
        Element root = Try<Element>(() => state.UIRoot, null);
        if (root == null || root.Address == 0) root = Try<Element>(() => state.IngameUi, null);
        if (root != null && root.Address != 0) UiText.IsRoot(Mem, root);   // learns a patch's field shift
        return root;
    }

    // ===================================================================
    // Pause (escape) menu: a game state of its own with its own UI tree
    // ===================================================================

    // What the developer probes (F2, F3, F4) look at: the pause menu when it is open.
    private Element ProbeRoot() => EscapeRoot() ?? UiRoot();

    // The game can leave its pause-menu state in the state list after the menu closes (seen
    // 18 Nov 2026: InGameState, EscapeState, LoadingState listed while playing, the menu's root
    // hidden and nameless). Then the mod thought the menu was open and stayed silent. So the
    // menu counts as open only while its root is a visible "root" element (or, for the rare
    // root found by search, that one is visible).
    private long _escCheckedMs = -1000;
    private bool _escCheckResult;

    private bool EscapeOpen()
    {
        long now = _clock.ElapsedMilliseconds;
        if (now - _escCheckedMs < 200) return _escCheckResult;
        _escCheckedMs = now;
        _escCheckResult = EscapeOpenNow();
        return _escCheckResult;
    }

    private bool EscapeOpenNow()
    {
        bool listed = Try(() => Game.IsEscapeState, false) || Try(() => Game.EscapeState.IsActive, false);
        if (!listed) return false;
        // After the menu closes its root stays "shown" but holds nothing (seen 19 Nov 2026), so
        // the menu is open only while the root is shown and has shown contents.
        bool Showing(Element e) =>
            e != null && e.Address != 0 && Try(() => UiText.Visible(Mem, e), false) &&
            Try(() => e.Children.Any(k => UiText.VisibleLocal(Mem, k)), false);
        Element r = Try<Element>(() => Game.EscapeState.UIRoot, null);
        if (r != null && r.Address != 0 && UiText.IsRoot(Mem, r)) return Showing(r);
        Element f = _escFallbackRoot ?? Try(() => FindStateRoot(Game.EscapeState.Address), null);
        return Showing(f);
    }

    // The game rebuilds the pause menu each time it opens, so its root is read fresh on every
    // call (a kept copy went stale and made the reader silent). Only the slow fallback search
    // is kept, and only until the menu closes.
    private Element _escFallbackRoot;
    private long _escLoggedRoot;

    // The pause menu's UI root while it is open, else null.
    private Element EscapeRoot()
    {
        if (!EscapeOpen()) return null;
        try
        {
            var es = Game.EscapeState;
            if (es == null || es.Address == 0) return null;

            Element r = Try<Element>(() => es.UIRoot, null);
            if (r == null || r.Address == 0 || !UiText.IsRoot(Mem, r))
                r = _escFallbackRoot ??= FindStateRoot(es.Address);

            long addr = r?.Address ?? 0;
            if (addr != _escLoggedRoot)
            {
                _escLoggedRoot = addr;
                PluginLog.Write("Pause menu UI root: " + (r == null ? "not found" : "0x" + addr.ToString("X")));
            }
            return r;
        }
        catch
        {
            return null;
        }
    }

    private bool _escWasOpen;
    private long _initialFocusPending;   // when a menu opened (0 = nothing pending)

    // Announces the pause menu opening and closing.
    private void PollEscape()
    {
        bool open = EscapeOpen();
        if (open == _escWasOpen) return;
        _escWasOpen = open;
        _focusState = null;   // a different tree: start comparing afresh
        _escFallbackRoot = null;
        _escLoggedRoot = 0;
        _initialFocusPending = open ? _clock.ElapsedMilliseconds : 0;
        PluginLog.Write("Pause menu " + (open ? "open" : "closed"));
        Say(open ? T("Game menu open", "Menú del juego abierto") : T("Game menu closed", "Menú del juego cerrado"), true);
    }

    // ===================================================================
    // Pre-game screens (login, character selection, character creation)
    // ===================================================================

    // The game states that are screens before the game (StateNames' spoken ones).
    private static readonly HashSet<string> PreGameScreens = new HashSet<string>
    {
        "LoginState", "SelectCharacterState", "CreateCharacterState", "DeleteCharacterState", "ChangePasswordState", "CreditsState",
    };

    private static readonly Dictionary<string, string> StateNames = new Dictionary<string, string>
    {
        { "LoginState", "Login screen" },
        { "SelectCharacterState", "Character selection" },
        { "CreateCharacterState", "Character creation" },
        { "DeleteCharacterState", "Delete character" },
        { "ChangePasswordState", "Change password" },
        { "CreditsState", "Credits" },
        { "PreGameState", "" },
        { "WaitingState", "" },
        { "AreaLoadingState", "" },
        { "LoadingState", "" },
        { "EscapeState", "" },
        { "InGameState", "" },
    };

    // The spoken name of a pre-game screen, from its English name in StateNames (the dictionary
    // stays English: the empty names are compared).
    private static string StateSpoken(string en)
    {
        switch (en)
        {
            case "Login screen": return T("Login screen", "Pantalla de inicio de sesión");
            case "Character selection": return T("Character selection", "Selección de personaje");
            case "Character creation": return T("Character creation", "Creación de personaje");
            case "Delete character": return T("Delete character", "Eliminar personaje");
            case "Change password": return T("Change password", "Cambiar contraseña");
            case "Credits": return T("Credits", "Créditos");
        }
        return en;
    }

    private string _preGameState = "";
    private long _preGameStateAddr;
    private Element _preGameRoot;
    private long _preGameRootFoundMs;

    // The active pre-game state, top-most first: (type name, address).
    private (string Type, long Addr) TopPreGameState()
    {
        var game = Game;
        if (game == null) return ("", 0);

        var byAddr = new Dictionary<long, string>();
        try
        {
            foreach (var kv in game.AllGameStates)
                if (kv.Value != 0) byAddr[kv.Value] = kv.Key.ToString();
        }
        catch { }

        IList<GameState> active = Try<IList<GameState>>(() => game.CurrentGameStates, null) ??
                                  Try<IList<GameState>>(() => game.ActiveGameStates, null);
        if (active == null) return ("", 0);

        // The last state with a screen of its own is the one showing. The game leaves character
        // creation in the list when you go back from it, even after logging out ("PreGameState,
        // LoginState, CreateCharacterState" on the login screen, found 20 Nov 2026), so a screen
        // whose UI root is hidden or gone (the left-behind creation state has none) is passed over
        // while another screen lies below it. The login screen keeps its root elsewhere and is
        // never left behind.
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var st = active[i];
            if (st == null || st.Address == 0) continue;
            string type = byAddr.TryGetValue(st.Address, out string t) ? t : "Unknown";
            if (StateNames.TryGetValue(type, out string spoken) && spoken.Length == 0) continue;
            if (PreGameScreens.Contains(type) && type != "LoginState" && !StateUiShown(st.Address) &&
                active.Take(i).Any(b => b != null && byAddr.TryGetValue(b.Address, out string bt) && PreGameScreens.Contains(bt)))
                continue;
            return (type, st.Address);
        }
        return ("", 0);
    }

    // Whether a screen's UI is shown: character selection and creation keep their UI root at
    // state +0x218, and it is visible. A screen left behind has no root there, while its other
    // pointers may lead to stray elements of other screens (seen 20 Nov 2026), so only +0x218
    // counts. Checked at most twice a second per state.
    private const int ScreenRootOffset = 0x218;
    private readonly Dictionary<long, (long AtMs, bool Shown)> _stateUiShown = new Dictionary<long, (long, bool)>();

    private bool StateUiShown(long stateAddr)
    {
        long now = _clock.ElapsedMilliseconds;
        if (_stateUiShown.TryGetValue(stateAddr, out var c) && now - c.AtMs < 500) return c.Shown;
        bool shown = false;
        long p = Try(() => Mem.Read<long>(stateAddr + ScreenRootOffset), 0L);
        if (p >= 0x10000 && p <= 0x7FFF_FFFF_FFFF)
        {
            Element e = Try<Element>(() => Game.GetObject<Element>(p), null);
            if (e != null && LooksLikeElement(e, out _)) shown = Try(() => UiText.VisibleLocal(Mem, e), false);
        }
        _stateUiShown[stateAddr] = (now, shown);
        return shown;
    }

    // A state's UI root: the pointer in the state's first bytes that leads to an element named
    // "root" (the same internal name the in-game UI root has).
    // A real UI element: it has children, and its first child points back to it as parent.
    private bool LooksLikeElement(Element e, out long kids)
    {
        kids = 0;
        if (e == null || e.Address == 0) return false;
        long k = Try(() => e.ChildCount, 0L);
        kids = k;
        if (k < 1 || k > 500) return false;
        Element first = Try<Element>(() => e.GetChildAtIndex(0), null);
        Element back = first != null ? UiText.ParentOf(Mem, first) : null;
        return back != null && back.Address == e.Address;
    }

    // A state's UI root: a pointer in the state's first bytes to an element named "root" (as the
    // in-game UI root is), else to the first thing that is clearly a UI element with children.
    // Every candidate is logged once, to learn each screen's layout.
    private Element FindStateRoot(long stateAddr, bool log)
    {
        byte[] b = TextProbe.ReadUpTo(Mem, stateAddr, 0x800);
        if (b == null) return null;
        Element firstCandidate = null;
        string firstAt = "";
        for (int o = 0; o + 8 <= b.Length; o += 8)
        {
            long p = BitConverter.ToInt64(b, o);
            if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF || p == stateAddr) continue;
            Element e = Try<Element>(() => Game.GetObject<Element>(p), null);
            if (e == null) continue;
            string name = UiText.Name(Mem, e);
            if (name == "root" || UiText.IsRoot(Mem, e))
            {
                PluginLog.Write("Pre-game UI root (named root) at state+0x" + o.ToString("X") + " -> 0x" + p.ToString("X"));
                return e;
            }
            if (LooksLikeElement(e, out long kids))
            {
                if (log) PluginLog.Write("  candidate at state+0x" + o.ToString("X") + " -> 0x" + p.ToString("X") +
                                         " name=\"" + name + "\" children=" + kids +
                                         " visible=" + Try(() => UiText.VisibleLocal(Mem, e), false));
                if (firstCandidate == null) { firstCandidate = e; firstAt = "state+0x" + o.ToString("X"); }
            }
        }
        if (firstCandidate != null && log)
            PluginLog.Write("Pre-game UI root (first element) at " + firstAt + " -> 0x" + firstCandidate.Address.ToString("X"));
        return firstCandidate;
    }

    // Overload kept for the pause menu fallback.
    private Element FindStateRoot(long stateAddr) => FindStateRoot(stateAddr, false);

    private Element PreGameRoot()
    {
        var (type, addr) = TopPreGameState();
        if (addr == 0) return null;
        // The game rebuilds these screens each time they open, so a kept root goes stale: it is
        // looked up again every second (cheap), and the log notes when it changes.
        long nowMs = _clock.ElapsedMilliseconds;
        if (_preGameRoot != null && addr == _preGameStateAddr && nowMs - _preGameRootFoundMs >= 1000)
        {
            long old = _preGameRoot.Address;
            _preGameRootFoundMs = nowMs;
            Element fresh = null;
            if (type == "LoginState" || type == "SelectCharacterState")
            {
                fresh = Try<Element>(() => Game.LoginState.UIRoot, null);
                if (fresh != null && !LooksLikeElement(fresh, out _)) fresh = null;
            }
            fresh ??= FindStateRoot(addr, false);
            if (fresh != null && fresh.Address != old)
            {
                PluginLog.Write("Pre-game UI root changed -> 0x" + fresh.Address.ToString("X"));
                _focusState = null;
            }
            if (fresh != null) _preGameRoot = fresh;
            return _preGameRoot;
        }

        if (addr != _preGameStateAddr || _preGameRoot == null)
        {
            long now = nowMs;
            if (addr == _preGameStateAddr && now - _preGameRootFoundMs < 1000) return _preGameRoot;
            bool firstTry = addr != _preGameStateAddr;
            _preGameStateAddr = addr;
            _preGameRootFoundMs = now;

            // The login state (which in PoE1 also shows character selection) has a known root.
            Element r = null;
            if (type == "LoginState")
            {
                r = Try<Element>(() => Game.LoginState.UIRoot, null);
                if (r != null && !LooksLikeElement(r, out _)) r = null;
                if (r != null && firstTry)
                    PluginLog.Write("Pre-game UI root from LoginState.UIRoot -> 0x" + r.Address.ToString("X") +
                                    " name=\"" + UiText.Name(Mem, r) + "\"");
            }
            _preGameRoot = r ?? FindStateRoot(addr, firstTry);
            if (_preGameRoot == null && firstTry) PluginLog.Write("No UI root found for " + type);
        }
        return _preGameRoot;
    }

    private long _preGameStateSinceMs;
    private long _lastInGameMs = -100000;   // last time the game was in gameplay
    private bool _preGameAnnounced;
    private bool _loginSeen;
    private long _charListPendingMs;

    private void PreGameTick(long now)
    {
        var (type, _) = TopPreGameState();
        if (type != _preGameState)
        {
            _preGameState = type;
            _preGameStateSinceMs = now;
            _preGameRoot = null;          // a new screen: find its root afresh
            _preGameStateAddr = 0;
            _screenTitle = "";
            _classInfo = "";
            _nameScreen = "";
            _classScreenLine = "";
            _classHintGiven = false;
            _preGameAnnounced = false;
            _openPanels = null;
            _focusState = null;
            _initialFocusPending = type.Length > 0 ? now : 0;
            _charListPendingMs = type == "SelectCharacterState" ? now : 0;
            PluginLog.Write("Game state: " + (type.Length > 0 ? type : "(none)"));
        }
        if (type.Length == 0) return;

        // Leaving the game, it passes through the login state for a few seconds on the way to
        // character selection, so then "Login screen" is only said once that screen has stayed
        // up 6 seconds. At the game's start it is said straight away.
        if (!_preGameAnnounced)
        {
            string spoken = StateNames.TryGetValue(type, out string s) ? StateSpoken(s) : type;
            bool leavingGame = now - _lastInGameMs < 10000;
            long wait = type == "LoginState" && leavingGame ? 6000 : 0;
            // The game passes through the change password state for a few seconds as it starts,
            // before the login screen; only one reached from the login screen is said.
            if (type == "LoginState") _loginSeen = true;
            if (type == "ChangePasswordState" && !_loginSeen) return;
            if (now - _preGameStateSinceMs >= wait)
            {
                _preGameAnnounced = true;
                if (spoken.Length > 0) Say(spoken, true);
            }
            else return;   // nothing else is read until the screen is announced
        }

        if (_charListPendingMs != 0) Guard("character list", () => AnnounceCharacterList(now));
        Guard("screen title", () => PollScreenTitle(now));
        Guard("class outline", () => PollClassOutline(now));


        // The class screen's outlined Exile is read from the scene (ClassScreen.cs). Also known:
        // CreateCharacterState +0x240 is non-zero while a class is picked, and [state +0x2E0]
        // +0x40 is the picked class number.
        if (Settings.AnnouncePanels.Value) PollPanels(now);
        if (Settings.ReadFocus.Value) PollFocus(now);
        // The screen's button prompts (character creation, selection, login): said as they change.
        Guard("button legend", () => PollButtonLegend(now, true));
        Guard("screen messages", () => PollScreenMessages(now));
    }

    // Pre-game screens carry their instructions in an element named "title_frame" ("Creation.
    // Choose a league by clicking on a banner below, then click OK."). Said when it changes.
    private string _screenTitle = "";
    private long _nextTitlePollMs;

    private void PollScreenTitle(long now)
    {
        if (now < _nextTitlePollMs) return;
        _nextTitlePollMs = now + 500;

        Element root = UiRoot();
        if (root == null) return;
        var mem = Mem;
        string text = "", classText = "", nameScreen = "", classScreen = "";
        IList<Element> top = Try<IList<Element>>(() => root.Children, null);
        if (top != null)
            foreach (Element e in top)
            {
                string name = UiText.Name(mem, e);
                if (!Try(() => UiText.VisibleLocal(Mem, e), false)) continue;
                if (name == "exit_frame")
                {
                    // Class screen: "Choose a character class by clicking on an Exile."
                    Element label = FindNamed(e, "label", 2);
                    if (label != null) classScreen = CleanText(UiText.Text(mem, label));
                    continue;
                }
                if (name == "create_character_frame")
                {
                    // "Enter the name of your Templar. Change your class by clicking on another Exile."
                    Element label = FindNamed(e, "label", 4);
                    if (label != null)
                    {
                        var lp = new List<string>();
                        CollectText(label, 2, lp);
                        nameScreen = JoinSpeech(lp);
                    }
                    continue;
                }
                if (name != "title_frame" && name != "class_info") continue;
                var parts = new List<string>();
                CollectText(e, 4, parts);
                if (name == "title_frame") text = JoinSpeech(parts);
                else classText = JoinSpeech(parts);
            }

        // The class screen's own line, plus how to browse: the classes are characters in the
        // scene, and the game only says which one it is once it is picked (picking is harmless).
        if (classScreen != _classScreenLine)
        {
            _classScreenLine = classScreen;
            if (classScreen.Length > 0 && (classScreen.IndexOf("class", StringComparison.OrdinalIgnoreCase) >= 0 || !EnglishClient))
            {
                string hint = classScreen + " " + ClassBrowseHint;
                PluginLog.Write("Class screen: " + hint);
                Say(hint, false);
            }
        }

        if (nameScreen != _nameScreen)
        {
            _nameScreen = nameScreen;
            if (nameScreen.Length > 0)
            {
                PluginLog.Write("Name screen: " + nameScreen);
                Say(nameScreen, false);
            }
        }

        if (text != _screenTitle)
        {
            _screenTitle = text;
            if (text.Length > 0)
            {
                PluginLog.Write("Screen title: " + text);
                Say(text, false);
            }
        }

        // Class screen: the class you pick fills "class_info" with its name and description.
        if (classText != _classInfo)
        {
            _classInfo = classText;
            if (classText.Length > 0)
            {
                // After the first class picked on this visit, remind once how to try another.
                string spoken = classText;
                if (!_classHintGiven)
                {
                    _classHintGiven = true;
                    spoken += " " + ClassBackHint;
                }
                PluginLog.Write("Class: " + classText);
                Say(spoken, true);
            }
        }
    }

    private string _classInfo = "";
    private string _nameScreen = "";
    private string _classScreenLine = "";
    private bool _classHintGiven;

    private static string ClassBrowseHint =>
        T("The classes are Exiles standing in front of you. Move the left stick to hear each one, and select it to hear its story. Nothing is created until you enter a name, so choose back to try another.",
          "Las clases son exiliados que están frente a ti. Mueve el stick izquierdo para oír cada una y selecciónala para oír su historia. No se crea nada hasta que introduzcas un nombre, así que elige volver para probar otra.");
    private static string ClassBackHint => T("Choose back to try another class.", "Elige volver para probar otra clase.");

    // First visible descendant with this internal name, up to `depth` levels down.
    private Element FindNamed(Element e, string name, int depth)
    {
        if (e == null || depth < 0) return null;
        IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
        if (kids == null || kids.Count > 100) return null;
        foreach (Element k in kids)
        {
            if (!Try(() => UiText.VisibleLocal(Mem, k), false)) continue;
            if (UiText.Name(Mem, k) == name) return k;
            Element r = FindNamed(k, name, depth - 1);
            if (r != null) return r;
        }
        return null;
    }

    // Character selection: the entries in the character list carry the character's name at
    // +0x338 (and the league at +0x2F8); their children hold "Level 3 Templar", the league...
    // Said once on arrival: "1 character: rioggggv, Level 3 Templar, Standard League".
    private void AnnounceCharacterList(long now)
    {
        Element root = UiRoot();
        if (root == null)
        {
            if (now - _charListPendingMs > 3000) _charListPendingMs = 0;
            return;
        }

        var mem = Mem;
        var entries = new List<string>();
        var stack = new Stack<(Element E, int Depth)>();
        stack.Push((root, 0));
        int visited = 0;
        while (stack.Count > 0 && visited++ < 3000)
        {
            var (e, depth) = stack.Pop();
            if (e == null || e.Address == 0) continue;
            if (depth > 0 && !Try(() => UiText.VisibleLocal(Mem, e), false)) continue;

            string charName = UiText.Read(mem, e, 0x338 + UiText.Shift);
            if (charName.Length > 0 && Try(() => e.ChildCount, 0L) > 0)
            {
                var parts = new List<string>();
                CollectText(e, 4, parts);
                if (parts.Count == 0) parts.Add(charName);
                entries.Add(JoinSpeech(parts));
                continue;
            }

            IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
            if (kids == null || kids.Count > 500 || depth >= 12) continue;
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], depth + 1));
        }

        if (entries.Count == 0)
        {
            if (now - _charListPendingMs > 3000) { _charListPendingMs = 0; PluginLog.Write("Character list: none found"); }
            return;
        }
        _charListPendingMs = 0;
        int characterCount = entries.Count;
        string characterList = string.Join(". ", entries.Take(10));
        string text = characterCount == 1
            ? T($"{characterCount} character: {characterList}", $"{characterCount} personaje: {characterList}")
            : T($"{characterCount} characters: {characterList}", $"{characterCount} personajes: {characterList}");
        PluginLog.Write("Character list: " + text);
        Say(text, false);
    }

    // Titles of visible panels, with the element each was found on.
    private List<(string Title, Element E, string Path)> FindVisiblePanels()
    {
        var found = new List<(string, Element, string)>();
        Element root = UiRoot();
        if (root == null || root.Address == 0) return found;

        var mem = Mem;
        var stack = new Stack<(Element E, int Depth, string Path)>();
        stack.Push((root, 0, "root"));
        Element esc = EscapeRoot();
        if (esc != null) stack.Push((esc, 0, "esc"));
        int visited = 0;
        while (stack.Count > 0 && visited < PanelScanMaxElements)
        {
            var (e, depth, path) = stack.Pop();
            visited++;
            if (e == null || e.Address == 0) continue;
            if (depth > 0 && !Try(() => UiText.VisibleLocal(Mem, e), false)) continue;

            string title = CleanText(UiText.Title(mem, e));
            // Dialog windows (Options) carry their title in a child named "label_title".
            if (title.Length == 0 && depth >= 2 && UiText.Name(mem, e) == "label_title")
                title = CleanText(UiText.Text(mem, e));
            if (title.Length > 0) found.Add((title, e, path));

            if (depth >= PanelScanDepth) continue;
            IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
            if (kids == null || kids.Count > 500) continue;
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], depth + 1, path + "/" + i));
        }
        return found;
    }

    private void PollPanels(long now)
    {
        if (now < _nextPanelPollMs) return;
        _nextPanelPollMs = now + 400;

        var visible = FindVisiblePanels();
        _visiblePanels = visible;
        _inventoryPanel = visible.FirstOrDefault(p => IsGameWord(p.Title, InventoryTitle)).E;
        _characterPanel = visible.FirstOrDefault(p => IsGameWord(p.Title, CharacterTitle)).E;
        _skillsPanel = visible.FirstOrDefault(p => IsGameWord(p.Title, SkillsTitle)).E;
        _worldPanel = visible.FirstOrDefault(p => IsGameWord(p.Title, WorldTitle)).E;
        var titles = new HashSet<string>(visible.Select(p => p.Title), StringComparer.OrdinalIgnoreCase);

        // The first reading is silent, so nothing is announced at start-up.
        if (_openPanels != null)
        {
            var opened = titles.Where(t => !_openPanels.Contains(t)).ToList();
            var closed = _openPanels.Where(t => !titles.Contains(t)).ToList();
            var parts = new List<string>();
            if (opened.Count > 0 || closed.Count > 0) _panelsChangedMs = _clock.ElapsedMilliseconds;
            foreach (string t in closed) parts.Add(T($"{t} closed", $"Se cierra {t}"));
            foreach (string t in opened) parts.Add(T($"{t} open", $"Se abre {t}"));
            // Opening the inventory with a build followed: how the worn gear stands (BuildGear.cs).
            if (_build != null && opened.Any(t => IsGameWord(t, InventoryTitle)))
            {
                string gear = Try(BuildGearSummary, "");
                if (gear.Length > 0) parts.Add(gear);
            }
            if (parts.Count > 0)
            {
                PluginLog.Write("Panels: " + string.Join(", ", parts));
                Say(JoinSpeech(parts), true);
            }
        }
        _openPanels = titles;
    }

    // ===================================================================
    // Focus reader (controller / keyboard menu selection)
    // ===================================================================

    // ExileApi's UIHover stays empty when the selection is moved with a controller, so the
    // selected control is found by its focus flag (UiText.FocusFlagOffset) instead.
    private Dictionary<long, (bool Flag, int Sel, bool Lit)> _focusState;
    private long _nextFocusPollMs;
    private long _lastFocusAddr;
    private Element _lastFocusElement;
    private const int FocusScanMaxElements = 6000;
    private long _focusCapLoggedMs, _panelsChangedMs;
    private readonly Dictionary<string, long> _gameMessageSaid = new Dictionary<string, long>();
    private const int TabSelectedValue = 0x100;

    private void PollFocus(long now)
    {
        if (now < _nextFocusPollMs) return;
        _nextFocusPollMs = now + 120;

        CheckValueChange();

        Element root = UiRoot();
        if (root == null || root.Address == 0) return;
        var mem = Mem;

        var state = new Dictionary<long, (bool Flag, int Sel, bool Lit)>();
        var elements = new Dictionary<long, (Element E, string Path)>();
        var stack = new Stack<(Element E, string Path, int Depth)>();
        stack.Push((root, "root", 0));
        // The pause (escape) menu is a separate UI tree; walk it first when it is open.
        Element esc = EscapeRoot();
        if (esc != null) stack.Push((esc, "esc", 0));
        while (stack.Count > 0 && state.Count < FocusScanMaxElements)
        {
            var (e, path, depth) = stack.Pop();
            if (e == null || e.Address == 0 || state.ContainsKey(e.Address)) continue;
            if (depth > 0 && !Try(() => UiText.VisibleLocal(Mem, e), false)) continue;

            byte[] block = UiText.ReadSelectionBlock(mem, e);
            state[e.Address] = (UiText.FocusFlag(block), UiText.Selection(block), UiText.Highlighted(mem, e));
            elements[e.Address] = (e, path);

            IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
            if (kids == null || kids.Count > 3000) continue;
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], path + "/" + i, depth + 1));
        }

        // A menu or screen just opened: say what is selected in it from the start.
        if (_initialFocusPending != 0)
        {
            bool inEscape = _escWasOpen;
            var flagged = state.Where(kv => kv.Value.Flag && (!inEscape || elements[kv.Key].Path.StartsWith("esc")))
                               .Select(kv => kv.Key).ToList();
            // No control has the flag: a highlighted element with text (the character entry
            // character selection starts on).
            if (flagged.Count == 0)
                flagged = state.Where(kv => kv.Value.Lit && (!inEscape || elements[kv.Key].Path.StartsWith("esc")) &&
                                            UiText.Read(mem, elements[kv.Key].E, 0x338 + UiText.Shift).Length > 0)
                               .Select(kv => kv.Key).ToList();
            if (flagged.Count >= 1 && flagged.Count <= 3)
            {
                long a = flagged[flagged.Count - 1];
                _initialFocusPending = 0;
                _focusState = state;
                // A highlighted banner inside the selected element is what the screen starts on:
                // the league screen selects its "Challenge Leagues" group, whose banners are its
                // grandchildren ([group]/1/n), with the default league's lit; the group's heading
                // ([group]/0/n) and the banners' own switches (deeper) are lit too.
                int bannerDepth = elements[a].Path.Count(c => c == '/') + 2;
                string banners = elements[a].Path + "/1/";
                long lit = _preGameState != "CreateCharacterState" ? 0 : state.Where(kv => kv.Value.Lit && elements[kv.Key].Path.StartsWith(banners) &&
                                             elements[kv.Key].Path.Count(c => c == '/') == bannerDepth &&
                                             DescribeHighlighted(elements[kv.Key].E).Length > 0)
                                .Select(kv => kv.Key).FirstOrDefault();
                if (lit != 0) AnnounceFocus(lit, elements[lit].E, elements[lit].Path, "Highlight");
                else AnnounceFocus(a, elements[a].E, elements[a].Path, "Initial focus");
                return;
            }
            if (now - _initialFocusPending > 1500) _initialFocusPending = 0;
        }

        var prev = _focusState;
        _focusState = state;
        if (prev == null) return;
        if (state.Count >= FocusScanMaxElements && now - _focusCapLoggedMs > 60000)
        {
            _focusCapLoggedMs = now;
            PluginLog.Write("Focus scan stopped at " + FocusScanMaxElements + " elements");
        }

        // A pop-up (an item's action menu): controls that were not there before, one of them
        // focused. Not right after a panel opened or closed, whose new elements are its own.
        var fresh = state.Keys.Where(k => !prev.ContainsKey(k)).ToList();
        Guard("tooltips", () => CatchTooltip(now, state, elements, fresh));
        Guard("prices", () => CatchPrice(now, elements));
        if (fresh.Count > 0 && fresh.Count <= 400 && now - _panelsChangedMs > 600)
        {
            var texts = fresh.Select(k => (Key: k, Text: CleanText(UiText.Text(mem, elements[k].E)))).Where(x => x.Text.Length > 0).ToList();
            var freshFocus = fresh.Where(k => state[k].Flag && elements[k].Path.Count(c => c == '/') > 1).ToList();
            // A message of the game (not enough currency, an item in hand): a sentence that
            // appears as its own element four levels down ("root/1/65/5/0", found 21 Nov 2026),
            // not deep inside a panel, the chat or a tooltip.
            foreach (var x in texts)
            {
                string p = elements[x.Key].Path;
                if (!p.StartsWith("root/") || p.Count(c => c == '/') != 4 || x.Text.Split(' ').Length < 3) continue;
                if (!(x.Text.EndsWith(".") || x.Text.EndsWith("!") || x.Text.EndsWith("?"))) continue;   // a sentence, not a label
                if (_gameMessageSaid.TryGetValue(x.Text, out long at) && now - at < 3000) continue;
                _gameMessageSaid[x.Text] = now;
                PluginLog.Write("Game message: [" + p + "] " + x.Text);
                Say(x.Text, true);
            }
            if (freshFocus.Count >= 1 && freshFocus.Count <= 2)
            {
                long addr = freshFocus[freshFocus.Count - 1];
                if (addr != _lastFocusAddr && DescribeFocused(elements[addr].E).Length > 0)
                {
                    // A dialog: its title and message ("Warning. You are holding an item...")
                    // before the button, all new with it (root/1/65/0/0: title_label,
                    // message_label, then the buttons in /3 or /2; found 21 Nov 2026).
                    string fp = elements[addr].Path;
                    int cut = fp.LastIndexOf('/');
                    cut = cut > 0 ? fp.LastIndexOf('/', cut - 1) : -1;
                    string dialog = cut > 0 ? fp.Substring(0, cut) : "";
                    string buttons = cut > 0 ? fp.Substring(0, fp.LastIndexOf('/')) : "";
                    var lines = dialog.Length == 0 ? new List<string>() : texts
                        .Where(x => elements[x.Key].Path.StartsWith(dialog + "/") && !elements[x.Key].Path.StartsWith(buttons + "/") && elements[x.Key].Path != buttons)
                        .OrderBy(x => elements[x.Key].Path, StringComparer.Ordinal).Select(x => x.Text).Distinct().ToList();
                    AnnounceFocus(addr, elements[addr].E, elements[addr].Path, "Pop-up focus",
                                  lines.Count > 0 && lines.Count <= 4 ? JoinSpeech(lines) : "");
                    return;
                }
            }
        }

        // Only elements that were visible last time count, so a panel opening (many elements
        // appearing at once) does not trigger a flood of speech.
        var flagOn = new List<long>();
        var litOn = new List<long>();
        var selChanged = new List<long>();
        var tabOn = new List<long>();
        foreach (var kv in state)
        {
            if (!prev.TryGetValue(kv.Key, out var was)) continue;
            if (kv.Value.Flag && !was.Flag) flagOn.Add(kv.Key);
            if (kv.Value.Lit && !was.Lit) litOn.Add(kv.Key);
            if (kv.Value.Sel != was.Sel)
            {
                if (kv.Value.Sel == TabSelectedValue && was.Sel == 0) tabOn.Add(kv.Key);
                else if (kv.Value.Sel >= 0 && kv.Value.Sel < 200) selChanged.Add(kv.Key);
            }
        }

        // 1. A tab was selected.
        if (tabOn.Count == 1)
        {
            var (tab, tabPath) = elements[tabOn[0]];
            string name = CleanText(FirstText(tab, 4, null));
            PluginLog.Write("Tab: [" + tabPath + "] -> \"" + name + "\"");
            if (name.Length > 0) Say(T($"{name} tab", $"Pestaña {name}"), true);
            return;
        }

        // 2. A control's focus flag switched on (one or two at a time = the selection moved).
        // Top-level panels (path "root/4") flip their flag between 1 and 2 as the selection
        // moves inside them; only the things inside are announced.
        flagOn.RemoveAll(a => elements[a].Path.Count(c => c == '/') <= 1);

        if (flagOn.Count >= 1 && flagOn.Count <= 2)
        {
            long addr = flagOn[flagOn.Count - 1];
            if (addr != _lastFocusAddr) AnnounceFocus(addr, elements[addr].E, elements[addr].Path, "Focus");
            return;
        }

        // 3. Highlight fallback: an element with text (a character entry) lit up, and no control
        //    took the focus flag. Only a single change counts, so animations stay quiet.
        if (flagOn.Count == 0)
        {
            var lit = litOn.Where(a => FirstText(elements[a].E, 4, null).Length > 0).ToList();
            if (lit.Count == 1 && lit[0] != _lastFocusAddr)
            {
                AnnounceFocus(lit[0], elements[lit[0]].E, elements[lit[0]].Path, "Highlight");
                return;
            }
        }

        // 4. Fallback for controls without the flag: a settings section's selected-row index
        //    changed. Its rows are in section/1/3.
        if (flagOn.Count == 0 && selChanged.Count == 1)
        {
            var (section, secPath) = elements[selChanged[0]];
            int index = state[selChanged[0]].Sel;
            Element rows = Try<Element>(() => section.GetChildAtIndex(1)?.GetChildAtIndex(3), null);
            long count = rows != null ? Try(() => rows.ChildCount, 0L) : 0;
            if (rows != null && index < count)
            {
                Element row = Try<Element>(() => rows.GetChildAtIndex(index), null);
                if (row != null && row.Address != 0 && row.Address != _lastFocusAddr)
                    AnnounceFocus(row.Address, row, secPath + "/1/3/" + index, "Row");
            }
        }
    }

    private void AnnounceFocus(long addr, Element e, string path, string kind, string lead = "")
    {
        _lastFocusEventMs = _clock.ElapsedMilliseconds;
        _lastFocusAddr = addr;
        _lastFocusElement = e;
        string text = kind == "Highlight" ? DescribeHighlighted(e) : DescribeFocused(e);
        if (text.Length == 0 && lead.Length == 0) text = Try(() => UnreadSelection(e, path), "");

        _focusRow = FindRow(e);
        string controlKind = "";
        _focusValue = _focusRow != null ? ControlValue(_focusRow, out controlKind) : "";
        // "Log in button, 2 of 5" (ButtonPlace.cs).
        if (text.Length > 0) text += Try(() => ButtonSuffix(e), "");

        PluginLog.Write(kind + ": [" + path + "] name=" + UiText.Name(Mem, e) + " -> \"" + text + "\"" +
                        (controlKind.Length > 0 ? " control=" + controlKind : ""));
        // The same line again at once (the screen's first focus, then its highlight settling on
        // the same character a quarter of a second later) is not said twice.
        long nowMs = _clock.ElapsedMilliseconds;
        if (lead.Length > 0) Say(text.Length > 0 ? JoinSpeech(new List<string> { lead, text }) : lead, true);
        else if (text.Length > 0 && !IsFateCardText(text) && !(text == _lastFocusText && nowMs - _lastFocusTextMs < 1500)) Say(text, true);
        _lastFocusText = text;
        _lastFocusTextMs = nowMs;
        // At the crafting bench: the option's cost (Hideout.cs).
        string benchCost = Try(() => BenchCost(e), "");
        if (benchCost.Length > 0) Say(benchCost, false);
        // Its tooltip, if one appears (not for a screen's first selection, which comes with the
        // whole screen).
        if (kind != "Initial focus") TooltipAnchor(addr, lead.Length > 0 ? lead + " " + text : text);
    }

    private string _lastFocusText = "";
    private long _lastFocusTextMs;

    // The login screen's news banners are pictures with no text, downloaded into the game's
    // MOTDCache: their file name says what they are ("public_news_2026-11-07_ExileCon_Packs_Half_
    // MOTD_EN.png" -> "News: ExileCon Packs").
    private string NewsPicture(Element e)
    {
        string path = "";
        try
        {
            long p = Mem.Read<long>(e.Address + 0x230 + UiText.Shift);
            if (p >= 0x10000 && p <= 0x7FFF_FFFF_FFFF)
            {
                long s = Mem.Read<long>(p + 8);
                if (s >= 0x10000 && s <= 0x7FFF_FFFF_FFFF) path = ReadAsciiOrWide(s);
            }
        }
        catch { }
        int at = path.IndexOf("MOTDCache/", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return "";
        string name = path.Substring(at + 10);
        int cut = name.IndexOfAny(new[] { '|', '.' });
        if (cut > 0) name = name.Substring(0, cut);
        // The path is read cut short at times ("..._ESHalf_MOT"): "_MOTD" and what follows go.
        int motdAt = name.IndexOf("_MOT", StringComparison.OrdinalIgnoreCase);
        if (motdAt > 0) name = name.Substring(0, motdAt);
        name = Regex.Replace(name, @"^public_news_\d{4}-\d{2}-\d{2}_", "", RegexOptions.IgnoreCase);
        var words = name.Split('_').Where(w => w.Length > 0).ToList();
        int motd = words.FindIndex(w => w.Equals("MOTD", StringComparison.OrdinalIgnoreCase));
        if (motd >= 0) words = words.Take(motd).ToList();
        // Layout words ("Half", "ESHalf": a half-width banner, in Spanish) say nothing.
        words = words.Where(w => !Regex.IsMatch(w, "^([A-Z]{2})?(Half|Full|Wide)$")).ToList();
        string text = string.Join(" ", words.Select(w => Regex.Replace(w, "(?<=[a-z]{2})(?=[A-Z][a-z]{3,})", " ")));
        return text.Length > 0 ? T($"News: {text}", $"Noticias: {text}") : T("News", "Noticias");
    }

    // Speaks a slider's or checkbox's new value when it changes on the selected row.
    private Element _focusRow;
    private string _focusValue = "";

    private void CheckValueChange()
    {
        if (_focusRow == null || _focusRow.Address == 0) return;
        string value = ControlValue(_focusRow, out string kind);
        if (value == _focusValue) return;
        _focusValue = value;
        if (value.Length == 0) return;
        PluginLog.Write("Value: " + value + " control=" + kind);
        Say(value, true);
    }

    // "Foreground FPS Cap, Enabled": the row's label, then the text inside its control.
    // A settings row has two children: [0] the label, [1] the control. The row is found by
    // walking up from the focused element; if no row is found, the nearest small parent's
    // text is used as the label.
    private string DescribeFocused(Element e)
    {
        // A character entry: all of it ("rioggggv, Level 3 Templar, Standard League").
        if (UiText.Read(Mem, e, 0x338 + UiText.Shift).Length > 0)
        {
            var all = new List<string>();
            CollectText(e, 4, all);
            if (all.Count > 0) return JoinSpeech(all);
        }

        Element row = FindRow(e);
        string label, value;
        if (row != null)
        {
            label = CleanText(FirstText(Try<Element>(() => row.GetChildAtIndex(0), null), 5, null));
            value = CleanText(FirstText(Try<Element>(() => row.GetChildAtIndex(1), null), 6, label));
        }
        else
        {
            value = CleanText(FirstText(e, 4, null));
            label = "";
            Element p = UiText.ParentOf(Mem, e);
            for (int up = 0; up < 4 && p != null && p.Address != 0; up++)
            {
                // Stop at lists (tab bars, long menus): their first text belongs to another item.
                if (Try(() => p.ChildCount, 99L) > 3) break;
                label = CleanText(FirstText(p, 5, value));
                if (label.Length > 0) break;
                p = UiText.ParentOf(Mem, p);
            }
        }

        var parts = new List<string>();
        if (label.Length > 0) parts.Add(label);
        if (value.Length > 0 && !string.Equals(value, label, StringComparison.OrdinalIgnoreCase)) parts.Add(value);
        else if (row != null)
        {
            string state = ControlValue(row, out _);
            if (state.Length > 0) parts.Add(state);
        }
        if (parts.Count == 0)
        {
            string news = NewsPicture(e);
            if (news.Length > 0) parts.Add(news);
        }
        return JoinSpeech(parts);
    }

    // The value of a settings row's control (row/1/0/0), for controls that show no text:
    //   dropdown  - has a "scrollbar" / "textField" child; value not found yet, so ""
    //   checkbox  - exactly one child; byte at UiText.CheckboxStateOffset
    //   slider    - otherwise (0 or 2 children); int at UiText.SliderValueOffset
    // `kind` is returned for logging.
    private string ControlValue(Element row, out string kind)
    {
        kind = "";
        Element control = Try<Element>(() => row.GetChildAtIndex(1)?.GetChildAtIndex(0)?.GetChildAtIndex(0), null);
        if (control == null || control.Address == 0) return "";

        // Controls with their own text (buttons, "Enabled") are described by that text.
        if (FirstText(control, 4, null).Length > 0) { kind = "text"; return ""; }

        var mem = Mem;
        IList<Element> kids = Try<IList<Element>>(() => control.Children, null);
        int kidCount = kids?.Count ?? 0;
        bool dropdown = kids != null && kids.Any(k =>
        {
            string n = UiText.Name(mem, k);
            return n == "scrollbar" || n == "textField";
        });

        byte[] b = TextProbe.ReadUpTo(mem, control.Address, 0x1000);
        if (b == null || b.Length < 0x608 + UiText.Shift) { kind = "unreadable"; return ""; }

        // Dropdowns: the selected choice's name, then "2 of 3". Falls back to the number if
        // the name cannot be read.
        if (dropdown)
        {
            // While the list is open, follow the highlighted choice instead of the chosen one.
            bool open = BitConverter.ToInt32(b, UiText.DropdownOpenOffset) == 1;
            int index = BitConverter.ToInt32(b, open ? UiText.DropdownHighlightOffset : UiText.DropdownIndexOffset);
            if (index < 0 || index >= 200) { kind = "dropdown open=" + open + " index=" + index; return ""; }
            string name = CleanText(UiText.DropdownChoice(mem, control.Address, index, out int count));
            kind = "dropdown open=" + open + " index=" + index + " count=" + count + " name=\"" + name + "\"";
            int shown = index + 1;
            string position = count > 0 ? T($"{shown} of {count}", $"{shown} de {count}") : T($"choice {shown}", $"opción {shown}");
            return name.Length > 0 ? name + ", " + position : position;
        }

        // Checkboxes have exactly one child (Reverb Enabled, Mute when in background).
        if (kidCount == 1)
        {
            byte c = b[UiText.CheckboxStateOffset];
            kind = "checkbox b602=" + c;
            return c == 1 ? T("checked", "marcado") : c == 0 ? T("not checked", "no marcado") : "";
        }

        // Sliders: volume sliders have no children, Sharpness has two. Ranges differ (Master
        // Volume tops out at 100, Dialogue Volume read 330), so the raw number is spoken.
        int v = BitConverter.ToInt32(b, UiText.SliderValueOffset);
        kind = "slider v364=" + v + " kids=" + kidCount;
        return v >= 0 && v <= 100000 ? v.ToString() : "";
    }

    // The settings row containing e (e itself or an ancestor up to 8 levels): an element with
    // exactly two children whose first child holds text, inside a list of several rows.
    // Buttons carry their click sound ("Audio/Sound Effects/UI/click_02_down.ogg") at +0x2D0;
    // a settings row's label does not. Checks the element and its direct children.
    private bool IsButton(Element e)
    {
        if (e == null) return false;
        var mem = Mem;
        if (UiText.Read(mem, e, 0x2D0 + UiText.Shift).StartsWith("Audio/")) return true;
        IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
        if (kids == null || kids.Count > 10) return false;
        return kids.Any(k => UiText.Read(mem, k, 0x2D0 + UiText.Shift).StartsWith("Audio/"));
    }

    // A highlighted entry (a league banner, a character): its own texts, from its direct
    // children first ("Hardcore Covens. Defeat witches of the Coven..."), not its buttons.
    private string DescribeHighlighted(Element e)
    {
        if (UiText.Read(Mem, e, 0x338 + UiText.Shift).Length > 0) return DescribeFocused(e);   // character entry
        var parts = new List<string>();
        string own = CleanText(UiText.Text(Mem, e));
        if (own.Length > 0) parts.Add(own);
        CollectText(e, 1, parts);
        if (parts.Count == 0) CollectText(e, 3, parts);
        return parts.Count > 0 ? JoinSpeech(parts.Distinct().ToList()) : DescribeFocused(e);
    }

    // The selected element must lie in the row's control half (child 1): two side-by-side
    // buttons ("log out", "options") share a parent but are not a label and its control.
    private Element FindRow(Element e)
    {
        Element cur = e;
        Element came = null;
        for (int up = 0; up <= 8 && cur != null && cur.Address != 0; up++)
        {
            if (Try(() => cur.ChildCount, 0L) == 2)
            {
                Element parent = UiText.ParentOf(Mem, cur);
                long siblings = parent != null ? Try(() => parent.ChildCount, 0L) : 0;
                Element first = Try<Element>(() => cur.GetChildAtIndex(0), null);
                Element second = Try<Element>(() => cur.GetChildAtIndex(1), null);
                bool inControl = came == null || (second != null && second.Address == came.Address);
                if (inControl && siblings >= 2 && FirstText(first, 4, null).Length > 0 && !IsButton(first)) return cur;
            }
            came = cur;
            cur = UiText.ParentOf(Mem, cur);
        }
        return null;
    }

    // First visible text in a subtree, depth-first, skipping text equal to `skip`.
    private string FirstText(Element e, int depth, string skip)
    {
        if (e == null || depth < 0) return "";
        string t = UiText.Text(Mem, e);
        if (t.Length > 0 && (skip == null || !string.Equals(CleanText(t), skip, StringComparison.OrdinalIgnoreCase)))
            return t;

        IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
        if (kids == null || kids.Count > 40) return "";
        foreach (Element k in kids)
        {
            if (!Try(() => UiText.VisibleLocal(Mem, k), false)) continue;
            string r = FirstText(k, depth - 1, skip);
            if (r.Length > 0) return r;
        }
        return "";
    }

    // ===================================================================
    // Hover reader
    // ===================================================================

    private void PollHover(long now)
    {
        if (now < _nextHoverPollMs) return;
        _nextHoverPollMs = now + 80;

        string text;
        try { text = ReadHoverText(now); }
        catch (Exception ex) { Report("hover", ex); return; }

        // Something on screen to read under the cursor/focus (NPC dialogue options, a window):
        // navigation sounds stay quiet meanwhile.
        if (text.Length > 0) _lastHoverTextMs = now;

        if (text != _pendingText)
        {
            _pendingText = text;
            _pendingSinceMs = now;
        }

        long stableFor = now - _pendingSinceMs;

        if (text.Length == 0)
        {
            // Nothing under the cursor for a moment: allow the next hover to be announced again.
            if (stableFor >= 400) _lastHoverSpoken = "";
            return;
        }

        if (stableFor >= Settings.HoverDelayMs.Value && text != _lastHoverSpoken)
        {
            // A dialogue's options are read one at a time by PollDialogue, not as one list.
            if (IsWholeOptionList(text) || IsWorldMapText(text) || _hoverQuiet || IsFateCardText(text)) { _lastHoverSpoken = text; return; }
            _lastHoverSpoken = text;
            Say(text, true);
            if (_hoverGeneric) TooltipAnchor(_hoverKeyHover, text);
        }
    }

    private string ReadHoverText(long now)
    {
        IngameState state = GameController?.IngameState;
        if (state == null) return "";

        Element hover = state.UIHover;
        // ExileApi's UIHoverTooltip is wrong on this game version (it points at an empty
        // full-screen element, or at whatever popup is open), so it is not used for now.
        Element tip = null;

        bool hoverOk = IsUsable(hover);
        bool tipOk = false;

        if (!hoverOk && !tipOk)
        {
            _hoverKeyHover = 0;
            _hoverKeyTip = 0;
            return "";
        }

        long hoverKey = hoverOk ? hover.Address : 0;
        long tipKey = tipOk ? tip.Address : 0;

        // Reading a whole tooltip is many memory reads: reuse the result while nothing changed.
        if (hoverKey == _hoverKeyHover && tipKey == _hoverKeyTip && now - _hoverCachedAtMs < 1000)
            return _hoverText;

        string text = ComposeHoverText(hover, hoverOk, tip, tipOk);

        _hoverKeyHover = hoverKey;
        _hoverKeyTip = tipKey;
        _hoverCachedAtMs = now;
        _hoverText = text;
        return text;
    }

    private string ComposeHoverText(Element hover, bool hoverOk, Element tip, bool tipOk)
    {
        ElementType? type = null;
        if (hoverOk)
        {
            try { type = UiText.TypeOf(Mem, hover); }
            catch { }
        }

        var tipParts = new List<string>();
        if (tipOk) CollectText(tip, 10, tipParts);

        _hoverQuiet = false;   // set again by DescribePassive for the finder's node
        _hoverGeneric = false;   // items, passives and slots have their own tooltips
        if (type == ElementType.PassivePoint)
        {
            string passive = Guard("passive", () => DescribePassive(hover, tipParts));
            if (!string.IsNullOrEmpty(passive)) return passive;
        }

        if (type == ElementType.InventoryItem && Settings.ItemsFromComponents.Value)
        {
            string item = Guard("item", () => DescribeInventoryFocus(hover));
            if (!string.IsNullOrEmpty(item)) return item;
        }
        if (hoverOk && type != ElementType.InventoryItem)
        {
            string slot = Guard("slot", () => DescribeEmptySlot(hover));
            if (!string.IsNullOrEmpty(slot)) return slot;
        }
        _lastFocusWasEquipment = false;
        _tipLines.Clear();   // not an item: tap no longer steps through an old tooltip
        _hoverGeneric = true;

        if (tipParts.Count > 0) return JoinSpeech(tipParts);

        var own = new List<string>();
        if (hoverOk) CollectText(hover, 3, own);

        // A button's label usually sits in a child; if the hovered element and its children
        // have no text, try its parent (for example a label beside an icon).
        if (own.Count == 0 && hoverOk)
        {
            Element parent = UiText.ParentOf(Mem, hover);
            long kids = parent != null ? Try(() => parent.ChildCount, 99L) : 99L;
            if (parent != null && parent.Address != 0 && kids <= 6) CollectText(parent, 2, own);
        }
        return JoinSpeech(own);
    }

    private static bool IsUsable(Element e)
    {
        return e != null && e.Address != 0 && e.IsValid;
    }

    // Gathers the visible text of an element and its children, in tree order.
    private void CollectText(Element root, int maxDepth, List<string> parts)
    {
        int visited = 0;
        CollectTextRec(root, maxDepth, parts, true, ref visited);
    }

    private void CollectTextRec(Element e, int depth, List<string> parts, bool isRoot, ref int visited)
    {
        if (e == null || depth < 0 || parts.Count >= MaxTextParts) return;
        if (++visited > MaxVisitedNodes) return;

        // IsVisible walks up the parents; for children the cheaper local flag is enough.
        bool visible = isRoot ? UiText.Visible(Mem, e) : UiText.VisibleLocal(Mem, e);
        if (!visible) return;

        string text = CleanText(UiText.Text(Mem, e));
        if (text.Length > 0 && (parts.Count == 0 || parts[parts.Count - 1] != text))
            parts.Add(text);

        IList<Element> kids = e.Children;
        if (kids == null) return;
        foreach (Element child in kids)
            CollectTextRec(child, depth - 1, parts, false, ref visited);
    }

    // ===================================================================
    // Passive tree
    // ===================================================================

    private string DescribePassive(Element hover, List<string> tipParts)
    {
        TreePassiveElement node = hover.AsObject<TreePassiveElement>();
        var skill = node?.PassiveSkill;
        if (skill == null || skill.Address == 0) return null;

        string name = CleanText(skill.Name);
        if (name.Length == 0) return null;

        // ExileApi's keystone/notable flags read the wrong bytes on this game version (every node
        // came out a keystone). In the PassiveSkills record the keystone flag is the byte at
        // +0x4A and the notable flag the byte at +0x4B (checked 18 Nov 2026 against known nodes:
        // 120 keystones, 1918 notables).
        var mem = Mem;
        bool keystone = Try(() => mem.Read<byte>(skill.Address + 0x4A), (byte)0) == 1;
        bool notable = Try(() => mem.Read<byte>(skill.Address + 0x4B), (byte)0) == 1;
        string id = Try(() => skill.Id, "") ?? "";
        // Masteries by their Id, which holds in every client language: "mastery_life123", or
        // "affliction_..._mastery..." on cluster jewels (all 398 masteries, and no other node, on
        // 20 Nov 2026).
        bool mastery = id.StartsWith("mastery_", StringComparison.OrdinalIgnoreCase)
            || (id.StartsWith("affliction_", StringComparison.OrdinalIgnoreCase) && id.IndexOf("mastery", StringComparison.OrdinalIgnoreCase) >= 0)
            || name.EndsWith("Mastery", StringComparison.OrdinalIgnoreCase);
        string kind = keystone ? T("keystone", "piedra angular")
            : notable ? T("notable", "notable")
            : mastery ? T("mastery", "maestría")
            : id.StartsWith("jewel_slot", StringComparison.OrdinalIgnoreCase) ? T("jewel socket", "engarce de joya")
            : T("passive", "pasiva");

        bool allocated = false;
        var ids = GameController.IngameState.ServerData?.PassiveSkillIds;
        if (ids != null) allocated = ids.Contains((ushort)skill.PassiveId);
        // "Linked" counts your class's starting node too (PassiveFinder.cs).
        var have = AllocatedPassives();

        string notLinkedText = T("not linked", "sin enlazar");
        string status = allocated ? T("allocated", "asignada")
            : node.CanAllocate ? T("can be allocated", "se puede asignar")
            : Try(() => _passiveLinks != null && _passiveLinks.TryGetValue(skill.Address, out var near) &&
                        near.Any(o => _passiveIdOfRow.TryGetValue(o, out ushort op) && have.Contains(op)), false) ? T("linked", "enlazada")
            : notLinkedText;

        // Class starts are stored as "MARAUDER", "SIX" (Shadow) and "Seven" (Scion).
        string[] starts = { "marauder", "witch", "ranger", "duelist", "templar", "six", "seven" };
        string startId = starts.FirstOrDefault(s => id.StartsWith(s, StringComparison.OrdinalIgnoreCase) && id.Length > s.Length && char.IsDigit(id[s.Length]));
        if (startId != null)
        {
            // The game's own class name, in the client's language (ClassScreen.cs), by class number.
            name = ClassName(startId switch
            {
                "marauder" => 1, "witch" => 3, "ranger" => 2, "duelist" => 4, "templar" => 5, "six" => 6, _ => 0,
            });
            kind = T("class start", "inicio de clase");
            status = "";
        }

        string distance = "", links = "";
        try { (distance, links) = DescribePassiveLinks(hover, skill.Address); }
        catch (Exception ex) { Report("passive links", ex); }
        // Its place in the followed build (BuildTree.cs): "in the build, from Act 2".
        string buildNote = Try(() => BuildNodeNote(skill.Address, (ushort)skill.PassiveId, allocated), "");
        // In your own plan (BuildCraft.cs), and the node the triple tap asks about.
        string planNote = Try(() => PlanNote((ushort)skill.PassiveId, allocated), "");
        if (planNote.Length > 0) buildNote = buildNote.Length > 0 ? planNote + ", " + buildNote : planNote;
        NoteCraftNode(skill.Address);
        var parts = new List<string> { name + ", " + kind + (status.Length > 0 ? ", " + status : "") +
                                        (!allocated && !string.IsNullOrEmpty(distance) && status == notLinkedText ? ", " + distance : "") +
                                        (buildNote.Length > 0 ? ", " + buildNote : "") };
        // The finder's guide (D-pad), right after the node's name, so it is heard first.
        _passiveHover = hover;
        _passiveHoverRow = skill.Address;
        // The node the finder was used on is not read again over the finder's words; another
        // node is, and ends that.
        _hoverQuiet = _finderQuietRow != 0 && _finderQuietRow == skill.Address;
        if (!_hoverQuiet) _finderQuietRow = 0;
        string guide = Try(() => FinderGuide(skill.Address), "");
        // Without a finder target, the build's next node and the way to it (BuildTree.cs).
        if (guide.Length == 0) guide = Try(() => BuildGuideLine(skill.Address), "");
        if (guide.Length > 0) parts.Add(guide);

        // What the node does: the game's own tooltip text when it can be found on screen,
        // otherwise the node's stats in plain words.
        var lines = tipParts.Count > 0 ? tipParts : FindTooltipLines(name, hover);
        int before = parts.Count;
        foreach (string line in lines)
            if (!string.Equals(line, name, StringComparison.OrdinalIgnoreCase))
                parts.Add(line);
        if (parts.Count == before)
            foreach (var (stat, value) in Try(() => skill.Stats.ToList(), new List<(ExileCore.PoEMemory.FilesInMemory.StatsDat.StatRecord, int)>()))
            {
                // The game's own words when known (keystones' rules too), else the stat in words.
                var game = Try(() => PassiveStatText(stat, value), new List<string>());
                if (game.Count > 0) { parts.AddRange(game); continue; }
                string s = StatWords(stat?.Key, value);
                if (s.Length > 0) parts.Add(s);
            }
        if (!string.IsNullOrEmpty(links)) parts.Add(links);

        // Tap and double tap step through these lines, as with item tooltips.
        if (!_tipLines.SequenceEqual(parts))
        {
            _tipLines = new List<string>(parts);
            _tipIndex = 0;
            _tipItemAddr = 0;
        }
        return JoinSpeech(parts);
    }

    /// <summary>
    /// The text of an on-screen tooltip whose title is `title`: the visible element showing that
    /// text (not the hovered element itself), then the nearest ancestor that also holds other
    /// lines. ExileApi's own tooltip pointer is wrong on this game version.
    /// </summary>
    private List<string> FindTooltipLines(string title, Element hover)
    {
        var result = new List<string>();
        Element root = UiRoot();
        if (root == null || root.Address == 0) return result;
        var mem = Mem;

        Element found = null;
        var queue = new Queue<(Element E, int Depth)>();
        queue.Enqueue((root, 0));
        int visited = 0;
        while (queue.Count > 0 && visited < 6000 && found == null)
        {
            var (e, depth) = queue.Dequeue();
            visited++;
            if (e.Address == hover.Address) continue;
            if (!Try(() => UiText.VisibleLocal(Mem, e), false)) continue;
            if (depth > 0 && string.Equals(CleanText(UiText.Text(mem, e)), title, StringComparison.OrdinalIgnoreCase))
            {
                found = e;
                break;
            }
            if (depth >= 16) continue;
            var kids = Try<IList<Element>>(() => e.Children, null);
            if (kids == null || kids.Count > 400) continue;
            foreach (Element k in kids) queue.Enqueue((k, depth + 1));
        }
        if (found == null) return result;

        Element cur = found;
        for (int up = 0; up < 4; up++)
        {
            Element parent = UiText.ParentOf(Mem, cur);
            if (parent == null || parent.Address == 0) break;
            var lines = new List<string>();
            CollectText(parent, 8, lines);
            if (lines.Count(l => !string.Equals(l, title, StringComparison.OrdinalIgnoreCase)) > 0) return lines;
            cur = parent;
        }
        return result;
    }

    /// <summary>A stat id in plain words: "maximum_life_+%" 10 -> "10% increased maximum life".</summary>
    private static string StatWords(string key, int value)
    {
        if (string.IsNullOrEmpty(key)) return "";
        string k = key;
        foreach (string pre in new[] { "base_", "keystone_" })
            if (k.StartsWith(pre)) k = k.Substring(pre.Length);
        string Words(string s) => s.Replace("_", " ").Replace("  ", " ").Trim();
        int a = Math.Abs(value);
        if (k.EndsWith("_+%_final"))
        {
            string wFinal = Words(k[..^9]);
            return value >= 0 ? T($"{a}% more {wFinal}", $"{a}% más {wFinal}") : T($"{a}% less {wFinal}", $"{a}% menos {wFinal}");
        }
        if (k.EndsWith("_+%"))
        {
            string wPct = Words(k[..^3]);
            return value >= 0 ? T($"{a}% increased {wPct}", $"{a}% de aumento de {wPct}") : T($"{a}% reduced {wPct}", $"{a}% de reducción de {wPct}");
        }
        if (k.EndsWith("_+"))
        {
            string signPlus = value >= 0 ? "+" : "-";
            string wPlus = Words(k[..^2]);
            return T($"{signPlus}{a} to {wPlus}", $"{signPlus}{a} a {wPlus}");
        }
        if (k.EndsWith("_%")) return value + "% " + Words(k[..^2]);
        if (k.EndsWith("_pct")) return value + "% " + Words(k[..^4]);
        if (value == 1) return Words(k);
        string signAny = value >= 0 ? "+" : "";
        string wAny = Words(k);
        return T($"{signAny}{value} to {wAny}", $"{signAny}{value} a {wAny}");
    }

    // ===================================================================
    // Items
    // ===================================================================

    private string DescribeHoveredItem(Element hover)
    {
        NormalInventoryItem slot = hover.AsObject<NormalInventoryItem>();
        Entity item = slot?.Item;
        if (item == null || item.Address == 0 || !item.IsValid) return null;
        return DescribeItem(item);
    }

    private static T GetComp<T>(Entity e) where T : Component, new()
    {
        try
        {
            if (!e.HasComponent<T>()) return null;
            T c = e.GetComponent<T>();
            return c != null && c.Address != 0 ? c : null;
        }
        catch
        {
            return null;
        }
    }

    // A spoken description of an item built from its data, not from the tooltip.
    private string DescribeItem(Entity item)
    {
        var parts = new List<string>();

        BaseComp baseC = GetComp<BaseComp>(item);
        ModsComp mods = GetComp<ModsComp>(item);
        QualityComp quality = GetComp<QualityComp>(item);
        SocketsComp sockets = GetComp<SocketsComp>(item);
        StackComp stack = GetComp<StackComp>(item);

        ItemRarity rarity = ItemRarity.Unknown;
        if (mods != null) rarity = Try(() => mods.ItemRarity, ItemRarity.Unknown);

        string rarityWord = rarity switch
        {
            ItemRarity.Normal => T("Normal", "Normal"),
            ItemRarity.Magic => T("Magic", "Mágico"),
            ItemRarity.Rare => T("Rare", "Raro"),
            ItemRarity.Unique => T("Unique", "Único"),
            _ => "",
        };

        string baseName = baseC != null ? CleanText(Try(() => baseC.Name, "")) : "";
        if (baseName.Length == 0) baseName = LastPathSegment(Try(() => item.Path, ""));

        string title = "";
        if (mods != null && (rarity == ItemRarity.Rare || rarity == ItemRarity.Unique))
            title = CleanText(Try(() => mods.UniqueName, ""));

        if (rarityWord.Length > 0) parts.Add(rarityWord);
        if (title.Length > 0 && !string.Equals(title, baseName, StringComparison.OrdinalIgnoreCase)) parts.Add(title);
        if (baseName.Length > 0) parts.Add(baseName);

        int stackSize = stack != null ? Try(() => stack.Size, 0) : 0;
        if (stackSize > 1) parts.Add(T($"stack of {stackSize}", $"pila de {stackSize}"));

        int itemLevel = mods != null ? Try(() => mods.ItemLevel, 0) : 0;
        if (itemLevel > 0) parts.Add(T($"item level {itemLevel}", $"nivel de objeto {itemLevel}"));

        int qualityValue = quality != null ? Try(() => quality.ItemQuality, 0) : 0;
        if (qualityValue > 0) parts.Add(T($"quality {qualityValue} percent", $"calidad {qualityValue} por ciento"));

        int socketCount = sockets != null ? Try(() => sockets.NumberOfSockets, 0) : 0;
        if (socketCount > 0)
        {
            int link = Try(() => sockets.LargestLinkSize, 0);
            string socketText = socketCount == 1 ? T($"{socketCount} socket", $"{socketCount} engarce") : T($"{socketCount} sockets", $"{socketCount} engarces");
            parts.Add(link > 1 ? socketText + T($", largest link {link}", $", enlace más largo {link}") : socketText);
        }

        if (baseC != null && Try(() => baseC.isCorrupted, false)) parts.Add(T("corrupted", "corrupto"));
        if (mods != null && Try(() => mods.IsMirrored, false)) parts.Add(T("mirrored", "reflejado"));
        if (mods != null && !Try(() => mods.Identified, true) &&
            (rarity == ItemRarity.Magic || rarity == ItemRarity.Rare || rarity == ItemRarity.Unique))
            parts.Add(T("unidentified", "sin identificar"));

        if (mods != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int before = parts.Count;

            AddStats(parts, T("Enchant", "Encantamiento"), Try<IEnumerable<string>>(() => mods.EnchantedStats, null), seen);
            AddStats(parts, T("Implicit", "Implícito"), Try<IEnumerable<string>>(() => mods.HumanImpStats, null), seen);
            AddStats(parts, T("Fractured", "Fracturado"), Try<IEnumerable<string>>(() => mods.FracturedStats, null), seen);
            AddStats(parts, T("Crafted", "Fabricado"), Try<IEnumerable<string>>(() => mods.HumanCraftedStats, null), seen);
            AddStats(parts, "", Try<IEnumerable<string>>(() => mods.HumanStats, null), seen);

            // If the readable stat lists came back empty, fall back to the raw modifier names.
            if (parts.Count == before)
            {
                var raw = Try<List<ItemMod>>(() => mods.ItemMods, null);
                if (raw != null && raw.Count > 0)
                {
                    var lines = new List<string>();
                    foreach (ItemMod m in raw)
                    {
                        ItemMod mod = m;
                        string t = Try(() => mod.Translation, "");
                        if (string.IsNullOrWhiteSpace(t)) t = Try(() => mod.DisplayName, "");
                        if (string.IsNullOrWhiteSpace(t)) t = Try(() => mod.Name, "");
                        lines.Add(t);
                    }
                    AddStats(parts, T("Modifiers", "Modificadores"), lines, seen);
                }
            }
        }

        return JoinSpeech(parts);
    }

    private static void AddStats(List<string> parts, string label, IEnumerable<string> lines, HashSet<string> seen)
    {
        if (lines == null) return;

        var fresh = new List<string>();
        foreach (string line in lines)
        {
            string clean = CleanText(line);
            if (clean.Length > 0 && seen.Add(clean)) fresh.Add(clean);
        }
        if (fresh.Count == 0) return;

        if (label.Length > 0) parts.Add(label);
        parts.AddRange(fresh);
    }

    private static string LastPathSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        int i = path.LastIndexOf('/');
        return i >= 0 && i < path.Length - 1 ? path.Substring(i + 1) : path;
    }

    // ===================================================================
    // Item browser (inventory and equipment)
    // ===================================================================

    private ServerData GetServerData() => GameController.IngameState?.ServerData;

    private static void AddSlotItems(List<BrowseEntry> list, ServerInventory inventory, string label, bool numberFlasks)
    {
        if (inventory == null || inventory.Address == 0) return;

        var slots = inventory.InventorySlotItems;
        if (slots == null) return;

        foreach (var s in slots.OrderBy(x => x.PosY).ThenBy(x => x.PosX))
        {
            Entity item = s.Item;
            if (item == null || item.Address == 0 || !item.IsValid) continue;

            list.Add(new BrowseEntry
            {
                Label = numberFlasks ? label + " " + (s.PosX + 1) : label,
                Item = item,
                Col = s.PosX + 1,
                Row = s.PosY + 1,
            });
        }
    }

    private List<BrowseEntry> BuildBrowseList()
    {
        var list = new List<BrowseEntry>();
        ServerData server = GetServerData();
        if (server == null) return list;

        if (!_browseEquipment)
        {
            AddSlotItems(list, server.GetPlayerInventoryBySlot(InventorySlotE.MainInventory1), "", false);
        }
        else
        {
            foreach (var (slot, label) in EquipmentSlots)
                AddSlotItems(list, server.GetPlayerInventoryBySlot(slot), EquipmentSlotName(label), slot == InventorySlotE.Flask1);
        }
        return list;
    }

    private string BrowseListName => _browseEquipment ? T("Equipment", "Equipamiento") : T("Inventory", "Inventario");

    private void SwitchList()
    {
        _browseEquipment = !_browseEquipment;
        _browseIndex = -1;

        List<BrowseEntry> list = BuildBrowseList();
        string listName = BrowseListName;
        int itemCount = list.Count;
        string summary = itemCount == 1 ? T($"{listName}, {itemCount} item", $"{listName}, {itemCount} objeto") : T($"{listName}, {itemCount} items", $"{listName}, {itemCount} objetos");

        if (!_browseEquipment)
        {
            ServerInventory inv = GetServerData()?.GetPlayerInventoryBySlot(InventorySlotE.MainInventory1);
            if (inv != null && inv.Address != 0)
            {
                int cells = 0;
                var slots = inv.InventorySlotItems;
                if (slots != null) foreach (var s in slots) cells += Math.Max(1, s.SizeX) * Math.Max(1, s.SizeY);
                int total = inv.Columns * inv.Rows;
                if (total > 0) summary += T($", {cells} of {total} cells used", $", {cells} de {total} casillas usadas");
            }
        }
        Say(summary, true);
    }

    private void BrowseStep(int direction)
    {
        List<BrowseEntry> list = BuildBrowseList();
        if (list.Count == 0)
        {
            string emptyName = BrowseListName;
            Say(T($"{emptyName} is empty, or its data is not available", $"{emptyName} está vacío, o sus datos no están disponibles"), true);
            return;
        }

        if (_browseIndex < 0 || _browseIndex >= list.Count)
            _browseIndex = direction > 0 ? 0 : list.Count - 1;
        else
            _browseIndex = (_browseIndex + direction + list.Count) % list.Count;

        BrowseEntry entry = list[_browseIndex];

        // The same wording as selecting the item in the inventory.
        string head = _browseEquipment
            ? (entry.Label.Length > 0 ? entry.Label + ": " : "") + ItemTitle(entry.Item)
            : T($"{ItemTitle(entry.Item)}, column {entry.Col}, row {entry.Row}", $"{ItemTitle(entry.Item)}, columna {entry.Col}, fila {entry.Row}");
        var lines = new List<string> { head };
        string missing = MissingRequirement(entry.Item);
        if (missing.Length > 0) lines.Add(missing);
        lines.AddRange(ItemLines(entry.Item));
        _tipLines = lines;
        _tipIndex = 0;

        int shownIndex = _browseIndex + 1, listCount = list.Count;
        Say(JoinSpeech(new List<string> { T($"{shownIndex} of {listCount}", $"{shownIndex} de {listCount}") }.Concat(lines).ToList()), true);
    }

    // ===================================================================
    // Dump tools
    // ===================================================================

    private void DumpItems()
    {
        ServerData server = GetServerData();
        if (server == null) { Say("No game data available", true); return; }

        var sb = new StringBuilder();
        sb.AppendLine("PoE1Access item dump " + DateTime.Now);

        bool wasEquipment = _browseEquipment;
        int total = 0;
        foreach (bool equipment in new[] { false, true })
        {
            _browseEquipment = equipment;
            List<BrowseEntry> list = BuildBrowseList();
            sb.AppendLine();
            sb.AppendLine("=== " + BrowseListName + " (" + list.Count + " items) ===");

            int n = 0;
            foreach (BrowseEntry entry in list)
            {
                n++;
                total++;
                DumpItem(sb, n, entry);
            }
        }
        _browseEquipment = wasEquipment;

        string path = WriteDump("items", sb);
        LogMessage("PoE1Access item dump: " + path);
        Say("Item dump saved. " + total + " items.", true);
    }

    private void DumpItem(StringBuilder sb, int number, BrowseEntry entry)
    {
        Entity item = entry.Item;
        sb.AppendLine();
        sb.AppendLine("[" + number + "] " + entry.Label + " col=" + entry.Col + " row=" + entry.Row +
                      " path=" + Try(() => item.Path, "?"));
        sb.AppendLine("    spoken: " + Guard("describe", () => DescribeItem(item)));

        ModsComp mods = GetComp<ModsComp>(item);
        if (mods == null)
        {
            sb.AppendLine("    (no mods component)");
            return;
        }

        sb.AppendLine("    rarity=" + Try(() => mods.ItemRarity.ToString(), "?") +
                      " unique=\"" + Try(() => mods.UniqueName, "") + "\"" +
                      " ilvl=" + Try(() => mods.ItemLevel, 0) +
                      " identified=" + Try(() => mods.Identified, true));
        DumpList(sb, "humanStats", Try<IEnumerable<string>>(() => mods.HumanStats, null));
        DumpList(sb, "implicit", Try<IEnumerable<string>>(() => mods.HumanImpStats, null));
        DumpList(sb, "enchant", Try<IEnumerable<string>>(() => mods.EnchantedStats, null));

        var raw = Try<List<ItemMod>>(() => mods.ItemMods, null);
        if (raw != null)
        {
            foreach (ItemMod m in raw)
            {
                ItemMod mod = m;
                sb.AppendLine("    mod: name=" + Try(() => mod.Name, "?") +
                              " display=\"" + Try(() => mod.DisplayName, "") + "\"" +
                              " translation=\"" + Try(() => mod.Translation, "") + "\"");
            }
        }
    }

    private static void DumpList(StringBuilder sb, string label, IEnumerable<string> lines)
    {
        if (lines == null) { sb.AppendLine("    " + label + ": (null)"); return; }
        string[] arr = lines.ToArray();
        sb.AppendLine("    " + label + ": " + arr.Length);
        foreach (string line in arr) sb.AppendLine("        " + line);
    }

    // F10: which named panels are open, then the UI tree under every visible top-level panel.
    private void DumpPanels()
    {
        IngameUIElements ui = GameController.IngameState?.IngameUi;
        if (ui == null) { Say("No UI available", true); return; }

        var sb = new StringBuilder();
        sb.AppendLine("PoE1Access panel dump " + DateTime.Now);
        sb.AppendLine();
        sb.AppendLine("=== panels found by title (what the plugin announces) ===");
        foreach (var p in FindVisiblePanels())
            sb.AppendLine(p.Title + "  [" + p.Path + "] addr=0x" + p.E.Address.ToString("X"));
        sb.AppendLine();
        sb.AppendLine("=== ExileApi named panels (known to be wrong on this game version) ===");
        foreach (var (label, getPanel) in PanelTable(ui))
        {
            Func<Element> getter = getPanel;
            bool visible = Try(() =>
            {
                Element el = getter();
                return el != null && el.Address != 0 && UiText.Visible(Mem, el);
            }, false);
            sb.AppendLine(label + ": " + (visible ? "OPEN" : "closed"));
        }

        sb.AppendLine();
        sb.AppendLine("=== visible top-level UI elements ===");
        int count = 0;
        IList<Element> top = Try<IList<Element>>(() => ui.Children, null);
        if (top != null)
        {
            int i = 0;
            foreach (Element panel in top)
            {
                if (panel != null && Try(() => UiText.VisibleLocal(Mem, panel), false))
                    DumpElement(sb, panel, 0, 4, i, ref count);
                i++;
            }
        }

        string path = WriteDump("panels", sb);
        LogMessage("PoE1Access panel dump: " + path);
        Say("Panel dump saved. " + count + " elements.", true);
    }

    // F11: the element under the cursor, its tooltip, and what the plugin made of it.
    private void DumpHovered()
    {
        IngameState state = GameController?.IngameState;
        if (state == null) { Say("No game state available", true); return; }

        var sb = new StringBuilder();
        sb.AppendLine("PoE1Access hover dump " + DateTime.Now);
        int count = 0;

        Element hover = state.UIHover;
        Element tip = state.UIHoverTooltip;

        sb.AppendLine();
        sb.AppendLine("=== hovered element ===");
        if (IsUsable(hover))
        {
            sb.AppendLine("type=" + Try(() => hover.Type.ToString(), "?") +
                          " pathFromRoot=" + Try(() => hover.PathFromRoot, "?"));
            DumpElement(sb, hover, 0, 8, 0, ref count);
        }
        else
        {
            sb.AppendLine("(nothing hovered)");
        }

        sb.AppendLine();
        sb.AppendLine("=== hover tooltip ===");
        if (IsUsable(tip))
            DumpElement(sb, tip, 0, 10, 0, ref count);
        else
            sb.AppendLine("(no tooltip)");

        sb.AppendLine();
        sb.AppendLine("=== what the reader would say now ===");
        sb.AppendLine(_hoverText.Length > 0 ? _hoverText : "(nothing)");

        if (IsUsable(hover) && Try(() => UiText.TypeOf(Mem, hover), ElementType.MiscGroundLabel) == ElementType.InventoryItem)
        {
            sb.AppendLine();
            sb.AppendLine("=== item described from its data ===");
            sb.AppendLine(Guard("describe", () => DescribeHoveredItem(hover)) ?? "(none)");
        }

        string path = WriteDump("hover", sb);
        LogMessage("PoE1Access hover dump: " + path);
        Say("Hover dump saved. " + count + " elements.", true);
    }

    private void DumpElement(StringBuilder sb, Element e, int depth, int maxDepth, int index, ref int count)
    {
        if (e == null || count >= MaxDumpNodes) return;
        count++;

        sb.Append(' ', depth * 2);
        sb.Append('[').Append(index).Append("] addr=0x").Append(Try(() => e.Address, 0L).ToString("X"));
        sb.Append(" type=").Append(Try(() => e.Type.ToString(), "?"));
        sb.Append(" visible=").Append(Try(() => UiText.VisibleLocal(Mem, e), false));
        sb.Append(" kids=").Append(Try(() => e.ChildCount, 0L));

        try
        {
            var r = e.GetClientRect();
            sb.Append(" rect=").Append((int)r.X).Append(',').Append((int)r.Y)
              .Append(' ').Append((int)r.Width).Append('x').Append((int)r.Height);
        }
        catch { }

        string name = UiText.Name(Mem, e);
        if (name.Length > 0) sb.Append(" name=").Append(name);
        string title = UiText.Title(Mem, e);
        if (title.Length > 0) sb.Append(" title=\"").Append(title).Append('"');
        string text = CleanText(UiText.Text(Mem, e));
        if (text.Length > 0) sb.Append(" text=\"").Append(text).Append('"');

        string texture = Try(() => e.TextureName, "");
        if (!string.IsNullOrEmpty(texture)) sb.Append(" texture=").Append(texture);
        sb.AppendLine();

        if (depth >= maxDepth) return;

        IList<Element> kids = Try<IList<Element>>(() => e.Children, null);
        if (kids == null) return;

        int i = 0;
        foreach (Element child in kids)
        {
            DumpElement(sb, child, depth + 1, maxDepth, i, ref count);
            i++;
        }
    }

    private static string WriteDump(string label, StringBuilder sb)
    {
        string dir = PluginLog.Folder;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, label + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        PluginLog.Write("Saved " + path);
        return path;
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private void Say(string text, bool interrupt)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaxSpeechChars) text = text.Substring(0, MaxSpeechChars);

        // News that cannot be cut off holds for 1.5 s: other speech queues behind it, except what
        // the player just asked for (SpeechPriority.cs).
        if (interrupt && !_fromInput && _clock.ElapsedMilliseconds < _holdUntilMs) interrupt = false;
        _lastSpoken = text;
        PluginLog.Write("Said: " + text.Replace("\r", " ").Replace("\n", " | "));
        _speech.Speak(text, interrupt);
    }

    // Runs an action and reports (rate-limited) instead of letting an exception reach the HUD.
    private void Guard(string where, Action action)
    {
        long start = _clock.ElapsedMilliseconds;
        try { action(); }
        catch (Exception ex) { Report(where, ex); }
        NoteSlow(where, start);
    }

    // A step that holds up the game's frame for long is logged (at most every 5 seconds per step),
    // so delays in what is said can be traced to it.
    private readonly Dictionary<string, long> _slowLogged = new Dictionary<string, long>();
    private void NoteSlow(string where, long start)
    {
        long now = _clock.ElapsedMilliseconds, took = now - start;
        if (took < 60) return;
        if (_slowLogged.TryGetValue(where, out long last) && now - last < 5000) return;
        _slowLogged[where] = now;
        PluginLog.Write($"Slow: {where} took {took} ms");
    }

    private T Guard<T>(string where, Func<T> func) where T : class
    {
        try { return func(); }
        catch (Exception ex) { Report(where, ex); return null; }
    }

    private void Report(string where, Exception ex)
    {
        long now = _clock.ElapsedMilliseconds;
        if (_errorLastMs.TryGetValue(where, out long last) && now - last < 10000) return;
        _errorLastMs[where] = now;
        PluginLog.Write("ERROR in " + where + ": " + ex);
        LogError("PoE1Access " + where + ": " + ex.GetType().Name + ": " + ex.Message);
    }

    // Memory reads can fail while the UI is changing; return a fallback instead of throwing.
    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    // Removes the game's colour tags and braces, and collapses whitespace so speech flows.
    private static string CleanText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOf("<<set:", StringComparison.Ordinal) >= 0) s = GrammarRegex.Replace(s, "");
        // Controller prompts: "ok <<xbox_button_a>>" -> "ok, A button".
        if (s.IndexOf("<<", StringComparison.Ordinal) >= 0)
            s = ControllerRegex.Replace(s, m => ", " + PromptName(m.Groups[1].Value) + " ");
        if (s.IndexOf('<') >= 0) s = TagRegex.Replace(s, " ");
        s = s.Replace("{", "").Replace("}", "");
        // Keyword links in mod texts: "[PlayerItemRarity|Item Rarity]" -> "Item Rarity",
        // "[Spell]" -> "Spell".
        if (s.IndexOf('[') >= 0) s = KeywordRegex.Replace(s, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);

        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || c == ' ')
            {
                space = sb.Length > 0;
            }
            else
            {
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
        }
        // "> Connecting to login server..." (the login screen's log lines): the mark goes.
        return sb.ToString().Replace(" ,", ",").TrimStart(',', ' ', '>').TrimStart();
    }

    // Joins parts into one phrase, with a pause between them.
    private static string JoinSpeech(List<string> parts)
    {
        var sb = new StringBuilder();
        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (sb.Length > 0)
            {
                char last = sb[sb.Length - 1];
                bool ended = last == '.' || last == ':' || last == ',' || last == '!' || last == '?';
                sb.Append(ended ? " " : ". ");
            }
            sb.Append(part.Trim());
        }
        return sb.ToString();
    }
}
