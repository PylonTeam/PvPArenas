# Arena voting checks

These exercise the compiled mod with the installed tModLoader types, without launching the game.
From the PvPArenas directory, compile the mod and copy its output without packaging loaded mods:

```powershell
dotnet msbuild PvPArenas.csproj '-t:Compile;CopyFilesToOutputDirectory' -p:Configuration=Release -p:BuildProjectReferences=false
dotnet run --project Tests/ArenaVoting -- "D:\Steam\steamapps\common\tModLoader" ".."
```

The referenced ErkySSC, PvPFramework, and Pylon Release assemblies must already be built.
Coverage includes config migration and round trips, fixed boss identities, per-fight stats,
ballot validation and synchronization, full-duration voting (including single-player),
winner selection, presentation lifetime, and retaining the voted boss when arena preparation fails.

To verify the localization keys in the packaged mod using tModLoader's own loader:

```powershell
dotnet build PvPArenas.csproj -c Release
dotnet run --project Tests/ArenaVoting -- "D:\Steam\steamapps\common\tModLoader" ".." --localization
```
