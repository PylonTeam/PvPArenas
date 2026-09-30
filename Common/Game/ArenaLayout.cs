using System.IO;
using Terraria.Enums;

namespace PvPArenas.Common.Game;

internal sealed record ArenaLayout(Rectangle ArenaBounds, Point BlueSpawn, Point RedSpawn)
{
    internal const int SpawnBoxWidth = 20;
    internal const int SpawnBoxHeight = 10;

    internal Rectangle BossBounds => new(
        ArenaBounds.X + ArenaBounds.Width / 4,
        ArenaBounds.Y,
        ArenaBounds.Width / 2,
        ArenaBounds.Height);

    internal Point BossSpawn { get; init; } = ArenaBounds.Center;

    internal Rectangle RedSpawnBox => new(
        ArenaBounds.Left,
        RedSpawn.Y - SpawnBoxHeight,
        SpawnBoxWidth,
        SpawnBoxHeight);

    internal Rectangle BlueSpawnBox => new(
        ArenaBounds.Right - SpawnBoxWidth,
        BlueSpawn.Y - SpawnBoxHeight,
        SpawnBoxWidth,
        SpawnBoxHeight);

    internal Point PlayerSpawn(Team team) => team switch
    {
        Team.Blue => BlueSpawn,
        Team.Red => RedSpawn,
        _ => ArenaBounds.Center
    };

    internal void Write(BinaryWriter writer)
    {
        Write(writer, ArenaBounds);
        Write(writer, BlueSpawn);
        Write(writer, RedSpawn);
        Write(writer, BossSpawn);
    }

    internal static ArenaLayout Read(BinaryReader reader) => new(
        ReadRectangle(reader), ReadPoint(reader), ReadPoint(reader)) { BossSpawn = ReadPoint(reader) };

    private static void Write(BinaryWriter writer, Rectangle value)
    {
        writer.Write(value.X); writer.Write(value.Y);
        writer.Write(value.Width); writer.Write(value.Height);
    }

    private static void Write(BinaryWriter writer, Point value)
    {
        writer.Write(value.X); writer.Write(value.Y);
    }

    private static Rectangle ReadRectangle(BinaryReader reader) => new(
        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    private static Point ReadPoint(BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32());
}
