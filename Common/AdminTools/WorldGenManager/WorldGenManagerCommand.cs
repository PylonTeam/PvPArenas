using System;
using System.Linq;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

internal sealed class WorldGenManagerCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat | CommandType.Console;
    public override string Command => "worldgen";
    public override string Usage => "/worldgen [status | list | run <pass, pass, ...>]";
    public override string Description => "Select and run native world generation passes.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        WorldGenPassRunner runner = ModContent.GetInstance<WorldGenPassRunner>();
        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case null or "ui" or "open":
                if (!Main.dedServ)
                    ModContent.GetInstance<WorldGenManagerUISystem>().Toggle();
                else
                    caller.Reply(Usage);
                break;
            case "status":
                caller.Reply($"{runner.Status} · {runner.Progress:P0} · Seed {runner.Seed}");
                break;
            case "list":
                caller.Reply(string.Join(", ", runner.PassNames));
                break;
            case "run":
                string[] names = string.Join(' ', args.Skip(1))
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (!WorldGenManagerNetHandler.RequestRunPasses(names, out string error))
                    caller.Reply(error, Color.OrangeRed);
                break;
            default:
                caller.Reply(Usage);
                break;
        }
    }
}
