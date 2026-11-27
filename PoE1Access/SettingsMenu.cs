using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Nodes;
using static PoE1Access.Loc;

namespace PoE1Access;

// The mod's own spoken settings menu, as in Poe2Access (its "Mod settings" menu): no need to
// see Exile API's F12 window.
//
//   Open:   hold L3 for a second and a half in play, or hold F3 for a second.
//   Move:   right stick up / down (arrow keys up / down).
//   Change: right stick left / right (arrow keys left / right): opens a submenu, toggles a
//           switch, moves a volume or speed in steps of 5, plays the sound it controls.
//   Back:   tap L3 (Backspace) goes up one level; at the top it closes.
//   Close:  double tap L3, or hold F3 again: "Settings closed".
// Exile API saves the values itself (config\PoE1Access\settings), as for its F12 window.
public partial class PoE1AccessCore
{
    private sealed class MenuItem
    {
        public string Label;
        public List<MenuItem> Children;               // a submenu
        public ToggleNode Toggle;                     // a switch
        public RangeNode<int> Range;                  // a number
        public int Step = 5;
        public Func<int, string> Words;               // how a number is said ("100 percent")
        public Action<int> OnChange;                  // a sample of what it controls
        public Action Play;                           // the sound glossary: plays the sound
        public string Description;                    // ...and says what it means
        public string Text;                           // a tutorial page, read as it is
        public Action StartPractice;                  // the tutorial's "Try it now", or a switch's action
        public Func<string> Describe;                 // said instead of Text when moving left
    }

    private List<MenuItem> _menuRoot;
    private readonly List<(List<MenuItem> Items, int Index)> _menuStack = new List<(List<MenuItem>, int)>();
    private readonly Dictionary<List<MenuItem>, int> _menuLastIndex = new Dictionary<List<MenuItem>, int>();
    public bool MenuOpen => _menuStack.Count > 0;

    private List<MenuItem> BuildMenu()
    {
        string Pct(int v) => T($"{v} percent", $"{v} por ciento");
        MenuItem Vol(string label, RangeNode<int> node, Action<int> sample) =>
            new MenuItem { Label = label, Range = node, Words = Pct, OnChange = sample };
        MenuItem Sw(string label, ToggleNode node) => new MenuItem { Label = label, Toggle = node };
        MenuItem Sub(string label, params MenuItem[] items) => new MenuItem { Label = label, Children = items.ToList() };

        return new List<MenuItem>
        {
            TutorialMenu(),
            Sub(T("Sound volumes", "Volúmenes de sonido"),
                Vol(T("Master volume", "Volumen general"), Settings.MasterVolume, _ => SampleSound("loot")),
                Vol(T("Enemy, NPC and combat sounds", "Sonidos de enemigos, PNJ y combate"), Settings.EnemyVolume, _ => SampleSound("enemy")),
                Vol(T("Loot sounds", "Sonidos de botín"), Settings.LootVolume, _ => SampleSound("loot")),
                Vol(T("Radar", "Radar"), Settings.RadarVolume, _ => SampleSound("radar")),
                Vol(T("Guide", "Guía"), Settings.GuideVolume, _ => SampleSound("guide"))),
            Sub(T("Speech", "Voz"),
                new MenuItem
                {
                    Label = T("Speech speed", "Velocidad de la voz"), Range = Settings.SpeechRate, Words = Pct,
                    OnChange = v => ApplySpeechRate(true),
                },
                Sw(T("Describe areas on arrival", "Describir las áreas al llegar"), Settings.DescribeAreas),
                Sw(T("Tutorials start as you meet each part of the game", "Los tutoriales empiezan al encontrar cada parte del juego"), Settings.AutoTutorials),
                Sw(T("Tips about new game mechanics", "Consejos sobre mecánicas nuevas del juego"), Settings.MechanicTips),
                Sw(T("Loot follows the game's loot filter", "El botín sigue el filtro de botín del juego"), Settings.FollowLootFilter),
                Sw(T("Explore mode: guiding to unexplored ground goes on to the next", "Modo exploración: la guía al terreno sin explorar sigue al siguiente"), Settings.ExploreMode),
                Sw(T("Warn about dangerous map mods", "Avisar de modificadores de mapa peligrosos"), Settings.MapModWarnings),
                Sw(T("Atlas finder on the D-pad", "Buscador del Atlas en la cruceta"), Settings.AtlasFinder),
                Sw(T("Quest route, once you have finished the campaign", "Ruta de misión, una vez terminada la campaña"), Settings.QuestRoute),
                Sw(T("Describe how each class plays on the class screen", "Describir cómo se juega cada clase en la pantalla de clases"), Settings.ClassDescriptions),
                Sw(T("Passive tree finder on the D-pad", "Buscador del árbol de pasivas en la cruceta"), Settings.PassiveFinder),
                Sw(T("Dodge warning when a monster attacks you", "Aviso para esquivar cuando un monstruo te ataca"), Settings.DodgeWarnings),
                Sw(T("Dodge warning for every enemy, not only rares and bosses", "Aviso para esquivar con todos los enemigos, no solo raros y jefes"), Settings.DodgeWarningsAll),
                Sw(T("Sounds for chests, shrines, waypoints, portals, exits and the stash", "Sonidos de cofres, santuarios, transportadores, portales, salidas y el alijo"), Settings.ObjectSounds),
                Sw(T("Say when you are frozen, poisoned, cursed and the like", "Decir cuando estás congelado, envenenado, maldito y similares"), Settings.StatusAlerts),
                Sw(T("Sound while standing in harmful ground", "Sonido mientras pisas suelo dañino"), Settings.HarmfulGroundSound),
                Sw(T("Charges gained and lost", "Cargas ganadas y perdidas"), Settings.ChargeAlerts),
                Sw(T("A boss's life as it falls", "La vida de un jefe a medida que baja"), Settings.AnnounceBossLife),
                Sw(T("Level up", "Subida de nivel"), Settings.AnnounceLevelUp),
                Sw(T("Name enemies coming into view", "Nombrar a los enemigos que aparecen a la vista"), Settings.AnnounceEnemies),
                Sw(T("Say which way enemies are", "Decir en qué dirección están los enemigos"), Settings.EnemyDirections),
                Sw(T("Name the targeted enemy", "Nombrar al enemigo apuntado"), Settings.AnnounceTarget),
                Sw(T("Life, mana and shield warnings", "Avisos de vida, maná y escudo"), Settings.AnnounceResources),
                Sw(T("Name things you walk up to", "Nombrar lo que tienes cerca al acercarte"), Settings.AnnounceNearby),
                Sw(T("Turn prompts while guiding", "Avisos de giro durante la guía"), Settings.RoutePrompts),
                Sw(T("Announce panels", "Anunciar los paneles"), Settings.AnnouncePanels),
                Sw(T("Read a PlayStation controller for the mod's L3 controls", "Leer un mando de PlayStation para los controles L3 del mod"), Settings.ReadPlayStationController)),
            SpeechPriorityMenu(),
            Sub(T("Sounds on or off", "Sonidos activados o desactivados"),
                Sw(T("Wall radar", "Radar de paredes"), Settings.Radar),
                Sw(T("Unexplored ground pips", "Pitidos del terreno sin explorar"), Settings.UnexploredMark),
                Sw(T("Enemy sounds", "Sonidos de enemigos"), Settings.EnemySounds),
                Sw(T("NPC sounds", "Sonidos de PNJ"), Settings.NpcSounds),
                Sw(T("Other player sounds", "Sonidos de otros jugadores"), Settings.PlayerSounds),
                Sw(T("Loot sounds", "Sonidos de botín"), Settings.LootSounds),
                Sw(T("Loot uses the game's own drop sounds", "El botín usa los sonidos de caída del propio juego"), Settings.GameLootSounds),
                Sw(T("Loot, chests and stash use your sound pack (a sounds folder beside PoE1Access)", "El botín, los cofres y el alijo usan tu paquete de sonidos (una carpeta sounds junto a PoE1Access)"), Settings.SoundPackSounds),
                new MenuItem
                {
                    Label = T("Which loot sounds", "Qué sonidos de botín"), Range = Settings.LootFilter, Step = 1,
                    Words = v => v == 0 ? T("all items", "todos los objetos") : v == 1 ? T("magic or better", "mágicos o mejores") : T("valuable only", "solo los valiosos"),
                },
                Sw(T("Targeting blips", "Pitidos de apuntado"), Settings.CrosshairBeep),
                Sw(T("Melee reach ping", "Aviso de alcance cuerpo a cuerpo"), Settings.ReachPing),
                Sw(T("Sounds for ailments you put on enemies", "Sonidos de las alteraciones que causas a los enemigos"), Settings.EnemyStateSounds),
                new MenuItem { Label = T("Melee reach distance", "Distancia de alcance cuerpo a cuerpo"), Range = Settings.ReachDistance, Words = v => v.ToString() }),
            Sub(T("Reading", "Lectura"),
                Sw(T("Read the controller selection", "Leer la selección del mando"), Settings.ReadFocus),
                Sw(T("Read what is under the cursor", "Leer lo que hay bajo el cursor"), Settings.ReadOnHover),
                Sw(T("Read the tooltips of menus", "Leer las descripciones de los menús"), Settings.SpeakTooltips)),
            Sub(T("Sound glossary", "Glosario de sonidos"), Glossary().ToArray()),
        };
    }

    // Speech priority (as in Poe2Access): how each kind of news the mod says by itself is delivered.
    private MenuItem SpeechPriorityMenu()
    {
        MenuItem P(string label, News kind) => new MenuItem { Label = label, Range = NewsLevel(kind), Step = 1, Words = NewsLevelWords };
        return new MenuItem
        {
            Label = T("Speech priority", "Prioridad de la voz"),
            Children = new List<MenuItem>
            {
                P(T("Speech: frozen, poisoned, cursed, harmful ground and the like beginning", "Voz: congelado, envenenado, maldito, suelo dañino y similares al empezar"), News.StatusBegins),
                P(T("Speech: those ending", "Voz: al terminar"), News.StatusEnds),
                P(T("Speech: low life, mana and shield, urgent when life is under half", "Voz: poca vida, maná y escudo, urgente con la vida por debajo de la mitad"), News.LifeAndMana),
                P(T("Speech: a boss's life, urgent at 30 percent and when defeated", "Voz: la vida de un jefe, urgente al 30 por ciento y al ser derrotado"), News.BossLife),
                P(T("Speech: enemy names, urgent for the one you aim at and rares and uniques coming into view", "Voz: nombres de enemigos, urgente para el que apuntas y los raros y únicos que aparecen"), News.EnemyNames),
                P(T("Speech: quest route", "Voz: ruta de misión"), News.Quest),
                P(T("Speech: things you walk up to", "Voz: lo que tienes cerca al acercarte"), News.Places),
                P(T("Speech: changes in your bag", "Voz: cambios en tu bolsa"), News.Bag),
                P(T("Speech: arriving with the guide", "Voz: llegar con la guía"), News.Arrivals),
                P(T("Speech: charges", "Voz: cargas"), News.Charges),
                P(T("Speech: level up", "Voz: subida de nivel"), News.LevelUp),
            },
        };
    }

    // Every sound the mod makes, what it means, and left or right to hear it (as in Poe2Access's
    // sound glossary), at the volume of its kind.
    private List<MenuItem> Glossary()
    {
        float V(RangeNode<int> node) => node.Value / 100f;
        MenuItem S(string label, string meaning, Func<float[]> make, Func<float> volume) =>
            new MenuItem { Label = label, Description = meaning, Play = () =>
            {
                AudioEngine.Master = Settings.MasterVolume.Value / 100f;
                _audio?.Play("glossary:" + label, make, volume());
            } };
        Func<float> radar = () => V(Settings.RadarVolume), enemy = () => V(Settings.EnemyVolume),
                    loot = () => V(Settings.LootVolume) * 1.4f, guide = () => V(Settings.GuideVolume);
        var items = new List<MenuItem>
        {
            S(T("Wall", "Pared"), T("radar: a wall closes the way on that side", "radar: una pared cierra el paso por ese lado"), () => Synth.Tock(0f, 1f), radar),
            S(T("Open way", "Paso abierto"), T("radar: the way opens again on that side", "radar: el paso se abre otra vez por ese lado"), () => Synth.Breath(0f, 0), radar),
            S(T("Exit ahead", "Salida delante"), T("radar: an exit lies that way", "radar: hay una salida en esa dirección"), () => Synth.Tin(0f, 1f), radar),
            S(T("Unexplored ground", "Terreno sin explorar"), T("pips over the radar: ground you have not seen lies that way", "pitidos sobre el radar: en esa dirección hay terreno que no has visto"), () => Synth.Pips(0f, 1f), radar),
            S(T("Wall bump", "Choque con pared"), T("you pushed into a wall and stopped", "has chocado con una pared y te has detenido"), () => Synth.Thud(0f), radar),
            S(T("Guide", "Guía"), T("the guide: the way to what you chose", "la guía: el camino hacia lo que elegiste"), () => Synth.Chime(0f, 1f), guide),
            S(T("Enemy", "Enemigo"), T("a normal enemy, faster as it gets closer", "un enemigo normal, más rápido cuanto más cerca está"), () => Synth.Growl(ExileCore.Shared.Enums.MonsterRarity.White, 0f, 1f), enemy),
            S(T("Magic enemy", "Enemigo mágico"), T("a magic enemy", "un enemigo mágico"), () => Synth.Growl(ExileCore.Shared.Enums.MonsterRarity.Magic, 0f, 1f), enemy),
            S(T("Rare enemy", "Enemigo raro"), T("a rare enemy", "un enemigo raro"), () => Synth.Growl(ExileCore.Shared.Enums.MonsterRarity.Rare, 0f, 1f), enemy),
            S(T("Unique enemy", "Enemigo único"), T("a unique enemy, a boss", "un enemigo único, un jefe"), () => Synth.Growl(ExileCore.Shared.Enums.MonsterRarity.Unique, 0f, 1f), enemy),
            S(T("Character", "Personaje"), T("an NPC near you", "un PNJ cerca de ti"), () => Synth.Tap(0f, 1f, false), enemy),
            S(T("Character with a quest", "Personaje con misión"), T("an NPC with a quest for you", "un PNJ con una misión para ti"), () => Synth.QuestRing(0f, 1f), enemy),
            S(T("Player", "Jugador"), T("another player near you", "otro jugador cerca de ti"), () => Synth.Tap(0f, 1f, true), enemy),
            S(T("Target", "Objetivo"), T("the game's target landed on an enemy", "el objetivo del juego se ha fijado en un enemigo"), Synth.Blips, enemy),
            S(T("Melee reach", "Alcance cuerpo a cuerpo"), T("an enemy came within melee reach", "un enemigo ha entrado en tu alcance cuerpo a cuerpo"), () => Synth.ReachPing(0f), enemy),
            S(T("Dodge warning", "Aviso para esquivar"), T("a monster starts an attack aimed at you: dodge", "un monstruo empieza un ataque dirigido a ti: esquiva"), () => Synth.DodgeWarning(0f), enemy),
            S(T("Trap", "Trampa"), T("a trap of a Trial of Ascendancy or the Labyrinth, faster as you get closer", "una trampa de una Prueba de Ascendencia o del Laberinto, más rápido cuanto más cerca"), () => Synth.Object(Synth.WorldCue.Trap, 0f, 1f), loot),
            S(T("Harmful ground", "Suelo dañino"), T("you stand in burning, caustic or other harmful ground: move", "estás sobre suelo ardiente, cáustico u otro suelo dañino: muévete"), Synth.HarmfulGround, enemy),
            S(T("Your ailment", "Tu alteración"), T("you are frozen, poisoned, cursed or the like; its name follows", "estás congelado, envenenado, maldito o similar; después se dice su nombre"), Synth.Ailment, enemy),
        };
        foreach (Synth.EnemyState st in Enum.GetValues(typeof(Synth.EnemyState)))
        {
            var state = st;
            string word = EnemyStateWord(st);
            items.Add(S(T($"Enemy {word}", $"Enemigo {word}"), T($"you made an enemy {word}", $"has dejado a un enemigo {word}"), () => Synth.EnemyStateCue(state, 0f), enemy));
        }
        items.Add(S(T("Charge gained", "Carga ganada"), T("a frenzy, power or endurance charge gained; its count follows", "ganas una carga de frenesí, de poder o de aguante; después se dice cuántas tienes"), () => Synth.Charge(0, true), enemy));
        items.Add(S(T("Charge lost", "Carga perdida"), T("a charge lost", "pierdes una carga"), () => Synth.Charge(0, false), enemy));
        items.Add(S(T("Level up", "Subida de nivel"), T("you gained a level", "has subido de nivel"), Synth.LevelUp, enemy));
        var objects = new (Synth.WorldCue Kind, string Label, string Meaning)[]
        {
            (Synth.WorldCue.Chest, T("Chest", "Cofre"), T("a chest you have not opened", "un cofre que no has abierto")),
            (Synth.WorldCue.Shrine, T("Shrine", "Santuario"), T("a shrine you can still take", "un santuario que todavía puedes tomar")),
            (Synth.WorldCue.Waypoint, T("Waypoint", "Transportador"), T("the nearest waypoint", "el transportador más cercano")),
            (Synth.WorldCue.Portal, T("Portal", "Portal"), T("the nearest portal", "el portal más cercano")),
            (Synth.WorldCue.Exit, T("Exit", "Salida"), T("the nearest way to another area", "el paso más cercano a otra área")),
            (Synth.WorldCue.Stash, T("Stash", "Alijo"), T("the nearest stash", "el alijo más cercano")),
            (Synth.WorldCue.Cauldron, T("Cauldron", "Caldero"), T("a Coven's Cauldron whose Fate you have not chosen, or the boss cauldron", "un caldero del aquelarre cuyo Destino no has elegido, o el caldero del jefe")),
        };
        foreach (var (kind, label, meaning) in objects)
        {
            var k = kind;
            items.Add(S(label, T($"{meaning}, faster as you get closer", $"{meaning}, más rápido cuanto más te acercas"), () => Synth.Object(k, 0f, 1f), loot));
        }
        var drops = new (Synth.Loot Kind, string Label)[]
        {
            (Synth.Loot.Currency, T("currency", "moneda")), (Synth.Loot.Scroll, T("a scroll", "un pergamino")), (Synth.Loot.Gem, T("a gem", "una gema")), (Synth.Loot.Flask, T("a flask", "un frasco")),
            (Synth.Loot.Map, T("a map", "un mapa")), (Synth.Loot.Card, T("a divination card", "una carta de adivinación")), (Synth.Loot.Jewel, T("a jewel", "una joya")), (Synth.Loot.Quest, T("a quest item", "un objeto de misión")),
            (Synth.Loot.Normal, T("a normal item", "un objeto normal")), (Synth.Loot.Magic, T("a magic item", "un objeto mágico")), (Synth.Loot.Rare, T("a rare item", "un objeto raro")), (Synth.Loot.Unique, T("a unique item", "un objeto único")),
        };
        foreach (var (kind, label) in drops)
        {
            var k = kind;
            items.Add(S(T($"Loot: {label}", $"Botín: {label}"), T($"{label} on the ground, the mod's own sound", $"{label} en el suelo, el sonido del propio mod"), () => Synth.LootCue(k, 0f, 1f), loot));
        }

        // The game's own drop sounds, which loot plays by default (GameSounds), from the game.
        var gameDrops = new (string Event, string Label)[]
        {
            ("orb", T("currency", "moneda")), ("Scroll", T("a scroll", "un pergamino")), ("Gem", T("a gem", "una gema")), ("Flask", T("a flask", "un frasco")), ("Map", T("a map", "un mapa")),
            ("DivinationCard", T("a divination card", "una carta de adivinación")), ("Jewel", T("a jewel", "una joya")), ("EssenceItem", T("an essence", "una esencia")), ("DelveFossil", T("a fossil", "un fósil")),
            ("Ring", T("a ring", "un anillo")), ("Amulet", T("an amulet", "un amuleto")), ("Belt", T("a belt", "un cinturón")), ("quiver", T("a quiver", "un carcaj")),
            ("Helm", T("a helmet", "un casco")), ("cap", T("a cap", "un gorro")), ("Metal_armour", T("metal body armour", "una armadura de metal")), ("Leather_armour", T("leather body armour", "una armadura de cuero")),
            ("cloth_armour", T("cloth body armour", "una armadura de tela")), ("chain_armour", T("chain body armour", "una armadura de malla")),
            ("chain_gloves", T("metal gloves", "guantes de metal")), ("Leather_glove", T("leather gloves", "guantes de cuero")),
            ("Metal_boots", T("metal boots", "botas de metal")), ("Boots", T("leather boots", "botas de cuero")), ("chain_boots", T("chain boots", "botas de malla")),
            ("Metal_shield", T("a metal shield", "un escudo de metal")), ("wooden_shield", T("a wooden shield", "un escudo de madera")), ("Metal_and_wooden_shield", T("a mixed shield", "un escudo mixto")),
            ("Sword", T("a sword", "una espada")), ("Large_sword", T("a two handed sword", "una espada a dos manos")), ("1HandAxe", T("an axe", "un hacha")), ("1HandMace", T("a mace", "una maza")),
            ("Maul", T("a two handed mace", "una maza a dos manos")), ("Scepter", T("a sceptre", "un cetro")), ("1HandDagger", T("a dagger", "una daga")), ("1HandClaw", T("a claw", "una garra")),
            ("wand", T("a wand", "una varita")), ("Staff", T("a staff", "un báculo")), ("Bow", T("a bow", "un arco")), ("item_drop_large", T("any other item", "cualquier otro objeto")),
        };
        MenuItem G(string label, string meaning, string eventPath) =>
            new MenuItem { Label = label, Description = meaning, Play = () =>
            {
                if (_gameSounds == null || !_gameSounds.Available) { Say(T("The game's sounds cannot be played here", "Los sonidos del juego no se pueden reproducir aquí"), true); return; }
                _gameSounds.Play(eventPath, 0f, 1f, V(Settings.LootVolume) * 1.4f * Settings.MasterVolume.Value / 100f);
            } };
        foreach (var (ev, label) in gameDrops)
            items.Add(G(T($"Game loot: {label}", $"Botín del juego: {label}"), T($"{label} on the ground, the game's own drop sound", $"{label} en el suelo, el sonido de caída del propio juego"), "event:/Sound Effects/ItemSounds/" + ev + ".ogg"));
        items.Add(G(T("Game loot: unique", "Botín del juego: único"), T("played over a unique item's drop sound, the game's own", "suena sobre el sonido de caída de un objeto único, el del propio juego"), GameSounds.UniqueLayer));
        // Your sound pack, when there is one.
        if (_soundPack != null && _soundPack.Available)
        {
            var packSounds = new (string Name, string Meaning)[]
            {
                ("gold", T("currency on the ground, looping", "moneda en el suelo, en bucle")),
                ("potion", T("a flask on the ground, looping", "un frasco en el suelo, en bucle")),
                ("normal equipment", T("normal gear on the ground, looping", "equipo normal en el suelo, en bucle")),
                ("magic equipment", T("magic gear on the ground, looping", "equipo mágico en el suelo, en bucle")),
                ("rare equipment", T("rare gear on the ground, looping", "equipo raro en el suelo, en bucle")),
                ("legendary equipment", T("a unique item on the ground, looping", "un objeto único en el suelo, en bucle")),
                ("quest item", T("a quest item on the ground, looping", "un objeto de misión en el suelo, en bucle")),
                ("gem", T("a skill gem on the ground", "una gema de habilidad en el suelo")),
                ("scroll", T("a scroll on the ground", "un pergamino en el suelo")),
                ("rune", T("a jewel on the ground", "una joya en el suelo")),
                ("chest", T("a chest you have not opened", "un cofre que no has abierto")),
                ("corpse", T("a corpse you can search", "un cadáver que puedes registrar")),
                ("weapon rack", T("a weapon or armour rack", "un estante de armas o armaduras")),
                ("stash", T("the nearest stash", "el alijo más cercano")),
            };
            foreach (var (name, meaning) in packSounds)
            {
                string file = _soundPack.File(name);
                if (file == null) continue;
                items.Add(new MenuItem { Label = T($"Sound pack: {name}", $"Paquete de sonidos: {name}"), Description = meaning, Play = () =>
                {
                    if (_gameSounds == null || !_gameSounds.PlayFile(file, false, 0f, 1f, V(Settings.LootVolume) * 1.4f * Settings.MasterVolume.Value / 100f, out _))
                        Say(T("The sound pack cannot be played here", "El paquete de sonidos no se puede reproducir aquí"), true);
                } });
            }
        }
        return items;
    }

    // How an ailment left on an enemy is said ("frozen"), for the glossary.
    private static string EnemyStateWord(Synth.EnemyState state) => state switch
    {
        Synth.EnemyState.Frozen => T("frozen", "congelado"),
        Synth.EnemyState.Chilled => T("chilled", "helado"),
        Synth.EnemyState.Ignited => T("ignited", "en llamas"),
        Synth.EnemyState.Shocked => T("shocked", "electrocutado"),
        Synth.EnemyState.Poisoned => T("poisoned", "envenenado"),
        Synth.EnemyState.Bleeding => T("bleeding", "sangrando"),
        Synth.EnemyState.Cursed => T("cursed", "maldito"),
        _ => state.ToString().ToLowerInvariant(),
    };

    private void OpenMenu()
    {
        _menuRoot ??= BuildMenu();
        _menuStack.Clear();
        _menuStack.Add((_menuRoot, _menuLastIndex.TryGetValue(_menuRoot, out int i) ? i : 0));
        _pad.MenuMode = true;
        PluginLog.Write("Settings menu opened");
        Say(T($"Mod settings. {DescribeMenuItem()}", $"Ajustes del mod. {DescribeMenuItem()}"), true);
    }

    private void CloseMenu()
    {
        if (!MenuOpen) return;
        foreach (var (items, index) in _menuStack) _menuLastIndex[items] = index;
        _menuStack.Clear();
        _pad.MenuMode = false;
        PluginLog.Write("Settings menu closed");
        Say(T("Settings closed", "Ajustes cerrados"), true);
    }

    private string DescribeMenuItem()
    {
        var (items, index) = _menuStack[^1];
        MenuItem it = items[index];
        if (it.Play != null) return T($"{it.Label}, {it.Description}, {index + 1} of {items.Count}", $"{it.Label}, {it.Description}, {index + 1} de {items.Count}");
        if (it.Describe != null) return T($"{it.Describe()}. {it.Text} {index + 1} of {items.Count}", $"{it.Describe()}. {it.Text} {index + 1} de {items.Count}");
        if (it.Text != null) return T($"{it.Text} {index + 1} of {items.Count}", $"{it.Text} {index + 1} de {items.Count}");
        string value = it.Children != null ? T("submenu", "submenú")
            : it.Toggle != null ? (it.Toggle.Value ? T("on", "activado") : T("off", "desactivado"))
            : it.Range != null ? (it.Words ?? (v => v.ToString()))(it.Range.Value)
            : "";
        return T($"{it.Label}, {value}, {index + 1} of {items.Count}", $"{it.Label}, {value}, {index + 1} de {items.Count}");
    }

    // dy: -1 up, +1 down; dx: -1 left, +1 right.
    private void MenuStep(int dy, int dx)
    {
        if (!MenuOpen) return;
        var (items, index) = _menuStack[^1];
        if (dy != 0)
        {
            int next = Math.Max(0, Math.Min(items.Count - 1, index + dy));
            _menuStack[^1] = (items, next);
            Say(DescribeMenuItem() + (next == index ? (dy > 0 ? T(", last", ", último") : T(", first", ", primero")) : ""), true);
            return;
        }

        MenuItem it = items[index];
        if (it.Play != null) { it.Play(); return; }
        if (it.StartPractice != null) { if (dx > 0) it.StartPractice(); else Say(it.Describe?.Invoke() ?? DescribeMenuItem(), true); return; }
        if (it.Text != null) { Say(DescribeMenuItem(), true); return; }
        if (it.Children != null)
        {
            if (dx < 0) { Say(DescribeMenuItem(), true); return; }
            _menuLastIndex[items] = index;
            _menuStack.Add((it.Children, _menuLastIndex.TryGetValue(it.Children, out int i) ? i : 0));
            Say(T($"{it.Label}. {DescribeMenuItem()}", $"{it.Label}. {DescribeMenuItem()}"), true);
            return;
        }
        if (it.Toggle != null)
        {
            it.Toggle.Value = !it.Toggle.Value;
            Say(it.Toggle.Value ? T($"{it.Label}, on", $"{it.Label}, activado") : T($"{it.Label}, off", $"{it.Label}, desactivado"), true);
            return;
        }
        if (it.Range != null)
        {
            int v = Math.Max(it.Range.Min, Math.Min(it.Range.Max, it.Range.Value + dx * it.Step));
            bool edge = v == it.Range.Value;
            it.Range.Value = v;
            Say((it.Words ?? (n => n.ToString()))(v) + (edge ? (dx > 0 ? T(", highest", ", máximo") : T(", lowest", ", mínimo")) : ""), true);
            it.OnChange?.Invoke(v);
        }
    }

    private void MenuBack()
    {
        if (!MenuOpen) return;
        if (_menuStack.Count == 1) { CloseMenu(); return; }
        var (items, index) = _menuStack[^1];
        _menuLastIndex[items] = index;
        _menuStack.RemoveAt(_menuStack.Count - 1);
        Say(DescribeMenuItem(), true);
    }

    // A sample of the sound a volume controls, at the new volume.
    private void SampleSound(string kind)
    {
        if (_audio == null) return;
        float master = Settings.MasterVolume.Value / 100f;
        AudioEngine.Master = master;
        switch (kind)
        {
            case "enemy":
                _audio.Play("sample:enemy", () => Synth.Growl(ExileCore.Shared.Enums.MonsterRarity.White, 0f, 1f), Settings.EnemyVolume.Value / 100f);
                break;
            case "loot":
                _audio.Play("sample:loot", () => Synth.LootCue(Synth.Loot.Rare, 0f, 1f), Settings.LootVolume.Value / 100f * 1.4f);
                break;
            case "radar":
                _audio.Play("sample:radar", () => Synth.Tock(0f, 1f), Settings.RadarVolume.Value / 100f);
                break;
            case "guide":
                _audio.Play("sample:guide", () => Synth.Chime(0f, 1f), Settings.GuideVolume.Value / 100f);
                break;
        }
    }

    // Speech speed: Prism passes it to voices that take one (the Windows voice); NVDA and JAWS
    // keep the speed set in the screen reader.
    private int _appliedRate = -1;

    private void ApplySpeechRate(bool fromMenu)
    {
        int rate = Settings.SpeechRate.Value;
        if (rate == _appliedRate && !fromMenu) return;
        if (!fromMenu && _appliedRate < 0 && rate == 50) { _appliedRate = rate; return; }   // untouched: leave the voice as it is
        _appliedRate = rate;
        bool ok = _speech is PrismSpeech p && p.SetRate(rate / 100f);
        if (fromMenu && !ok)
            Say(_speech?.Name is string reader
                ? T($"Your screen reader keeps its own speed. Change it in {reader}'s settings", $"Tu lector de pantalla mantiene su propia velocidad. Cámbiala en los ajustes de {reader}")
                : T("Your screen reader keeps its own speed. Change it in the screen reader's settings", "Tu lector de pantalla mantiene su propia velocidad. Cámbiala en los ajustes del lector de pantalla"), false);
    }
}
