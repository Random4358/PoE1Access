using static PoE1Access.Loc;

namespace PoE1Access;

// How each class plays, said after its name on the class screen of character creation (moving
// the outline from one Exile to the next), in the mod's own words from beginner guides and the
// game's long-standing facts: its attributes, where it starts on the passive tree, the weapon and
// skill it starts with, and how that plays with the mod's sounds. Ascendancy names are left out:
// they have changed between leagues (3.25 to 3.29) and the game names them at the Labyrinth.
// "Describe how each class plays" in the Speech submenu turns this off.
public partial class PoE1AccessCore
{
    private static string AnyClassNote =>
        T(" Any class can use any skill gem; the class only sets where you start on the passive tree and your ascendancy choices.",
          " Cualquier clase puede usar cualquier gema de habilidad; la clase solo decide dónde empiezas en el árbol de pasivas y tus ascendencias.");

    /// <summary>How the class plays, by class number (0 Scion ... 6 Shadow), or "".</summary>
    private static string ClassDescription(int number) => number switch
    {
        1 => T("Strength. Starts with a big two-handed mace and Heavy Strike. The toughest start: its side of the tree has lots of life, armour and life regeneration. Close-up melee: you walk into enemies and hit them, so listen for the melee reach ping. Easy for a first character, a bit slow.",
               "Fuerza. Empieza con una gran maza a dos manos y Golpe pesado. El comienzo más resistente: su lado del árbol tiene mucha vida, armadura y regeneración de vida. Cuerpo a cuerpo: caminas hacia los enemigos y los golpeas, así que atiende al aviso de alcance. Fácil para un primer personaje, algo lento."),
        4 => T("Strength and dexterity. Starts with a sword and Double Strike. A fighter of weapons: swords, axes, maces, often with a shield, or two weapons at once. Melee, with more speed and evasion than the Marauder and a bit less life. Good with the melee reach ping and the dodge warning.",
               "Fuerza y destreza. Empieza con una espada y Golpe doble. Un luchador de armas: espadas, hachas, mazas, a menudo con escudo, o dos armas a la vez. Cuerpo a cuerpo, con más velocidad y evasión que el Merodeador y algo menos de vida. Va bien con el aviso de alcance y el aviso para esquivar."),
        2 => T("Dexterity. Starts with a bow and Burning Arrow. Ranged and quick: its side of the tree has evasion, movement speed and projectile damage. You shoot from a distance and keep moving, so enemy sounds and the dodge warning tell you when to step away. Less life than the strength classes.",
               "Destreza. Empieza con un arco y Flecha ardiente. A distancia y rápida: su lado del árbol tiene evasión, velocidad de movimiento y daño de proyectiles. Disparas desde lejos y te mueves sin parar, así que los sonidos de enemigos y el aviso para esquivar te dicen cuándo apartarte. Menos vida que las clases de fuerza."),
        6 => T("Dexterity and intelligence. Starts with a dagger and Viper Strike, a poison strike. The most varied class: critical strikes, poison, traps and mines, or spells. Often fragile at first, with energy shield and evasion. Traps and mines let you place damage and stay back. Rewarding, less forgiving for a first character.",
               "Destreza e inteligencia. Empieza con una daga y Golpe de víbora, un golpe venenoso. La clase más variada: golpes críticos, veneno, trampas y minas, o hechizos. Suele ser frágil al principio, con escudo de energía y evasión. Las trampas y minas te dejan colocar el daño y quedarte atrás. Gratificante, menos indulgente para un primer personaje."),
        3 => T("Intelligence. Starts with a wand and Fireball. A caster: fire, cold, lightning or chaos spells, curses, or minions such as skeletons and zombies that fight for you. Fragile, with energy shield more than life. Minions are a calm start: they hold the enemies' attention while you stay back.",
               "Inteligencia. Empieza con una varita y Bola de fuego. Lanza hechizos: de fuego, frío, rayo o caos, maldiciones, o esbirros como esqueletos y zombis que luchan por ti. Frágil, con más escudo de energía que vida. Los esbirros son un comienzo tranquilo: atraen a los enemigos mientras tú te quedas atrás."),
        5 => T("Strength and intelligence. Starts with a sceptre and Glacial Hammer. A holy warrior: melee or spells, with lots of elemental damage, especially lightning, and both armour and energy shield. Tough and steady, it can fight close up or cast from a few steps back. A good first character.",
               "Fuerza e inteligencia. Empieza con un cetro y Martillo glacial. Un guerrero sagrado: cuerpo a cuerpo o hechizos, con mucho daño elemental, sobre todo de rayo, y tanto armadura como escudo de energía. Resistente y constante, puede luchar de cerca o lanzar hechizos a unos pasos. Un buen primer personaje."),
        0 => T("All three attributes. Starts with a sword and Spectral Throw, which throws a copy of your weapon. Starts in the middle of the passive tree and can reach every other class's side, so she can become anything, but she takes longer to get strong and her ascendancy choices are unlike the others'. Best once you know the game.",
               "Los tres atributos. Empieza con una espada y Lanzamiento espectral, que lanza una copia de tu arma. Empieza en el centro del árbol de pasivas y puede llegar al lado de cualquier otra clase, así que puede ser cualquier cosa, pero tarda más en hacerse fuerte y sus ascendencias son distintas a las de las demás. Mejor cuando ya conoces el juego."),
        _ => "",
    };
}
