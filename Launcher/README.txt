PATH OF EXILE ACCESS - TEST VERSION

A test mod that makes Path of Exile 1 playable without seeing the screen. It reads menus,
items, panels and what is around you through your screen reader, and adds sounds to find your
way: a wall radar, unexplored-ground pips, and a guide to whatever you choose.

Items on the ground keep making a sound while they are within about a screen of you, from the
side they are on, higher when up the screen, and more often as you get closer. Each item plays
the game's own drop sound for its kind (an orb, a scroll, a gem, a flask, a ring, a helm, metal
or leather armour, a sword...), played from your installed game with the game's own sound
library; nothing of the game's is copied. A unique adds the game's unique drop sound, a rare or
magic item the mod's rarity tone (three notes for rare, one for magic). "Loot uses the game's own
drop sounds" in the F3 menu (GameLootSounds) switches back to the mod's own sounds, which are
also used if the game's cannot be played. You can turn loot sounds off (LootSounds), sound only
magic-or-better or only valuable items (LootFilter 1 or 2), or change their volume (LootVolume).
Other players near you make a quick double tap (PlayerSounds, "Other player sounds" in the F3
menu), and the scanner lists them under Other players.

Enemies within about a screen growl from the side they are on (the Poe2Access enemy sounds,
rebuilt): a short low growl, repeated faster as they get closer, lower in pitch when they are
below you; magic and rare enemies buzz on top, uniques ring like struck metal. The four that
matter most sound (uniques first, then magic and rare, then the nearest); enemies behind walls
stay quiet, uniques too. When an enemy comes within melee reach, two quick rising notes play
from its side (ReachPing, ReachDistance); they play again once every enemy has stepped back.
When a rare or unique monster starts an attack or spell aimed at you, a dodge warning plays from
its side: a sharp click and two quick metal tings, louder the closer it is
(DodgeWarnings). "Dodge warning for every enemy" in the F3 menu adds every monster within a few
steps (DodgeWarningsAll).
Things worth walking to sound from their side, faster and louder as you get closer, within about
two screens (ObjectSounds): the three nearest unopened chests in sight (a low wooden knock), the
nearest shrine you can still take (a shimmer), and the nearest waypoint (a droning hum), portal
(a rising swirl), exit (a soft rush of air) and stash (a lid's clunk). In the Covens league, a
Coven's Cauldron whose Fate you have not chosen and the boss cauldron bubble. The scanner's
Objects list has the cauldrons ("Coven's Cauldron, guarded by the coven" until you beat the
coven, then "choose a Fate", then "Fate chosen"), the boss cauldron and the Wickerman in town.
The Fate window at a cauldron is read: its title and count, then each card as you move to it,
with its tier, what it does to the boss and its reward ("Wildfire, tier 1. Creates walls of
blackfire. Reward: Empowered Monster will drop additional Rare Melee Weapons, 1 of 3"). Hold L3
to hear the selected card again.
While you stand in harmful ground (burning, chilled, shocked, tarred, desecrated or caustic
ground, a vortex, spores, a monster's degeneration aura) two harsh low stabs repeat, and its name
is said the first time (HarmfulGroundSound). Your harmful states are said as they begin and end
with a short buzz: "Frozen", "No longer frozen", ignited, chilled, shocked, poisoned, bleeding,
maimed, blinded, hindered, "Cursed: Temporal Chains" (StatusAlerts). Frenzy, power and endurance
charges play a rising or falling pair of notes and say the count (ChargeAlerts). A boss's life is
said as it falls past 75, 50, 25 and 10 percent, and "defeated" (AnnounceBossLife); a level up
plays three rising notes and says the level (AnnounceLevelUp). Each ailment you put on an enemy in
sight has a short sound of its own from its side (EnemyStateSounds): frozen an icy ring, chilled a
glassy note, ignited a crackle, shocked a falling zap, poisoned a bubbling pair, bleeding a wet
drip, cursed a low swell.
The two nearest NPCs make a light wooden tap from their side, and
the nearest other player a quicker, higher double tap (NpcSounds). Life, mana and energy shield
are spoken as they fall below 75, 50 and 25 percent (AnnounceResources). Double tap L3 for your
level and how far you are to the next one ("Level 1, 65 percent of the way to level 2"), then life, mana and the area. Enemies are named once as they come into view, a
pack in one line with the way to its middle: "Zombie, 3, up right. Rhoa, rare, left". When the
game's target lands on a living enemy you hear two quick blips and its name, with its life when
hurt. Settings: EnemySounds, EnemyVolume, AnnounceEnemies, EnemyDirections, CrosshairBeep,
AnnounceTarget.

WARNING
This is an unofficial tool. It reads the game's memory through Exile API, but never changes it
and never presses anything for you. Grinding Gear Games has not approved it: use it at your own
risk. It is a test version and it can fail.

WHAT YOU NEED
- Windows 10 or 11, 64-bit.
- Path of Exile 1 (Steam or standalone), in windowed or windowed fullscreen mode.
- The Microsoft .NET 10 Desktop Runtime, x64. The mod tells you if it is missing and opens the
  download page: https://dotnet.microsoft.com/download/dotnet/10.0
- The Microsoft Visual C++ Redistributable (most computers have it).
- A screen reader. NVDA, JAWS and others work, with braille displays too; without one, the
  Windows voice is used.
- A controller is recommended (the game's controller mode is what the mod is built around).

GETTING STARTED
1. Unzip this folder anywhere.
2. Double-click PoE1Access.exe.
3. The first time, it asks to download Exile API, the program that reads the game, from
   https://github.com/exApiTools/ExileApi-Compiled. Exile API is not part of this package.
   It also moves Exile API's own extra plugins (some of which press keys or click for you) into
   app\ExileApi\Plugins\Disabled. You can move any of them back to Plugins\Compiled.
4. It starts Path of Exile through Steam if it is not running (with the standalone client,
   start the game first), then Exile API.
5. The login screen, character selection and character creation are read from the start. Exile
   API itself only starts once a character is in the game; until then the mod reads those
   screens on its own (app\reader). Password boxes are never read aloud or logged.
6. Exile API's own window (its menu, notifications and messages drawn over the game) is kept
   hidden, out of Alt+Tab, and from taking the focus from the game. To use its menu (F12), for
   example to change a key, start PoE1Access.exe --show-exileapi.

When you close the game, the mod closes too.

STARTING WITH THE GAME FROM STEAM
The mod can start by itself whenever you start Path of Exile from Steam, so you do not need to
start PoE1Access.exe first. The first time you start PoE1Access.exe, it asks whether to copy
Steam's launch option to the clipboard. Say yes, then in your Steam library open Path of
Exile's Properties, and on the General page paste it into Launch Options. It looks like this,
with the folder you unzipped the mod into:
  "C:\Games\PoE1Access\PoE1Access.exe" %command%
It is also kept in app\steam-launch-option.txt. To be asked again, start PoE1Access.exe
--steam-setup. If you move the mod's folder, paste the new one. To stop it, clear Launch
Options in Steam. Starting PoE1Access.exe yourself still works as before.

LANGUAGES

The mod follows the language set in the game's options, after the game and the mod start again.
Windows, prompts and class names are read in that language, and the mod speaks its own phrases
in it too: English, Spanish, Brazilian Portuguese, French, German, Russian, Thai, Japanese,
Korean or Traditional Chinese. A phrase not yet translated is said in English.

YOUR OWN SOUND PACK
You can give loot, chests and the stash sounds of your own. Make a folder named sounds beside
PoE1Access.exe and put your sound files in it (.ogg or .wav; any folders inside it are fine).
The mod does not come with any. The names it looks for: gold
(currency), potion (flasks), normal equipment, magic equipment, rare equipment, legendary
equipment (uniques) and quest item, which loop from the nearest items and get louder as you get
closer; and gem, scroll, rune (jewels), chest, corpse, weapon rack and stash, which repeat from
their side. A name it does not find keeps the mod's or the game's sound. Updates leave the
sounds folder alone. Turn it off with "Loot, chests and stash use your sound pack" in the settings
menu; the Sound glossary plays each one it found.

UPDATES
Each time you start PoE1Access.exe it checks the mod's GitHub page
(https://github.com/Random4358/PoE1Access/releases) and, when there is a newer version, says
"Updating Path of Exile access", installs it in this folder and starts again. Exile API is kept
up to date the same way. Your Exile API settings are kept. Without an internet connection it
simply starts the version you have. After an update it reads what changed (the release notes of
every version since the last one you had), and keeps them in app\whats-new.txt.

TUTORIAL (OPTIONAL)
A few seconds into your first game with the mod, and again with each new character, it asks
whether you want tutorials for that character: tap L3 for yes, double tap L3 for no, or hold
L3 for no and to stop asking new characters. With yes, each lesson starts by itself the first
time you meet what it is about: how the mod works at once, the scanner when you start moving, the sounds when an
enemy first comes near, passive points when you level up, the passive tree, inventory, stash,
character sheet, a vendor and skill binding the first time you open them, gems when one comes
into your bag, life and flasks when your life first runs low, and towns, waypoints, the bandits,
your hideout, the trials, the Labyrinth, resistances, maps and the Covens when you reach them.
While a lesson plays: tap L3 for the next page, double tap to stop it (it comes back later),
hold L3 to hear the page again. Lessons wait while an enemy is near, and each new character hears
them from the start (app\mechanics-seen.txt). At the end of the Tutorial menu, "Tutorials for
this character" turns them on or off for the character you are playing, and "Ask new characters
about tutorials" turns the question back on. Some end with a practice that asks you to use the
mod's controls and says when you have. "Tutorials start as you meet each part of the game" in
the Speech submenu turns this off; every lesson is also in the mod menu (hold L3 for a second
and a half), first entry, Tutorial. The tutorial only reads and listens: it never moves your
character or presses anything for you.

Step by step walks through the busiest screens and tasks with what the mod says at each step:
reading the passive tree, the finder, your inventory and items, socketing a gem, links and
support gems, putting skills on your buttons, vendors, the stash, quest rewards, and the
character sheet. It says what to listen for, and leaves the game's buttons to the game's own
prompts (hold L3 to hear them).

The tutorial's last entry, Game mechanics, explains the game itself, in the order the game
brings each thing in, and how it works with the mod: towns and quests, waypoints and portals,
skill gems and sockets, flasks, passive points, currency and the stash, Act 2's bandits, the
Trials of Ascendancy, the Labyrinth and your Ascendancy, the resistance penalties and the
Pantheon, maps and the Atlas, and this league's Covens. The first time you meet one in play (a
town, a waypoint, a trial's traps, the way to the Labyrinth...), a short tip says what it is,
once for all your characters (app\mechanics-seen.txt). Turn the tips off with "Tips about new
game mechanics" in the Speech submenu.

TRIALS OF ASCENDANCY AND THE LABYRINTH (NOT TESTED YET)
Traps have a scanner kind of their own, Traps, and the two nearest tick from their side within
about a screen, faster as you get closer (a quick metallic double tick). Trial plaques, levers,
keys and Darkshrines are listed under Objects, with the Altar of Ascendancy and the Divine Font; pressure
plates are under Traps. In the Labyrinth, an exit to a chamber says what its name means
("Basilica Annex, a dead end", "Sepulchre Halls, three ways", "Aspirant's Trial, a fight with
Izaro"). This was written before anyone reached a trial
with the mod: the mod writes every trial and Labyrinth object it finds to its log ("Labyrinth
object: ..."), so please send the log after your first trial.

CONTROLS
In game (controller, L3 = left stick click / keyboard):
- Hold L3 + right stick up or down / Page Up, Page Down: scanner category (among them Other
  players, each with their name and level) (Quest lists only what
  the game itself marks with a quest icon on the minimap; it is skipped when there is none;
  "Where you died" holds the spot of your last death in the area you are in, to guide you back;
  exits and quest things are listed (exits heard too) once you have seen where they stand, and
  exits stay listed while you are in their area, however far you go; once you have seen one in an
  area, on any character, it is listed there from then on on every character (kept in
  app\known-places.txt); before that, unexplored ground that holds an exit you have not seen
  yet says "leads to a new exit", and one that holds something for your quest you have not
  seen yet says "leads to something for your quest"; an exit whose area your current quest objective names says
  "your quest leads there")
- Hold L3 + right stick left or right / Home, End: previous or next thing, nearest first
- Tap L3 / Insert: where the chosen thing is now
- Double tap L3 / F1: level and how far you are to the next, life, mana, energy shield, area,
  how much you have explored, and the area's description (its size, how open it is, where you
  are in it, its waypoint and where it leads; also said the first time you enter an area)
- On the character sheet, hold L3 + right stick: up and down for each stat, left and right for
  each section
- In the skills panel, hold L3 + right stick: left and right for each skill on your skill bar,
  up and down for its details (description, speed, cost, cooldown, damage, linked supports)
- Passive tree finder, D-pad (as in Poe2Access): up and down choose a list (Position, Keystones,
  Notables, Jewel sockets, Next to your nodes), left and right go through it, fewest points
  first. The node named becomes the target: each node you move to says which way it lies and,
  while it is not linked, the next node to allocate on the cheapest way ("Allocate this one").
  Position drops the target. Nothing is allocated for you.
- World map (from a waypoint): opening it says the act and your current quest; moving the
  selection says the place and its kind ("Lioneye's Watch, Town"); tap L3 for its flavour text
- Triple tap L3 / Delete: guide to the chosen thing on or off (R3 is left to the game, which
  opens skill binding with it)
- F9: repeat the last thing said
- F5, F6: previous, next item in your inventory; F7: inventory or equipment
Every key can be changed in Exile API's menu (F12), under PoE1Access.

YOUR HIDEOUT (NOT TESTED YET)
Your first hideout comes in Act 2, from Helena once she is rescued from the Chamber of Sins. Go
there from a waypoint's world map (its hideout button). In a hideout everything you can use is
under Objects in the scanner, and decorations are left out. At the crafting bench, each option
is read with its cost and whether you can pay it. The stash is under Objects in towns too.

QUEST ROUTE (AFTER YOUR FIRST CAMPAIGN, NOT TESTED YET)
Once any of your characters has finished the campaign (reached the epilogue or a map), every
character hears the way through the act's areas to the one its quest names, as the world map
shows it: "Quest route: the Coast, then the Tidal Island. Last waypoint on the way: the Coast",
as you enter an area and when the quest changes. Exits you have found on the way say "on your
quest's route", and explore mode tries ground that holds an exit you have not found first. It
never says where something is inside an area before you have found it, as the game does not
show that either. Turn it off with "Quest route" in the Speech submenu.

MAPS AND THE ATLAS (NOT TESTED YET)
- Loot follows the game's loot filter: items it hides make no sound and are not in the scanner.
- Explore mode: guiding to unexplored ground goes on to the next by itself ("Next: ...") until
  "Area explored". Works in every area.
- A map item starts with its tier, Atlas Drops, rarity and pack size, and warns of dangerous
  mods (reflect, no regeneration, no leech, lower maximum resistances, harmful ground...).
- The map device says what is in it as it opens and changes; it is under Objects in a hideout.
- In a map, the monsters left are said at 50, 20, 10, 5 and none.
- With the Atlas open, the D-pad goes through lists of maps (Not completed, Bonus objective left,
  Shaped Regions, Unique maps), each with its base tier, state, region and where it lies.
Each can be turned off in the Speech submenu. In maps the mod logs every kind of object it
finds ("Area object: ..."), so the first maps show what to fix.

FOLLOWING A PATH OF BUILDING BUILD

Path of Building cannot be read by a screen reader, but the builds it shares can. Copy a build
code, or a pobb.in or pastebin link to one (guides give these), then in the mod menu open "Build
to follow (Path of Building)" and choose "Load a build from the clipboard". After that:
- "About the build": the class and ascendancy, the bandit, each skill setup with its gems, the
  gear and the names of its passive trees. "Read the notes" reads the guide's notes.
- In the passive tree, every node says its place in the build: "in the build, from Act 2" (the
  first of the build's trees that takes it), "next in the build", "outside the build" for one
  you took that the build does not, and each node ends with the build's next node and the way to
  it ("Next in the build: Life: down left").
- The D-pad's lists have three more: "Your build" (the build's passives you have not taken,
  fewest points first, opening with how far along you are: "16 of 60 taken, 3 points outside
  the build"), "Taken in the build" and "Taken outside the build".
- A worn piece of gear says, after its name, how it compares with the build's piece for that
  slot ("Build: same base, 3 of 6 modifiers"), and after its own lines each modifier the build
  wants against yours ("+150 to maximum Life, the build asks for 150" with yours first,
  "Missing: ..." or "met"); a unique is named ("not this one"). An empty slot says what the
  build puts there, and opening the inventory says how the worn gear stands. A gem says which of
  the build's skill setups it is for.
- When you level up, the build's next passive is said ("Build: next passive, Life").
- "Your gems against the build": for each skill setup, where you have it and which of its gems
  are missing or not linked with it.
- "Passives you have that the build does not take".
A leveling guide has several trees ("Act 1 End", "End Act 2"...) and skill sets: "Passive tree
to follow" and "Skill set to follow" choose one; by itself the tree is the first with passives
you have not taken yet. The build is kept until you load another or choose "Forget the build".
Nothing is allocated or socketed for you.

With Path of Building Community installed (from its GitHub page; the mod finds it by itself),
two more entries say Path of Building's own numbers, after a few seconds: "Path of Building: the
build's numbers" and "Path of Building: your character's numbers": the main skill's damage per
second, life, energy shield, mana, resistances, armour, evasion, block, effective health, the
biggest hit you survive of each kind, your attributes and any requirement you do not meet. For
your character the mod describes your class, level, passives, worn gear and socketed gems to Path
of Building; jewels, mastery choices, the pantheon and its configuration are not given, so the
numbers are close rather than exact. After the build's numbers, yours are compared with them.
Path of Building's own window is not used and never opens.

CRAFTING YOUR OWN BUILD (needs Path of Building Community installed)

- Triple tap L3 on any item, in your bag, the stash or a merchant's stock: Path of Building says
  what wearing it would change ("Wearing Doom Shell: damage per second +12%, life +30, fire
  resistance minus 20"); a ring is tried in each ring slot. Flasks and jewels are not compared.
- Triple tap L3 on a passive node: what taking it, with the points on the way, would change, or
  for a node you have, what refunding it would.
- On the passive tree, hold L3 and push the right stick right on a node to plan it (with the
  nodes on the way) or take it out of your plan. Nothing is allocated. Planned nodes say
  "planned", and the D-pad's lists have "Your plan".
- The mod menu's "Build crafting (Path of Building)": your plan's numbers against yours, follow
  your plan as your build, copy your plan or your character as a build code (to paste in Path of
  Building or send to a friend), clear your plan, choose which of your skills Path of Building
  calculates, and try any support gem on that skill, added or instead of each support it has.
  The enemy your character's numbers and every comparison face is set as Path of Building sets
  it, by its own names: "Is the enemy a Boss?" (No, the default; Standard Boss, a story or map
  boss; Guardian/Pinnacle Boss; Uber Pinnacle Boss), each answer explained as you choose it; "Is
  the enemy Rare or Unique?" (a boss counts as one by itself); and "Enemy level" (the area's, the
  default, or a level you choose; the bosses' own resistances, armour and evasion come on top).
Each answer takes a few seconds. Nothing is allocated, equipped or socketed for you.

CLASS DESCRIPTIONS

On the class screen of character creation, each class's name is followed by how it plays: its
attributes, the weapon and skill it starts with, its side of the passive tree, and how that
suits the mod's sounds. Moving to the next class cuts it. "Describe how each class plays on the
class screen" in the Speech submenu turns this off.

SPEECH PRIORITY

"Speech priority" in the mod menu sets, for each kind of news the mod says by itself, whether it
waits its turn, cuts in when urgent, cuts in, or cuts in and cannot be cut off (other news waits
1.5 seconds behind it): harmful states beginning (cannot be cut off) and ending (cuts in), low
life and mana (urgent under half life), a boss's life (urgent at 30 percent and when defeated),
enemy names (urgent for the one you aim at and for rares and bosses), quest route, things you
walk up to, changes in your bag, charges (these wait), arriving with the guide (cannot be cut
off) and level up (cuts in). What you ask for with the controller always cuts in.

CURRENCY AND VENDOR PRICES
Vendors price items in currency items (scrolls, orbs and shards), not gold. Gold pays for
passive refunds and the Currency Exchange.
- Double tap L3 in play: your gold, with the rest of your status.
- In any window, hold L3 + right stick down: your gold, then every currency in your bag and in
  the open stash tab, by name and count (CurrencyHotkey in Exile API's menu, no key by default).
- At a vendor, an item's price is said after its description, with whether you can pay it
  ("Price: 1 Orb of Transmutation, you have 3"). Hold L3 + right stick up: the price again.
- In the sell window, what the vendor offers is said as it changes ("Vendor offers: ...").
The price is read from the screen, and this has not been tried in the game yet: when no price
is found, the mod writes what it saw to its log ("Vendor price not found").

TOOLTIPS IN MENUS

A selected button is said with the word button and its place among the buttons beside it:
"log in button, 2 of 5". "Say button and its place among the buttons" in the Reading submenu
turns this off.
When you move to a menu entry or button that shows a tooltip, the tooltip is read after its
name, in any menu, including new ones a game update adds. In a menu, tap L3 for its next line,
double tap for the previous one, and hold L3 for all of it. Turn it off with "Read the tooltips
of menus" in the Reading submenu. If something you move to is not read at all, the mod writes
what it found there to its log ("Unread selection"): sending the log is how it gets fixed.

MOD SETTINGS MENU (spoken, as in Poe2Access)
- Open: hold L3 for a second and a half in play, or hold F3 for a second.
- Right stick up and down (or the arrow keys): move. Right stick left and right: open a submenu,
  turn a switch on or off, or change a value in steps of 5; a volume plays its sound.
- Tap L3 (or Backspace): back up one level. Double tap L3, or hold F3 again: close.
- Submenus: Sound volumes (master, enemies and combat, loot, radar, guide), Speech (speech speed
  and what is announced), Sounds on or off, Reading, and Sound glossary: every sound the mod makes
  with what it means; right stick left or right plays it (as in Poe2Access).
- Speech speed applies to the Windows voice; NVDA and JAWS keep the speed set in the screen
  reader.

LICENSE
Path of Exile Access is free software under the GNU General Public License, version 3 (see
LICENSE.txt). Prism keeps its own licenses (licenses\prism).

CREDITS
- Exile API (https://github.com/exApiTools/ExileApi-Compiled), downloaded on first run, which
  reads the game.
- Prism 0.18.3 (MPL-2.0 license), for screen reader and braille output:
  https://github.com/ethindp/prism. Its licenses are in licenses\prism.
- The navigation design follows Path of Exile 2 Access (https://github.com/Ali-Bueno/poe2-access);
  the mod's own sounds are synthesized.

IF SOMETHING FAILS
Send these files to whoever gave you the mod:
- app\launcher.log
- app\reader.log and app\reader\PoE1Access_dumps\PoE1Access_log.txt (login and character screens)
- app\ExileApi\PoE1Access_dumps\PoE1Access_log.txt (in the game)
Email addresses are blanked out in these logs.
