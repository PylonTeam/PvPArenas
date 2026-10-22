using System;
using System.IO;

namespace PvPArenas.Common.Generation;

/// <summary>Extends the bundled world with empty rows before vanilla allocates the session world.</summary>
internal static class ArenaWorldFile
{
    internal static byte[] Expand(byte[] source)
    {
        using BinaryReader reader = new(new MemoryStream(source, writable: false));
        // This transformer belongs to the checked-in Arenas_v10 asset, whose native
        // format is 279. Reject a replacement format instead of guessing its layout.
        if (reader.ReadInt32() != 279)
            throw new InvalidDataException("The bundled arena uses an unsupported world file format.");
        reader.BaseStream.Position = 24; // Version and Terraria's 20-byte file metadata.
        int sections = reader.ReadInt16();
        if (sections < 3 || sections > 32) throw new InvalidDataException("Invalid arena section table.");
        int[] positions = new int[sections];
        for (int i = 0; i < sections; i++)
        {
            positions[i] = reader.ReadInt32();
            if (positions[i] < 0 || positions[i] > source.Length || i > 0 && positions[i] < positions[i - 1])
                throw new InvalidDataException("Invalid arena section position.");
        }
        bool[] important = new bool[reader.ReadUInt16()];
        for (int start = 0; start < important.Length; start += 8)
        {
            int flags = reader.ReadByte();
            for (int bit = 0; bit < 8 && start + bit < important.Length; bit++)
                important[start + bit] = (flags & 1 << bit) != 0;
        }
        if (reader.BaseStream.Position != positions[0]) throw new InvalidDataException("Invalid arena format header.");
        reader.ReadString(); reader.ReadString(); reader.ReadUInt64(); reader.ReadBytes(16);
        reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32();
        long bottomOffset = reader.BaseStream.Position;
        reader.ReadInt32();
        int height = reader.ReadInt32(), width = reader.ReadInt32();
        if (width != ArenaWorldSystem.Width || height != ArenaWorldSystem.LegacyHeight)
            throw new InvalidDataException("The bundled arena must remain 850×600.");

        using MemoryStream result = new(source.Length + width * 3);
        using BinaryWriter writer = new(result);
        writer.Write(source, 0, positions[1]);
        reader.BaseStream.Position = positions[1];
        for (int x = 0; x < width; x++)
        {
            int columnStart = (int)reader.BaseStream.Position, y = 0;
            while (y < height) y += ReadRun(reader, important);
            if (y != height) throw new InvalidDataException("An arena tile run crosses its column boundary.");
            writer.Write(source, columnStart, (int)reader.BaseStream.Position - columnStart);
            writer.Write((byte)0x80); // Empty tile with a 16-bit repeat count.
            writer.Write((short)(ArenaWorldSystem.Height - height - 1));
        }
        if (reader.BaseStream.Position != positions[2]) throw new InvalidDataException("Invalid arena tile section.");
        int added = (int)result.Position - positions[2];
        writer.Write(source, positions[2], source.Length - positions[2]);
        result.Position = 26;
        for (int i = 0; i < sections; i++) writer.Write(positions[i] + (i >= 2 ? added : 0));
        result.Position = bottomOffset;
        writer.Write(ArenaWorldSystem.Height * 16);
        writer.Write(ArenaWorldSystem.Height);
        return result.ToArray();
    }

    // Native v279 tile headers: flags, optional tile/frame/paint, wall/paint,
    // liquid, high wall byte, then RLE. Copy the encoded records verbatim.
    private static int ReadRun(BinaryReader reader, bool[] important)
    {
        byte first = reader.ReadByte();
        byte second = (first & 1) != 0 ? reader.ReadByte() : (byte)0;
        byte third = (second & 1) != 0 ? reader.ReadByte() : (byte)0;
        if ((third & 1) != 0) reader.ReadByte();
        if ((first & 2) != 0)
        {
            int type = (first & 32) != 0 ? reader.ReadUInt16() : reader.ReadByte();
            if (type >= important.Length) throw new InvalidDataException("Invalid arena tile type.");
            if (important[type]) { reader.ReadInt16(); reader.ReadInt16(); }
            if ((third & 8) != 0) reader.ReadByte();
        }
        if ((first & 4) != 0)
        {
            reader.ReadByte();
            if ((third & 16) != 0) reader.ReadByte();
        }
        if ((first & 24) != 0) reader.ReadByte();
        if ((third & 64) != 0) reader.ReadByte();
        int repeats = (first >> 6) switch { 0 => 0, 1 => reader.ReadByte(), _ => reader.ReadInt16() };
        if (repeats < 0) throw new InvalidDataException("Invalid arena tile run.");
        return repeats + 1;
    }
}
