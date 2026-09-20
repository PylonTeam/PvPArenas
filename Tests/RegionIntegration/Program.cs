using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2)
    throw new ArgumentException("Pass the tModLoader installation and ModSources directories.");

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
RegionIntegrationTests.Run(Assembly.LoadFrom(Path.Combine(sources, "PvPArenas/bin/Release/net8.0/PvPArenas.dll")));
