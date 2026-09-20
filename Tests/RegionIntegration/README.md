# Arenas lobby region checks

Build the Release DLLs for ErkySSC, PvPFramework, Pylon, and PvPArenas first. From
the PvPArenas directory, run:

```powershell
dotnet run --project Tests/RegionIntegration -- "<tModLoader installation>" "<ModSources directory>"
```

The checks use the compiled mods and real tModLoader types without starting a
game, network listener, or Steam session. They cover lobby activation, countdown
and combat deactivation, preserving manual regions and saved settings, world
spawn movement, save/reload, late-join snapshots, and cleanup.
