using System.Reflection;
using System.Runtime.Loader;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--localization")
    throw new ArgumentException("Pass the tModLoader installation and ModSources directories, optionally followed by --localization.");

string tmlDirectory = Path.GetFullPath(args[0]);
string sources = Path.GetFullPath(args[1]);
string[] libraries = Directory.GetFiles(Path.Combine(tmlDirectory, "Libraries"), "*.dll", SearchOption.AllDirectories)
    .Concat(Directory.GetFiles(Path.Combine(sources, "ErkySSC/lib"), "*.dll")).ToArray();
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string mod = Path.Combine(sources, name.Name!, "bin/Release/net8.0", name.Name + ".dll");
    if (File.Exists(mod)) return context.LoadFromAssemblyPath(mod);
    string platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
    string[] matches = libraries.Where(path => Path.GetFileNameWithoutExtension(path) == name.Name).ToArray();
    string dependency = matches.FirstOrDefault(path => path.Replace('\\', '/').Contains($"/runtimes/{platform}/lib/"))
        ?? matches.FirstOrDefault(path => !path.Replace('\\', '/').Contains("/runtimes/"));
    return dependency == null ? null : context.LoadFromAssemblyPath(dependency);
};

Assembly tml = Assembly.LoadFrom(Path.Combine(tmlDirectory, "tModLoader.dll"));
tml.GetType("Terraria.Program")!.GetField("SavePath")!.SetValue(null, AppContext.BaseDirectory);
if (args.Length == 3)
    LocalizationTests.Run(Path.GetFullPath(Path.Combine(sources, "../Mods/PvPArenas.tmod")));
else
    ArenaVotingTests.Run(Assembly.LoadFrom(Path.Combine(sources, "PvPArenas/bin/Release/net8.0/PvPArenas.dll")));
