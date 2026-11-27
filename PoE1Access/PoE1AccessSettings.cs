using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace PoE1Access;

// Options shown in the ExileApi menu (F12). ExileApi saves the values itself.
// The class is called PoE1AccessSettings (not Settings) so it cannot clash with the
// BaseSettingsPlugin.Settings property.
public class PoE1AccessSettings : ISettings
{
    // Mandatory: lets ExileApi switch the plugin on and off.
    public ToggleNode Enable { get; set; } = new ToggleNode(true);

    // Say "Inventory open", "Passive tree closed" and so on.
    public ToggleNode AnnouncePanels { get; set; } = new ToggleNode(true);

    // Speak the menu element, item or passive node under the cursor (or controller focus).
    public ToggleNode ReadOnHover { get; set; } = new ToggleNode(true);

    // Speak the menu item selected with a controller or the keyboard.
    public ToggleNode ReadFocus { get; set; } = new ToggleNode(true);

    // Set once the mod has said where the tutorial is (the first game with the mod).
    public ToggleNode TutorialOffered { get; set; } = new ToggleNode(false);

    // Lessons start by themselves as you meet each part of the game, once you said yes (AutoTutorial.cs).
    public ToggleNode AutoTutorials { get; set; } = new ToggleNode(true);

    // A short tip the first time you meet a game mechanic (a town, a trial, the Labyrinth...).
    public ToggleNode MechanicTips { get; set; } = new ToggleNode(true);

    // Maps and speed (Atlas.cs, Mapping.cs).
    public ToggleNode FollowLootFilter { get; set; } = new ToggleNode(true);
    public ToggleNode ExploreMode { get; set; } = new ToggleNode(true);
    public ToggleNode MapModWarnings { get; set; } = new ToggleNode(true);
    public ToggleNode AtlasFinder { get; set; } = new ToggleNode(true);
    // The way through the areas to your quest, once a character has finished the campaign (QuestRoute.cs).
    public ToggleNode QuestRoute { get; set; } = new ToggleNode(true);
    // How each class plays, after its name on the class screen (ClassDescriptions.cs).
    public ToggleNode ClassDescriptions { get; set; } = new ToggleNode(true);

    // How long the hovered thing must stay the same before it is spoken (milliseconds).
    // Raise it if you hear too many half-finished announcements.
    public RangeNode<int> HoverDelayMs { get; set; } = new RangeNode<int>(150, 0, 1000);

    // Read the tooltip that appears beside a menu entry or button when it is selected (any
    // menu: see TooltipCatcher). Items, gems and passives are read from their data either way.
    public ToggleNode SpeakTooltips { get; set; } = new ToggleNode(true);

    // Describe hovered inventory items from their data (name, rarity, mods) instead of
    // from the tooltip text.
    public ToggleNode ItemsFromComponents { get; set; } = new ToggleNode(true);

    // ---- Navigation ----

    // Wall radar: tock = wall, breath = open way, tin = exit, on each screen side.
    public ToggleNode Radar { get; set; } = new ToggleNode(true);
    public RangeNode<int> RadarVolume { get; set; } = new RangeNode<int>(100, 0, 200);
    // Every sound of the mod, on top of each kind's own volume.
    public RangeNode<int> MasterVolume { get; set; } = new RangeNode<int>(100, 0, 300);
    // Speech speed for voices the mod can set (the Windows voice); screen readers keep their own.
    public RangeNode<int> SpeechRate { get; set; } = new RangeNode<int>(50, 0, 100);
    public RangeNode<int> GuideVolume { get; set; } = new RangeNode<int>(100, 0, 200);

    // Radar: two pips after an open way or exit that leads to ground you have not seen yet,
    // one pip when that unseen ground is a small dead end.
    public ToggleNode UnexploredMark { get; set; } = new ToggleNode(true);

    // Name what you walk up to (NPCs, waypoints, exits, chests, items...), once.
    public ToggleNode AnnounceNearby { get; set; } = new ToggleNode(true);

    // While guiding, say each turn about half a second before it ("Now up, 5").
    public ToggleNode RoutePrompts { get; set; } = new ToggleNode(true);

    // Items on the ground keep sounding while within about a screen, each kind with its own
    // cue (currency, gem, flask, rare, unique...): from the side they are on, higher when up the
    // screen, more often as you get closer.
    public ToggleNode LootSounds { get; set; } = new ToggleNode(true);
    // Which items sound: 0 = all, 1 = magic or better (plus currency, gems, quest items...),
    // 2 = only valuable ones (currency, gems, quest items, maps, cards, rares and uniques).
    public RangeNode<int> LootFilter { get; set; } = new RangeNode<int>(0, 0, 2);

    // Speech priority (SpeechPriority.cs): 0 waits, 1 cuts in when urgent, 2 cuts in, 3 cuts in
    // and cannot be cut off for 1.5 s. Defaults as in Poe2Access.
    public RangeNode<int> SpeechStatusBegins { get; set; } = new RangeNode<int>(3, 0, 3);
    public RangeNode<int> SpeechStatusEnds { get; set; } = new RangeNode<int>(2, 0, 3);
    public RangeNode<int> SpeechLifeAndMana { get; set; } = new RangeNode<int>(1, 0, 3);
    public RangeNode<int> SpeechBossLife { get; set; } = new RangeNode<int>(1, 0, 3);
    public RangeNode<int> SpeechEnemyNames { get; set; } = new RangeNode<int>(1, 0, 3);
    public RangeNode<int> SpeechQuest { get; set; } = new RangeNode<int>(0, 0, 3);
    public RangeNode<int> SpeechPlaces { get; set; } = new RangeNode<int>(0, 0, 3);
    public RangeNode<int> SpeechBag { get; set; } = new RangeNode<int>(0, 0, 3);
    public RangeNode<int> SpeechArrivals { get; set; } = new RangeNode<int>(3, 0, 3);
    public RangeNode<int> SpeechCharges { get; set; } = new RangeNode<int>(0, 0, 3);
    public RangeNode<int> SpeechLevelUp { get; set; } = new RangeNode<int>(2, 0, 3);
    public RangeNode<int> LootVolume { get; set; } = new RangeNode<int>(60, 0, 200);

    // Enemies: a growl for each enemy on screen from its side, faster as it gets closer (a buzz
    // on top for magic and rare, a ring for unique).
    public ToggleNode EnemySounds { get; set; } = new ToggleNode(true);
    public RangeNode<int> EnemyVolume { get; set; } = new RangeNode<int>(70, 0, 200);
    // Name enemies as they come into view, a pack in one line: "Zombie, 3, up right".
    public ToggleNode AnnounceEnemies { get; set; } = new ToggleNode(true);
    // A sharp click-and-ting cue from the side of a monster starting an attack aimed at you.
    public ToggleNode DodgeWarnings { get; set; } = new ToggleNode(true);
    // Off: rare and unique monsters (bosses) only. On: every monster close to you.
    public ToggleNode DodgeWarningsAll { get; set; } = new ToggleNode(false);
    // Chests, shrines, the nearest waypoint, portal, exit and stash, each with a sound from its side.
    public ToggleNode ObjectSounds { get; set; } = new ToggleNode(true);
    // Loot plays the game's own drop sounds (from your installed game) instead of the mod's.
    public ToggleNode GameLootSounds { get; set; } = new ToggleNode(true);
    // Your own sound files for loot, chests and the stash (a "sounds" folder beside PoE1Access.exe,
    // see SoundPack), used when they are there.
    public ToggleNode SoundPackSounds { get; set; } = new ToggleNode(true);
    // The quick double tap of another player near you (NPCs keep theirs with NpcSounds).
    public ToggleNode PlayerSounds { get; set; } = new ToggleNode(true);
    // A sound of its own for each ailment you put on an enemy (frozen, ignited, shocked...).
    public ToggleNode EnemyStateSounds { get; set; } = new ToggleNode(true);
    // Your harmful states as they begin and end ("Frozen", "No longer frozen"), with a short buzz.
    public ToggleNode StatusAlerts { get; set; } = new ToggleNode(true);
    // A repeating harsh cue while you stand in harmful ground (burning, caustic...).
    public ToggleNode HarmfulGroundSound { get; set; } = new ToggleNode(true);
    // Frenzy, power and endurance charges: a pair of notes and the count as they change.
    public ToggleNode ChargeAlerts { get; set; } = new ToggleNode(true);
    // A boss's life as it falls past 75, 50, 25 and 10 percent, and "defeated".
    public ToggleNode AnnounceBossLife { get; set; } = new ToggleNode(true);
    // Level up: three rising notes and the new level.
    public ToggleNode AnnounceLevelUp { get; set; } = new ToggleNode(true);
    // Describe each area on a first visit in the session (size, openness, waypoint, exits).
    public ToggleNode DescribeAreas { get; set; } = new ToggleNode(true);
    // In the passive tree, the D-pad finds keystones, notables, jewel sockets and nodes to
    // allocate, and guides to the one chosen.
    public ToggleNode PassiveFinder { get; set; } = new ToggleNode(true);
    // Say which way those packs are (up, down left...).
    public ToggleNode EnemyDirections { get; set; } = new ToggleNode(true);
    // Two short blips when the game's target lands on a living enemy.
    public ToggleNode CrosshairBeep { get; set; } = new ToggleNode(true);
    // Name the targeted enemy, with its life when hurt.
    public ToggleNode AnnounceTarget { get; set; } = new ToggleNode(true);

    // Two quick rising notes from an enemy's side when it comes within melee reach (the gap
    // between its body and yours, in world units); again only once every enemy has stepped
    // back past that distance plus 25.
    public ToggleNode ReachPing { get; set; } = new ToggleNode(true);
    public RangeNode<int> ReachDistance { get; set; } = new RangeNode<int>(40, 5, 150);

    // NPCs and other players: a light wooden tap from their side (players a quicker double tap).
    public ToggleNode NpcSounds { get; set; } = new ToggleNode(true);

    // Say life, mana and energy shield as they fall below 75, 50 and 25 percent.
    public ToggleNode AnnounceResources { get; set; } = new ToggleNode(true);

    // Controller commands on L3 (tap, double tap, triple tap, hold + right stick).
    public ToggleNode ControllerCommands { get; set; } = new ToggleNode(true);
    // Read a PlayStation controller (DualSense, DualShock 4) for the mod's L3 controls, when no
    // XInput controller is there. Off leaves the controller to the game and Steam alone.
    public ToggleNode ReadPlayStationController { get; set; } = new ToggleNode(true);

    // Keyboard scanner and guide.
    public HotkeyNodeV2 ScanPrevCategoryHotkey { get; set; } = new HotkeyNodeV2(Keys.PageUp);
    public HotkeyNodeV2 ScanNextCategoryHotkey { get; set; } = new HotkeyNodeV2(Keys.PageDown);
    public HotkeyNodeV2 ScanPrevHotkey { get; set; } = new HotkeyNodeV2(Keys.Home);
    public HotkeyNodeV2 ScanNextHotkey { get; set; } = new HotkeyNodeV2(Keys.End);
    public HotkeyNodeV2 WhereHotkey { get; set; } = new HotkeyNodeV2(Keys.Insert);
    // Your gold and currency (also hold L3 + right stick down in a window); no key unless set.
    public HotkeyNodeV2 CurrencyHotkey { get; set; } = new HotkeyNodeV2(Keys.None);
    public HotkeyNodeV2 GuideHotkey { get; set; } = new HotkeyNodeV2(Keys.Delete);
    public HotkeyNodeV2 StatusHotkey { get; set; } = new HotkeyNodeV2(Keys.F1);

    // Hear the last thing spoken again.
    public HotkeyNodeV2 RepeatHotkey { get; set; } = new HotkeyNodeV2(Keys.F9);

    // Item browser: step through your inventory or equipment, one item per key press.
    public HotkeyNodeV2 PreviousItemHotkey { get; set; } = new HotkeyNodeV2(Keys.F5);
    public HotkeyNodeV2 NextItemHotkey { get; set; } = new HotkeyNodeV2(Keys.F6);
    public HotkeyNodeV2 SwitchListHotkey { get; set; } = new HotkeyNodeV2(Keys.F7);

    // Developer tools. Each saves a text file into the PoE1Access_dumps folder.
    //   F8  = every inventory and equipment item, with its data
    //   F10 = which panels are open, and the UI tree under them
    //   F11 = the element under the cursor, its tooltip, and what the reader says about it
    public HotkeyNodeV2 DumpItemsHotkey { get; set; } = new HotkeyNodeV2(Keys.F8);
    public HotkeyNodeV2 DumpPanelsHotkey { get; set; } = new HotkeyNodeV2(Keys.F10);
    public HotkeyNodeV2 DumpHoverHotkey { get; set; } = new HotkeyNodeV2(Keys.F11);

    // Developer: F3 = text probe. Hover an inventory item, press it, and it saves a file
    // showing where the game really stores on-screen text (ExileApi's own text reading is off).
    public HotkeyNodeV2 TextProbeHotkey { get; set; } = new HotkeyNodeV2(Keys.F3);

    // Developer: F2 = focus probe. Press it, move the menu selection one step, press it again.
    // Each file shows what changed, so we can find how the game marks the selected item.
    public HotkeyNodeV2 FocusProbeHotkey { get; set; } = new HotkeyNodeV2(Keys.F2);

    // Developer: F4 = change logging on/off. While on, every change in the visible UI is written
    // to PoE1Access_changes.txt (used to find checkbox, slider and dropdown values).
    public HotkeyNodeV2 ChangeLogHotkey { get; set; } = new HotkeyNodeV2(Keys.F4);
}
