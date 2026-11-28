using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PoE1Access;

/// <summary>
/// The game's own sounds, played from your installed game: Path of Exile keeps them in FMOD
/// banks beside the game (FMOD\Desktop\*.bank) and ships the FMOD Studio library that plays
/// them (fmod.dll, fmodstudio.dll). Nothing of the game's is copied or shipped with the mod: the
/// banks are opened where the game keeps them, with the game's own library, when the mod starts.
/// Used for the loot sounds (the game's drop sound for each kind of item, ItemDrops.bank, and
/// its unique drop sound). If anything is missing the mod keeps its own synthesized sounds.
/// Checked 21 Nov 2026 with FMOD 2.03.11.
/// </summary>
internal sealed class GameSounds : IDisposable
{
    private const uint HeaderVersion = 0x00020311;
    private static readonly string[] Banks = { "Master Bank.bank", "Master Bank.strings.bank", "ItemDrops.bank", "_Effects_misc_unique_drop.bank" };

    private IntPtr _system;
    private readonly Dictionary<string, IntPtr> _events = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
    public bool Available { get; private set; }
    public string Error { get; private set; } = "";

    [StructLayout(LayoutKind.Sequential)]
    private struct Vec { public float X, Y, Z; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes3D { public Vec Position, Velocity, Forward, Up; }

    private delegate int CreateFn(out IntPtr system, uint headerVersion);
    private delegate int InitFn(IntPtr system, int maxChannels, uint studioFlags, uint flags, IntPtr extra);
    private delegate int LoadBankFn(IntPtr system, [MarshalAs(UnmanagedType.LPUTF8Str)] string file, uint flags, out IntPtr bank);
    private delegate int GetEventFn(IntPtr system, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr description);
    private delegate int CreateInstanceFn(IntPtr description, out IntPtr instance);
    private delegate int Set3DFn(IntPtr instance, ref Attributes3D attributes);
    private delegate int SetFloatFn(IntPtr instance, float value);
    private delegate int InstanceFn(IntPtr instance);
    private delegate int SystemFn(IntPtr system);
    private delegate int SetPropertyFn(IntPtr instance, int index, float value);
    private delegate int DescCountFn(IntPtr description, out int count);
    private delegate int DescFn(IntPtr description);
    private SetPropertyFn _setProperty; private DescCountFn _instanceCount; private DescFn _releaseAll;

    private CreateFn _create; private InitFn _init; private LoadBankFn _loadBank; private GetEventFn _getEvent;
    private CreateInstanceFn _createInstance; private Set3DFn _set3D; private SetFloatFn _setVolume, _setPitch;
    private InstanceFn _start, _release; private SystemFn _update, _releaseSystem;

    // FMOD's core library, for sound files of your own (the sound pack, see SoundPack).
    private delegate int GetCoreFn(IntPtr studio, out IntPtr core);
    private delegate int CreateSoundFn(IntPtr core, byte[] nameUtf8, uint mode, IntPtr exinfo, out IntPtr sound);
    private delegate int PlaySoundFn(IntPtr core, IntPtr sound, IntPtr group, int paused, out IntPtr channel);
    private delegate int ChannelFloatFn(IntPtr channel, float value);
    private delegate int ChannelIntFn(IntPtr channel, int value);
    private delegate int ChannelFn(IntPtr channel);
    private delegate int ChannelPlayingFn(IntPtr channel, out int playing);
    private GetCoreFn _getCore; private CreateSoundFn _createSound; private PlaySoundFn _playSound;
    private ChannelFloatFn _chVolume, _chPan, _chPitch; private ChannelIntFn _chPaused; private ChannelFn _chStop; private ChannelPlayingFn _chPlaying;
    private IntPtr _core;
    private readonly Dictionary<string, IntPtr> _files = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

    public GameSounds()
    {
        try
        {
            string game = GameFolder();
            if (game == null) { Error = "game folder not found"; return; }
            string desktop = Path.Combine(game, "FMOD", "Desktop");
            if (!File.Exists(Path.Combine(game, "fmodstudio.dll")) || !Directory.Exists(desktop)) { Error = "no FMOD in " + game; return; }
            IntPtr core = NativeLibrary.Load(Path.Combine(game, "fmod.dll"));
            T C<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(core, name));
            _createSound = C<CreateSoundFn>("FMOD_System_CreateSound");
            _playSound = C<PlaySoundFn>("FMOD_System_PlaySound");
            _chVolume = C<ChannelFloatFn>("FMOD_Channel_SetVolume");
            _chPan = C<ChannelFloatFn>("FMOD_Channel_SetPan");
            _chPitch = C<ChannelFloatFn>("FMOD_Channel_SetPitch");
            _chPaused = C<ChannelIntFn>("FMOD_Channel_SetPaused");
            _chStop = C<ChannelFn>("FMOD_Channel_Stop");
            _chPlaying = C<ChannelPlayingFn>("FMOD_Channel_IsPlaying");
            IntPtr lib = NativeLibrary.Load(Path.Combine(game, "fmodstudio.dll"));
            T F<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
            _create = F<CreateFn>("FMOD_Studio_System_Create");
            _init = F<InitFn>("FMOD_Studio_System_Initialize");
            _loadBank = F<LoadBankFn>("FMOD_Studio_System_LoadBankFile");
            _getEvent = F<GetEventFn>("FMOD_Studio_System_GetEvent");
            _createInstance = F<CreateInstanceFn>("FMOD_Studio_EventDescription_CreateInstance");
            _set3D = F<Set3DFn>("FMOD_Studio_EventInstance_Set3DAttributes");
            _setVolume = F<SetFloatFn>("FMOD_Studio_EventInstance_SetVolume");
            _setPitch = F<SetFloatFn>("FMOD_Studio_EventInstance_SetPitch");
            _start = F<InstanceFn>("FMOD_Studio_EventInstance_Start");
            _release = F<InstanceFn>("FMOD_Studio_EventInstance_Release");
            // For replaying the game's drop sounds as often as the mod needs (see Play).
            try { _setProperty = F<SetPropertyFn>("FMOD_Studio_EventInstance_SetProperty"); } catch { }
            try { _instanceCount = F<DescCountFn>("FMOD_Studio_EventDescription_GetInstanceCount"); } catch { }
            try { _releaseAll = F<DescFn>("FMOD_Studio_EventDescription_ReleaseAllInstances"); } catch { }
            _update = F<SystemFn>("FMOD_Studio_System_Update");
            _releaseSystem = F<SystemFn>("FMOD_Studio_System_Release");
            _getCore = F<GetCoreFn>("FMOD_Studio_System_GetCoreSystem");

            int r = _create(out _system, HeaderVersion);
            if (r != 0) { Error = "FMOD create " + r + " (another FMOD version?)"; return; }
            r = _init(_system, 64, 0, 0, IntPtr.Zero);
            if (r != 0) { Error = "FMOD initialise " + r; return; }
            foreach (string bank in Banks)
            {
                r = _loadBank(_system, Path.Combine(desktop, bank), 0, out _);
                if (r != 0) { Error = "bank " + bank + ": " + r; return; }
            }
            Available = true;
            if (_getCore(_system, out _core) != 0) _core = IntPtr.Zero;
        }
        catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; }
    }

    // The folder of the running game (its executable's folder).
    private static string GameFolder()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.StartsWith("PathOfExile2", StringComparison.OrdinalIgnoreCase)) continue;
                string exe = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) return Path.GetDirectoryName(exe);
            }
            catch { }
        }
        return null;
    }

    /// <summary>Plays one of the game's events ("event:/Sound Effects/ItemSounds/orb.ogg") from
    /// the side given by pan (-1 left .. 1 right); false if it cannot.</summary>
    public bool Play(string eventPath, float pan, float pitch, float volume)
    {
        if (!Available || string.IsNullOrEmpty(eventPath)) return false;
        try
        {
            if (!_events.TryGetValue(eventPath, out IntPtr description))
            {
                if (_getEvent(_system, eventPath, out description) != 0) description = IntPtr.Zero;
                _events[eventPath] = description;
            }
            if (description == IntPtr.Zero) return false;
            // The game's events keep a sound from playing again too soon (a cooldown) or from
            // playing more than once or twice at a time, which it needs, not the mod: a gold
            // pile's sound played once and never again (28 Nov 2026). Copies still playing make
            // way, and the new one has no cooldown.
            if (_instanceCount != null && _releaseAll != null && _instanceCount(description, out int alive) == 0 && alive >= 2) _releaseAll(description);
            if (_createInstance(description, out IntPtr instance) != 0) return false;
            _setProperty?.Invoke(instance, 5, 0f);   // FMOD_STUDIO_EVENT_PROPERTY_COOLDOWN: none
            // The listener stands at the origin facing +Z; the sound two steps ahead, to the side.
            var at = new Attributes3D
            {
                Position = new Vec { X = 2.5f * Math.Max(-1f, Math.Min(1f, pan)), Y = 0, Z = 1.5f },
                Forward = new Vec { Z = 1 },
                Up = new Vec { Y = 1 },
            };
            _set3D(instance, ref at);
            _setVolume(instance, Math.Max(0f, volume));
            _setPitch(instance, pitch);
            _start(instance);
            _release(instance);   // freed by FMOD once it has played
            return true;
        }
        catch { return false; }
    }

    /// <summary>Plays a sound file (.ogg, .wav) once or looping, from the side given by pan;
    /// the channel is returned to move it, change its volume or stop it.</summary>
    public bool PlayFile(string file, bool loop, float pan, float pitch, float volume, out IntPtr channel)
    {
        channel = IntPtr.Zero;
        if (!Available || _core == IntPtr.Zero || string.IsNullOrEmpty(file)) return false;
        try
        {
            string key = (loop ? "loop:" : "once:") + file;
            if (!_files.TryGetValue(key, out IntPtr sound))
            {
                // FMOD_2D | FMOD_CREATESAMPLE | FMOD_LOOP_NORMAL or FMOD_LOOP_OFF
                uint mode = 0x8 | 0x100 | (loop ? 0x2u : 0x1u);
                if (_createSound(_core, Encoding.UTF8.GetBytes(file + "\0"), mode, IntPtr.Zero, out sound) != 0) sound = IntPtr.Zero;
                _files[key] = sound;
            }
            if (sound == IntPtr.Zero) return false;
            if (_playSound(_core, sound, IntPtr.Zero, 1, out channel) != 0) return false;
            SetChannel(channel, pan, pitch, volume);
            _chPaused(channel, 0);
            return true;
        }
        catch { return false; }
    }

    public void SetChannel(IntPtr channel, float pan, float pitch, float volume)
    {
        if (channel == IntPtr.Zero) return;
        try
        {
            _chPan(channel, Math.Max(-1f, Math.Min(1f, pan)));
            _chPitch(channel, pitch);
            _chVolume(channel, Math.Max(0f, volume));
        }
        catch { }
    }

    public bool IsPlaying(IntPtr channel)
    {
        if (channel == IntPtr.Zero) return false;
        try { return _chPlaying(channel, out int playing) == 0 && playing != 0; } catch { return false; }
    }

    public void Stop(IntPtr channel)
    {
        if (channel == IntPtr.Zero) return;
        try { _chStop(channel); } catch { }
    }

    /// <summary>FMOD's own work (mixing, freeing finished sounds); called from every tick.</summary>
    public void Update()
    {
        if (!Available) return;
        try { _update(_system); } catch { }
    }

    public void Dispose()
    {
        if (_system == IntPtr.Zero) return;
        try { _releaseSystem(_system); } catch { }
        _system = IntPtr.Zero;
        Available = false;
    }

    // ---- Which of the game's drop sounds an item makes ----

    public const string UniqueLayer = "event:/Sound Effects/ItemSounds/UniqueDrop/UniqueDrop.oggSG";

    /// <summary>The game's drop sound for an item: by its class and base path, as the game gives
    /// each kind its own (an orb for currency, a ring, a helm, metal or leather armour...).</summary>
    public static string DropEvent(string cls, string path)
    {
        string E(string name) => "event:/Sound Effects/ItemSounds/" + name + ".ogg";
        cls ??= ""; path ??= "";
        bool Has(string s) => cls.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        bool P(string s) => path.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        // Attribute of an armour base from its path ("BodyStr1", "GlovesDexInt2").
        string attr = P("StrDex") ? "StrDex" : P("StrInt") ? "StrInt" : P("DexInt") ? "DexInt" : P("Str") ? "Str" : P("Dex") ? "Dex" : P("Int") ? "Int" : "";

        // Gold has its own coin sound in the game (Metadata/Items/Currency/GoldCoin -> GoldCoin.ogg,
        // found 28 Nov 2026 in ItemDrops.bank): the game plays it as gold drops, and the mod used the
        // orb's, so the game's coins on a kill sounded like something the scanner did not list.
        if (cls == "Gold" || path.EndsWith("/GoldCoin", StringComparison.OrdinalIgnoreCase)) return E("GoldCoin");
        if (path.EndsWith("CurrencyIdentification") || path.EndsWith("CurrencyPortal")) return E("Scroll");
        if (P("/Essence")) return E("EssenceItem");
        if (P("/Delve") && P("Fossil")) return E("DelveFossil");
        if (Has("Currency") || path.StartsWith("Metadata/Items/Currency")) return E("orb");
        if (Has("Gem") || path.StartsWith("Metadata/Items/Gems")) return E("Gem");
        if (Has("Flask")) return E("Flask");
        if (cls == "Map" || path.StartsWith("Metadata/Items/Maps")) return E("Map");
        if (Has("Divination")) return E("DivinationCard");
        if (Has("Jewel")) return E("Jewel");
        if (Has("Ring")) return E("Ring");
        if (Has("Amulet")) return E("Amulet");
        if (Has("Belt")) return E("Belt");
        if (Has("Quiver")) return E("quiver");
        if (Has("Body Armour")) return E(attr == "Str" ? "Metal_armour" : attr == "Dex" ? "Leather_armour" : attr == "Int" ? "cloth_armour" : "chain_armour");
        if (Has("Helmet")) return E(attr == "Int" ? "cap" : "Helm");
        if (Has("Gloves")) return E(attr == "Str" || attr == "StrInt" || attr == "StrDex" ? "chain_gloves" : "Leather_glove");
        if (Has("Boots")) return E(attr == "Str" ? "Metal_boots" : attr.Length == 6 ? "chain_boots" : "Boots");
        if (Has("Shield")) return E(attr == "Str" ? "Metal_shield" : attr == "Dex" ? "wooden_shield" : "Metal_and_wooden_shield");
        if (Has("Two Hand Sword")) return E("Large_sword");
        if (Has("Sword")) return E("Sword");
        if (Has("Axe")) return E("1HandAxe");
        if (Has("Two Hand Mace")) return E("Maul");
        if (Has("Sceptre")) return E("Scepter");
        if (Has("Mace")) return E("1HandMace");
        if (Has("Claw")) return E("1HandClaw");
        if (Has("Dagger")) return E("1HandDagger");
        if (Has("Wand")) return E("wand");
        if (Has("Staff") || Has("Warstaff")) return E("Staff");
        if (Has("Bow")) return E("Bow");
        if (Has("Fishing")) return E("Small_wooden_weapon");
        return E("item_drop_large");
    }
}
