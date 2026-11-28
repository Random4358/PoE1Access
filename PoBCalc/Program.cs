// PoBCalc: runs Path of Building Community's own calculations without its window, for PoE1Access.
// It loads the LuaJIT library from the installed Path of Building (lua51.dll), runs driver.lua
// there (after Path of Building's HeadlessWrapper.lua), and driver.lua writes the build's main
// numbers to the output file as "key=value" lines. Nothing touches the game.
//
//   PoBCalc.exe <Path of Building folder> <driver.lua> <build.xml> <out.txt> [character]
using System; using System.IO; using System.Runtime.InteropServices; using System.Text;
string pob = args[0], driver = args[1], xmlFile = args[2], outFile = args[3];
Directory.SetCurrentDirectory(pob);
NativeLibrary.SetDllImportResolver(typeof(Lua).Assembly, (name, asm, path) => name == "lua51" ? NativeLibrary.Load(Path.Combine(pob, "lua51.dll")) : IntPtr.Zero);
IntPtr L = Lua.luaL_newstate();
Lua.luaL_openlibs(L);
string mode = args.Length > 4 ? args[4] : "build";
string pre = "POBCALC_MODE=[==[" + mode + "]==] POBCALC_XML=[==[" + xmlFile.Replace("\\", "/") + "]==] POBCALC_OUT=[==[" + outFile.Replace("\\", "/") + "]==] POBCALC_DIR=[==[" + Path.GetDirectoryName(Path.GetFullPath(driver)).Replace("\\", "/") + "]==]";
Run(pre, "pre");
Run(File.ReadAllText(driver), "driver");
Lua.lua_close(L);

void Run(string code, string name)
{
    byte[] b = Encoding.UTF8.GetBytes(code);
    int r = Lua.luaL_loadbuffer(L, b, (UIntPtr)b.Length, "=" + name);
    if (r == 0) r = Lua.lua_pcall(L, 0, 0, 0);
    if (r != 0)
    {
        IntPtr s = Lua.lua_tolstring(L, -1, out UIntPtr len);
        string msg = s == IntPtr.Zero ? "?" : Marshal.PtrToStringUTF8(s, (int)len);
        File.AppendAllText(outFile, "error=" + msg.Replace("\n", " | ") + "\n");
        Console.Error.WriteLine(msg);
        Environment.Exit(2);
    }
}

static class Lua
{
    [DllImport("lua51", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr luaL_newstate();
    [DllImport("lua51", CallingConvention = CallingConvention.Cdecl)] public static extern void luaL_openlibs(IntPtr L);
    [DllImport("lua51", CallingConvention = CallingConvention.Cdecl)] public static extern int luaL_loadbuffer(IntPtr L, byte[] buff, UIntPtr sz, string name);
    [DllImport("lua51", CallingConvention = CallingConvention.Cdecl)] public static extern int lua_pcall(IntPtr L, int nargs, int nresults, int errfunc);
    [DllImport("lua51", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr lua_tolstring(IntPtr L, int idx, out UIntPtr len);
    [DllImport("lua51", CallingConvention = CallingConvention.Cdecl)] public static extern void lua_close(IntPtr L);
}
