# Path of Exile Access (PoE1Access)

An accessibility mod that makes **Path of Exile 1** playable without seeing the screen. It reads
menus, items, panels and the passive tree through your screen reader (NVDA, JAWS and others,
with braille), and adds sounds to find your way and to  engage in combat.

It runs on [Exile API](https://github.com/exApiTools/ExileApi-Compiled), which reads the game's
memory. The mod only reads: it never changes the game and never presses anything for you.

> **Warning:** this is in beta, it could be unstable, and not all menus may read properly.   Grinding Gear Games has not approved it. Use it at
> your own risk. It can fail, especially right after a game patch.

## What it does

- **Menus:** login screen, character selection and creation (with the class each Exile is as
  you move between them), the pause menu and the options (rows, tabs, checkbox, slider and
  dropdown values), read as the controller selection moves.
- **Any client language:** windows, prompts and names are found by the game's own words, so the
  mod works with the game in any of its languages.
- **Inventory and equipment:** each item with its mods, gem and currency descriptions; every
  equipment slot, including empty ones; what you pick up, place and equip.
- **Character sheet and skills panel:** every stat, stepped through one by one.
- **Passive tree:** each node's type, state, what it does, how many points away it is, and the
  nodes linked to it with their direction on screen.
- **Navigation:** a wall radar, unexplored-ground pips, a scanner of exits, waypoints,
  characters, enemies, items, chests and unexplored ground, and a guide with turn-by-turn
  prompts.
- **Sounds:** enemies (growl, buzz for magic and rare, ring for unique), NPCs and players,
  items on the ground with the game's own drop sound for each kind of item (played from your
  installed game; the game's unique drop over uniques, a rarity tone over magic and rare), a targeting blip and a melee reach ping, and a dodge
  warning (a sharp click and two quick metal tings) from the side of a rare or unique
  monster starting an attack aimed at you, or of any monster close by if you choose. Things
  worth walking to sound from their side, faster as you get closer: chests (a wooden knock),
  shrines (a shimmer), the nearest waypoint (a drone), portal (a swirl), exit (a rush of air)
  and stash (a clunk); in the Covens league, the Coven's Cauldrons and the boss cauldron (a
  bubbling), listed in the scanner with the Wickerman. A harsh repeating cue warns while you stand in harmful ground, and each
  ailment you put on an enemy (frozen, chilled, ignited, shocked, poisoned, bleeding, cursed) has
  a short sound of its own from its side. The settings menu's Sound glossary plays every sound
  with what it means. All the mod's own sounds are synthesized; loot, chests and the stash can
  use a sound pack of your own.
- **Alerts:** your harmful states as they begin and end ("Frozen", "No longer frozen",
  "Cursed: Temporal Chains"), frenzy, power and endurance charges as they change, a boss's life
  as it falls past 75, 50, 25 and 10 percent and when it is defeated, and each level up.
- **Speech:** enemies coming into view with their direction, the targeted enemy, life, mana and
  energy shield as they fall past 75, 50 and 25 percent, your level and experience.

**Your own sound pack:** a `sounds` folder you make beside `PoE1Access.exe` gives loot, chests
and the stash sounds of your own: loops
from the nearest items (`gold`, `potion`, `normal/magic/rare/legendary equipment`, `quest item`)
and repeating sounds (`gem`, `scroll`, `rune`, `chest`, `corpse`, `weapon rack`, `stash`), as
.ogg or .wav files. No sounds of this kind come with the mod.

The full player guide, with every control and setting, is [Launcher/README.txt](Launcher/README.txt)
(it ships as README.txt in the download).

## Languages

The mod works with the game in any of its languages: it finds windows and prompts by the
game's own words, read from the game's data in the client's language, and names classes the
way the game does. It also speaks its own phrases in the game's language: English and Spanish
are written in the code, and Brazilian Portuguese, French, German, Russian, Thai, Japanese,
Korean and Traditional Chinese come from the tables in `PoE1Access/lang/`. The language is the
one set in the game's options (it takes effect when the game and the mod start again).

To add or fix a translation, edit `PoE1Access/lang/<code>.json` ({ "English phrase": "translation" },
keeping placeholders such as `{0}`), then run `py tools/localization/phrases.py check`, which
reports missing, stale and mismatched entries. New phrases in the code are written as
`T("English", "Spanish")`; `py tools/localization/phrases.py missing <code>` lists what a table
still lacks.

## Starting with the game from Steam

Set Path of Exile's launch option in Steam (Properties, General, Launch Options) to
`"<the mod's folder>\PoE1Access.exe" %command%` and the mod starts whenever the game is started
from Steam: Steam runs the launcher, which starts the game from Steam's command and everything
else as usual. The launcher offers to copy that line to the clipboard the first time it runs
(`PoE1Access.exe --steam-setup` offers it again), and keeps it in `app\steam-launch-option.txt`.

## Tutorial

An optional tutorial is the first entry of the mod menu (`PoE1Access/Tutorial.cs`): ten short
lessons on what the mod adds, read page by page, some ending with "Try it now", which closes the
menu and waits for the player to use the mod's own L3 controls, saying when each is done. Like
the rest of the mod it only reads and listens, and leaves the game's own buttons to the game's
button prompts (hold L3), so it does not go out of date with the game. Each new character is
asked whether it wants the lessons to start by themselves (tap L3 yes, double tap no, hold no and
stop asking); each character hears them from the start. The menu's last two entries turn them on
or off for the character you play, and the question for new characters back on.

## Following a Path of Building build

`PoE1Access/BuildPlan.cs` unpacks a Path of Building build code (zlib-compressed XML in URL-safe
base64), from the clipboard or a pobb.in or pastebin link: class, ascendancy, every passive tree
and skill set, gems, gear, bandit and notes. `PoE1Access/BuildFollow.cs` compares it with your
character: a "Your build" list in the passive tree finder, the next passive on levelling up, your
gems against each skill setup, and passives off the build. Everything is in the mod menu under
"Build to follow (Path of Building)". Nothing is allocated or socketed for you.

## Game mechanics and the trials

`PoE1Access/Mechanics.cs` holds the tutorial's Game mechanics lessons (the game's own systems,
in the order the campaign brings them in, each with how it works with the mod) and one-time tips
when the player first meets one (kept in `app\mechanics-seen.txt`). `PoE1Access/Labyrinth.cs`
handles the Trials of Ascendancy and the Labyrinth: written before reaching one, it matches
objects by words in their paths (traps get a Traps scanner kind and a ticking cue; plaques,
levers, keys and Darkshrines go under Objects) and logs every one found as `Labyrinth object:
<path> kind=... type=... states=...`, to be made exact from the first test.

## Menus the mod has not seen

Menus are read without knowing them: the focus reader follows the game's own selection marker
in every element on screen, and the tooltip catcher (`PoE1Access/TooltipCatcher.cs`) reads
whatever text appears on screen right after the selection moves, outside the selected element
and the button prompts, as its tooltip. A tooltip box the game keeps on screen and rewrites is
read again when its text changes. A selection with no words is named from its picture (an item,
skill or other art file) when it has one, and logged once as `Unread selection: [path] name=...
texts=... pictures=...`, so a screen added by a game update can be fixed from a log.

## Controls

The mod only reads the controller. It cannot stop the game from seeing a press, so its
controls use L3 (clicking the left stick) and the right stick while L3 is held, which the game
leaves free in play. Every keyboard key can be changed in Exile API's menu (F12), under
PoE1Access. The launcher keeps Exile API's window hidden; start `PoE1Access.exe --show-exileapi` to use
that menu.

### Mod controls in play

- **Tap L3** (Insert): where the thing you chose in the scanner is now.
- **Double tap L3** (F1): your level and how far you are to the next, life, mana, energy shield,
  flask uses, the area and its level, how much you have explored, and the area's description.
- **Triple tap L3** (Delete): the guide to the chosen thing, on or off.
- **Hold L3 + right stick up or down** (Page Up, Page Down): the scanner's previous or next
  category (exits, waypoints, characters, other players, enemies, items, chests, unexplored
  ground, quest, where you died).
- **Hold L3 + right stick left or right** (Home, End): the previous or next thing in that
  category, nearest first.
- **Hold L3 for half a second**: the game's button prompts for what is around you (pick up,
  talk, and so on).
- **Hold L3 for a second and a half** (hold F3 for a second): the mod's settings menu.
- **F9**: repeat the last thing said.

### Mod controls in windows and menus

- **Tap L3**: the next line of the selected item or tooltip. **Double tap L3**: the previous
  line.
- **Hold L3 for half a second**: the whole item or tooltip, or the window's controls when there
  is nothing to read.
- **Hold L3 for a second and a half** in the inventory: every item in the bag, or every
  equipment slot when the selection is on the equipment.
- **F5, F6**: previous, next item in your inventory. **F7**: switch between inventory and
  equipment.
- **Character sheet**: hold L3 + right stick up and down for each stat, left and right for each
  section.
- **Skills panel**: hold L3 + right stick left and right for each skill on your skill bar, up and
  down for its details.
- **World map** (from a waypoint): move the selection to hear each place; tap L3 for its
  description.
- **Passive tree**: each node says its links; tap L3 for the next line, double tap for the
  previous, hold L3 for everything about the node.
- **Passive tree finder (D-pad)**: up and down choose a list (Position, Keystones, Notables,
  Jewel sockets, Next to your nodes); left and right go through it, fewest points first. The
  node named becomes the target: every node you move to then says which way it is, and, while it
  is not linked yet, the next node to allocate on the cheapest way there ("Allocate this one").
  Position drops the target. The finder never allocates anything for you.

### Mod settings menu

- **Right stick up and down** (arrow keys): move.
- **Right stick left and right**: open a submenu, turn a switch on or off, or change a value in
  steps of 5. A volume plays its sound as it changes.
- **Tap L3** (Backspace): back up one level. **Double tap L3**, or hold F3 again: close.
- **Describe how each class plays** (Speech): on the class screen, each class's name is
  followed by how it plays: its attributes, first weapon and skill, its side of the passive
  tree, and how that suits the mod's sounds. Moving to the next class cuts it.
- **Speech priority**: for each kind of news the mod says by itself (harmful states beginning and
  ending, low life and mana, a boss's life, enemy names, quest route, things you walk up to,
  changes in your bag, arriving with the guide, charges, level up), whether it waits its turn,
  cuts in when urgent, cuts in, or cuts in and cannot be cut off (other news waits 1.5 seconds
  behind it). What you ask for with the controller always cuts in. As in Poe2Access.
- **Sound glossary** (last submenu): every sound the mod makes, with what it means; right stick
  left or right plays it.

### Game controls (controller)

These are the game's own controls, as it shows them on screen with a controller. The mod reads
the game's button prompts as they appear, so the prompts of each window and menu are said as
you open them. Hold L3 to hear them again.

- **Left stick**: move, and move the selection in menus and windows.
- **A, B, X, Y and the D-pad**: the skills on your skill bar, one per button.
- **R3** (clicking the right stick): skill binding, to choose which skill each button uses.
  - **Right stick**: move between the buttons; each one says the skill on it, or "empty".
  - **A**: the skills you can put on that button. Move with the right stick to hear each one with
    its details and its place in the list ("2 of 3"). A puts it on the button.
  - **B**: back, then close skill binding.
- **Start**: the game menu (options, log out, exit).
- **Left and right bumper**, in the character windows: switch between Passives, Character,
  Skills, Inventory, Cosmetics and World.
- **Inventory**: the left stick moves the selection; L3 compares items or opens more actions; the
  game names the buttons to pick up, place and close in its prompts, which the mod reads.

## Installing

1. Download the latest release zip from the Releases page and unzip it anywhere.
2. Double-click `PoE1Access.exe`.
3. The first time, it asks to download Exile API from its GitHub page. Exile API is not part of
   this package. On every start it checks for a newer Exile API and updates it, since Exile API
   is updated after each game patch.
4. On every start it also installs the newest PoE1Access release from this page, then starts
   again (versions 0.2.2 and later).

You need Windows 10 or 11 (64-bit), the .NET 10 Desktop Runtime (x64), and Path of Exile 1 in
windowed or windowed fullscreen mode. A controller is recommended: Xbox (XInput) controllers
and PlayStation controllers (DualSense, DualSense Edge, DualShock 4) are read.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -ExileApi "C:\path\to\ExileApi-Compiled-master"
```

The Exile API folder is only used to compile against `ExileCore.dll`. The script builds:

- `PoE1Access/`: the Exile API plugin (`app\plugin`)
- `Reader/`: reads the login and character screens before Exile API starts (`app\reader`)
- `Launcher/`: `PoE1Access.exe`, a single self-contained file that sets up and starts everything

and writes `dist\PoE1Access` and `dist\PoE1Access.zip`.

## License

Path of Exile Access is free software under the [GNU General Public License v3.0](LICENSE).

It bundles [Prism](https://github.com/ethindp/prism) 0.18.3 for screen reader output, under its
own licenses (MPL-2.0 and those in `third_party/prism/LICENSES`). Exile API is not included.

## Credits

- [Exile API](https://github.com/exApiTools/ExileApi-Compiled), which reads the game.
- [Prism](https://github.com/ethindp/prism), screen reader and braille output.
- [Path of Exile 2 Access](https://github.com/Ali-Bueno/poe2-access): the navigation design, the
  controls and the sound designs follow it. Its synthesized sounds are rebuilt here from its
  documentation, and no code or recordings are copied.
