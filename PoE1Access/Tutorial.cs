using System;
using System.Collections.Generic;
using System.Linq;
using static PoE1Access.Loc;

namespace PoE1Access;

// The tutorial: optional lessons on what the mod adds, first in the mod menu (hold L3 for a second
// and a half). Each lesson is a submenu of pages read with the right stick up and down; some end
// with "Try it now", which closes the menu and listens for you to use the mod's own controls
// (L3 and the right stick while L3 is held), saying when each one is done.
//
// The tutorial only reads and listens, like the rest of the mod: it never moves your character,
// presses a button or clicks for you. What the game's own buttons do is left to the game's button
// prompts, which the mod reads (hold L3), so nothing here can be out of date with the game.
public partial class PoE1AccessCore
{
    private sealed class Lesson
    {
        public string Key = "";   // what starts it in play (AutoTutorial.cs)
        public string Title;
        public string[] Pages;
        public (string Goal, string Ask)[] Practice = Array.Empty<(string, string)>();
    }

    private List<(string Goal, string Ask)> _practice = new List<(string, string)>();
    private string _practiceLesson = "";

    private List<Lesson> Lessons() => new List<Lesson>
    {
        new Lesson
        {
            Key = "basics",
            Title = T("1. How the mod works", "1. Cómo funciona el mod"),
            Pages = new[]
            {
                T("Path of Exile access reads the game to you and adds sounds to find your way. It only reads: it never moves your character, presses a button or clicks for you. You play with the game's own controls.",
                  "Path of Exile access te lee el juego y añade sonidos para orientarte. Solo lee: nunca mueve a tu personaje, ni pulsa botones, ni hace clic por ti. Juegas con los controles del propio juego."),
                T("The mod's controls are on L3, clicking the left stick, which the game leaves free in play. Tap it, double tap it, triple tap it, or hold it. While you hold L3, the right stick is the mod's too.",
                  "Los controles del mod están en L3, al pulsar el stick izquierdo, que el juego deja libre al jugar. Tócalo una vez, dos, tres, o mantenlo. Mientras mantienes L3, el stick derecho también es del mod."),
                T("Hold L3 for a second and a half for this menu. F9 on the keyboard repeats the last thing said. Every key can be changed in Exile API's menu, F12.",
                  "Mantén L3 un segundo y medio para este menú. F9 en el teclado repite lo último que se dijo. Cada tecla se puede cambiar en el menú de Exile API, F12."),
                T("To learn the game's own buttons, hold L3 for half a second in play: the mod reads the game's button prompts for what is near you, such as talk, pick up or open.",
                  "Para aprender los botones del propio juego, mantén L3 medio segundo al jugar: el mod lee las indicaciones de botones del juego para lo que tienes cerca, como hablar, recoger o abrir."),
            },
            Practice = new[]
            {
                ("prompts", T("Hold L3 for half a second, then let go, to hear the game's button prompts.", "Mantén L3 medio segundo y suéltalo para oír las indicaciones de botones del juego.")),
            },
        },
        new Lesson
        {
            Key = "sounds",
            Title = T("2. The sounds around you", "2. Los sonidos a tu alrededor"),
            Pages = new[]
            {
                T("Every sound comes from the side its source is on, higher when it is up the screen and lower when it is down the screen.",
                  "Cada sonido llega desde el lado en que está su origen, más agudo cuando está arriba de la pantalla y más grave cuando está abajo."),
                T("The wall radar: a tock means a wall closes the way on that side, a breath means the way opens again, and a tin note means an exit lies that way. Pips over the radar point to ground you have not explored.",
                  "El radar de paredes: un toc significa que una pared cierra el paso por ese lado, un soplo que el paso se abre otra vez, y una nota metálica que hay una salida en esa dirección. Los pitidos sobre el radar señalan terreno sin explorar."),
                T("Enemies growl, faster as they get closer; magic and rare ones buzz on top and bosses ring like metal. Two rising notes mean an enemy is within melee reach. A sharp click and two quick tings is a dodge warning: a strong monster is attacking you.",
                  "Los enemigos gruñen, más rápido cuanto más cerca están; los mágicos y raros zumban encima y los jefes suenan como metal. Dos notas ascendentes significan que un enemigo está a tu alcance cuerpo a cuerpo. Un clic seco y dos tintineos rápidos son un aviso para esquivar: un monstruo fuerte te ataca."),
                T("Items on the ground keep sounding from where they lie, faster as you get closer. Chests knock, and shrines, waypoints, portals and the stash have sounds of their own.",
                  "Los objetos en el suelo siguen sonando desde donde están, más rápido cuanto más te acercas. Los cofres golpean, y los santuarios, transportadores, portales y el alijo tienen sus propios sonidos."),
                T("To hear every sound with its meaning, open the Sound glossary at the end of this menu: move right on a sound to play it.",
                  "Para oír cada sonido con su significado, abre el Glosario de sonidos al final de este menú: muévete a la derecha sobre un sonido para reproducirlo."),
            },
        },
        new Lesson
        {
            Key = "scanner",
            Title = T("3. The scanner: what is around you", "3. El escáner: lo que te rodea"),
            Pages = new[]
            {
                T("The scanner lists what is in the area by kind: enemies, items, chests, characters, exits, your quest and more. Hold L3 and push the right stick up or down to change the kind.",
                  "El escáner lista lo que hay en el área por tipo: enemigos, objetos, cofres, personajes, salidas, tu misión y más. Mantén L3 y empuja el stick derecho arriba o abajo para cambiar de tipo."),
                T("Hold L3 and push the right stick left or right to go through the things of that kind, nearest first. Each says how far and which way it is, and its place in the list, such as 2 of 5.",
                  "Mantén L3 y empuja el stick derecho a la izquierda o a la derecha para recorrer las cosas de ese tipo, la más cercana primero. Cada una dice a qué distancia y en qué dirección está, y su lugar en la lista, como 2 de 5."),
                T("Tap L3 to hear where the thing you chose is now, after you have moved.",
                  "Toca L3 para oír dónde está ahora lo que elegiste, después de moverte."),
                T("Exits stay listed once you have seen them, even on your other characters. Unexplored ground says when it leads to a new exit, or to something for your quest.",
                  "Las salidas siguen en la lista una vez que las has visto, incluso con tus otros personajes. El terreno sin explorar dice cuándo lleva a una salida nueva o a algo para tu misión."),
            },
            Practice = new[]
            {
                ("category", T("Hold L3 and push the right stick up or down to change the kind.", "Mantén L3 y empuja el stick derecho arriba o abajo para cambiar de tipo.")),
                ("thing", T("Now hold L3 and push the right stick left or right to go through that kind.", "Ahora mantén L3 y empuja el stick derecho a la izquierda o a la derecha para recorrer ese tipo.")),
                ("where", T("Now tap L3 to hear where it is.", "Ahora toca L3 para oír dónde está.")),
            },
        },
        new Lesson
        {
            Key = "guide",
            Title = T("4. The guide: getting somewhere", "4. La guía: llegar a un sitio"),
            Pages = new[]
            {
                T("Choose something in the scanner, then triple tap L3 to turn the guide on. A chime then sounds from the way to walk, around walls, until you get there.",
                  "Elige algo en el escáner y toca L3 tres veces para activar la guía. Entonces suena una campanilla desde el camino que debes seguir, rodeando paredes, hasta que llegues."),
                T("You walk yourself, with the game's left stick: the guide only tells you the way. It also says turns, such as now down right. Triple tap L3 again to turn it off.",
                  "Caminas tú, con el stick izquierdo del juego: la guía solo te indica el camino. También dice los giros, como ahora abajo a la derecha. Toca L3 tres veces otra vez para desactivarla."),
            },
            Practice = new[]
            {
                ("guide", T("Choose something in the scanner, then triple tap L3 to turn the guide on or off.", "Elige algo en el escáner y toca L3 tres veces para activar o desactivar la guía.")),
            },
        },
        new Lesson
        {
            Key = "character",
            Title = T("5. You and your character", "5. Tú y tu personaje"),
            Pages = new[]
            {
                T("Double tap L3 for your level and how far to the next, your life, mana and energy shield, your flasks, the area and how much of it you have explored.",
                  "Toca L3 dos veces para oír tu nivel y cuánto falta para el siguiente, tu vida, maná y escudo de energía, tus frascos, el área y cuánto has explorado."),
                T("The mod warns you when life or mana run low, when you are frozen, poisoned or cursed, and when you stand in harmful ground. Each can be turned off in the Speech submenu.",
                  "El mod te avisa cuando la vida o el maná bajan, cuando estás congelado, envenenado o maldito, y cuando pisas suelo dañino. Cada aviso se puede desactivar en el submenú Voz."),
            },
            Practice = new[]
            {
                ("status", T("Double tap L3 to hear how you are doing.", "Toca L3 dos veces para oír cómo estás.")),
            },
        },
        new Lesson
        {
            Key = "menus",
            Title = T("6. Menus, items and tooltips", "6. Menús, objetos y descripciones"),
            Pages = new[]
            {
                T("In the game's windows and menus, whatever you move to is read, with its tooltip after it. The game's buttons for the window are said as it opens; hold L3 to hear them again.",
                  "En las ventanas y menús del juego se lee lo que seleccionas, con su descripción después. Los botones del juego para la ventana se dicen al abrirla; mantén L3 para oírlos otra vez."),
                T("In a menu, tap L3 for the next line of a tooltip, double tap for the line before, and hold for all of it.",
                  "En un menú, toca L3 para la siguiente línea de una descripción, dos veces para la anterior, y mantén para oírla entera."),
                T("Items are read from the game's data: name, rarity, mods, and what they need. In the inventory, hold L3 for a second and a half to hear every item in your bag. F5 and F6 go through your bag from the keyboard.",
                  "Los objetos se leen de los datos del juego: nombre, rareza, modificadores y lo que necesitan. En el inventario, mantén L3 un segundo y medio para oír todos los objetos de tu bolsa. F5 y F6 recorren tu bolsa desde el teclado."),
                T("On the character sheet and in the skills panel, hold L3 and use the right stick to go through each stat or skill.",
                  "En la hoja de personaje y en el panel de habilidades, mantén L3 y usa el stick derecho para recorrer cada estadística o habilidad."),
            },
        },
        new Lesson
        {
            Key = "skillbinding",
            Title = T("7. Skills on your buttons", "7. Habilidades en tus botones"),
            Pages = new[]
            {
                T("The game puts your skills on buttons in its skill binding mode, which R3, clicking the right stick, opens in play. The mod says when it opens and closes.",
                  "El juego pone tus habilidades en botones en su modo de asignación de habilidades, que se abre con R3, al pulsar el stick derecho, al jugar. El mod dice cuándo se abre y se cierra."),
                T("Move with the right stick and each button's slot is read with its skill. Press the game's select button on a slot for the skills you can put there; each is read with what it does, and its place in the list.",
                  "Muévete con el stick derecho y se lee la ranura de cada botón con su habilidad. Pulsa el botón de seleccionar del juego en una ranura para ver las habilidades que puedes poner ahí; cada una se lee con lo que hace y su lugar en la lista."),
                T("New skills come from skill gems socketed in your gear. With only one skill to choose, the mod says so.",
                  "Las habilidades nuevas vienen de gemas de habilidad engarzadas en tu equipo. Si solo hay una habilidad para elegir, el mod lo dice."),
            },
        },
        new Lesson
        {
            Key = "areas",
            Title = T("8. Areas, quests and travel", "8. Áreas, misiones y viajes"),
            Pages = new[]
            {
                T("When you enter an area for the first time, the mod describes it: its size, how open it is, where you are in it, its waypoint and where it leads. Double tap L3 to hear it again.",
                  "Cuando entras en un área por primera vez, el mod la describe: su tamaño, lo abierta que es, dónde estás, su transportador y adónde lleva. Toca L3 dos veces para oírlo otra vez."),
                T("The Quest kind of the scanner holds what the game marks for your quest. An exit to the area your quest names says your quest leads there.",
                  "El tipo Misión del escáner contiene lo que el juego marca para tu misión. Una salida al área que nombra tu misión dice que tu misión lleva allí."),
                T("Once any of your characters has finished the campaign, Quest route says the way through the areas to the one your quest names, as the world map shows it: Quest route: the Coast, then the Tidal Island. Exits you have found on that way say so. Where things are inside an area is still yours to find.",
                  "Cuando cualquiera de tus personajes ha terminado la campaña, Ruta de misión dice el camino por las áreas hasta la que nombra tu misión, como lo muestra el mapa del mundo: Ruta de misión: la Costa, después la Isla de la Marea. Las salidas que has encontrado en ese camino lo dicen. Dónde están las cosas dentro de un área sigues teniendo que encontrarlo tú."),
                T("At a waypoint, the world map says the act and your quest as it opens, and each place as you move to it.",
                  "En un transportador, el mapa del mundo dice el acto y tu misión al abrirse, y cada lugar al que te mueves."),
            },
        },
        new Lesson
        {
            Key = "tree",
            Title = T("9. The passive tree", "9. El árbol de pasivas"),
            Pages = new[]
            {
                T("Each passive you move to is read with what it does and whether it can be allocated. The mod never allocates anything for you.",
                  "Cada pasiva a la que te mueves se lee con lo que hace y si se puede asignar. El mod nunca asigna nada por ti."),
                T("With the tree open, the D-pad is the finder: up and down choose a list, such as keystones or notables, and left and right go through it, fewest points first. The one named becomes your target, and each passive you move to says which way it lies.",
                  "Con el árbol abierto, la cruceta es el buscador: arriba y abajo eligen una lista, como piedras angulares o notables, y izquierda y derecha la recorren, las de menos puntos primero. La nombrada pasa a ser tu objetivo, y cada pasiva a la que te mueves dice en qué dirección está."),
            },
        },
        new Lesson
        {
            Key = "help",
            Title = T("10. If something goes wrong", "10. Si algo falla"),
            Pages = new[]
            {
                T("If something is not read, or a sound is too much, most of what the mod does can be turned off or made quieter in this menu.",
                  "Si algo no se lee, o un sonido molesta, casi todo lo que hace el mod se puede desactivar o bajar en este menú."),
                T("The mod keeps logs of what it read and what it could not. Sending app, ExileApi, PoE1Access_dumps, PoE1Access_log.txt to whoever gave you the mod is how problems get fixed. Email addresses are blanked out in it.",
                  "El mod guarda registros de lo que leyó y de lo que no pudo leer. Enviar app, ExileApi, PoE1Access_dumps, PoE1Access_log.txt a quien te dio el mod es como se arreglan los problemas. Las direcciones de correo se borran en él."),
            },
        },
    };

    // The Tutorial submenu: a submenu per lesson, its pages, then "Try it now" where it has practice.
    private MenuItem TutorialMenu()
    {
        var lessons = Lessons().Select(LessonMenu).ToList();
        // The busiest screens and tasks, step by step with what the mod says (Guides.cs).
        lessons.Add(new MenuItem { Label = T("Step by step", "Paso a paso"), Children = Guides().Select(LessonMenu).ToList() });
        // The game's own mechanics, in the order the game brings them in (Mechanics.cs).
        lessons.Add(new MenuItem { Label = T("Game mechanics", "Mecánicas del juego"), Children = MechanicsLessons().Select(LessonMenu).ToList() });
        // Switches for the lessons that start by themselves (AutoTutorial.cs): move right to change.
        lessons.Add(new MenuItem
        {
            Label = T("Tutorials for this character", "Tutoriales para este personaje"),
            Text = T("Move right to turn them on or off.", "Muévete a la derecha para activarlos o desactivarlos."),
            StartPractice = ToggleTutorialsForCharacter,
            Describe = () => TutorialAnswer() == true ? T("Tutorials for this character: on", "Tutoriales para este personaje: activados") : T("Tutorials for this character: off", "Tutoriales para este personaje: desactivados"),
        });
        lessons.Add(new MenuItem
        {
            Label = T("Ask new characters about tutorials", "Preguntar a los personajes nuevos por los tutoriales"),
            Text = T("Move right to turn it on or off.", "Muévete a la derecha para activarlo o desactivarlo."),
            StartPractice = ToggleAskNewCharacters,
            Describe = () => SeenHas("ask:never") ? T("Ask new characters about tutorials: off", "Preguntar a los personajes nuevos: desactivado") : T("Ask new characters about tutorials: on", "Preguntar a los personajes nuevos: activado"),
        });
        return new MenuItem { Label = T("Tutorial", "Tutorial"), Children = lessons };
    }

    private MenuItem LessonMenu(Lesson lesson)
    {
        {
            var pages = lesson.Pages.Select(p => new MenuItem { Label = "", Text = p }).ToList();
            if (lesson.Practice.Length > 0)
            {
                Lesson l = lesson;
                pages.Add(new MenuItem
                {
                    Label = T("Try it now", "Pruébalo ahora"),
                    Text = T("Try it now: move right to close the menu and practise. Nothing is pressed for you.", "Pruébalo ahora: muévete a la derecha para cerrar el menú y practicar. No se pulsa nada por ti."),
                    StartPractice = () => StartPractice(l),
                });
            }
            return new MenuItem { Label = lesson.Title, Children = pages };
        }
    }

    private void StartPractice(Lesson lesson)
    {
        CloseMenu();
        _practice = lesson.Practice.ToList();
        _practiceLesson = lesson.Title;
        PluginLog.Write("Tutorial practice: " + lesson.Title);
        Say(_practice[0].Ask, false);
    }

    /// <summary>The player used one of the mod's controls ("category", "thing", "where", "guide",
    /// "status", "prompts"): the practice moves on when it is the one asked for.</summary>
    private void TutorialDid(string goal)
    {
        if (_practice.Count == 0 || _practice[0].Goal != goal) return;
        _practice.RemoveAt(0);
        // Heard at once, while the control's own answer is still being spoken (the spoken
        // "well done" waited behind it, so the step felt slow to register).
        Try(() => { _audio?.Play("stepdone", Synth.StepDone, 0.8f); return true; }, false);
        if (_practice.Count > 0) { Say(_practice[0].Ask, false); return; }
        PluginLog.Write("Tutorial practice done: " + _practiceLesson);
        if (AutoTutorials) { Say(T("Well done, that is the lesson.", "Muy bien, esa es la lección."), false); return; }
        Say(T("Well done, that is the lesson. Hold L3 for a second and a half for the menu and the next lesson.",
              "Muy bien, esa es la lección. Mantén L3 un segundo y medio para el menú y la siguiente lección."), false);
    }
}
