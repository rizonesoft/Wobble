using System.Text.RegularExpressions;

namespace Wobble.Core;

/// <summary>Loads a CD described by a cue sheet and its binary track files.</summary>
public static partial class CueSheet
{
    private sealed class CueTrack
    {
        public int Number;
        public TrackMode Mode;
        public int SectorSize;
        public ImageFile File = null!;
        public long VirtualPregap;      // PREGAP: silence that is not in the file
        public long? Index0;            // frames from the start of the file
        public long Index1;
    }

    public static Media Load(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var files = new List<ImageFile>();
        var tracks = new List<CueTrack>();
        try
        {
            ImageFile? file = null;
            CueTrack? track = null;
            foreach (string line in File.ReadLines(path))
            {
                var m = Command().Match(line);
                if (!m.Success) continue;
                string argument = m.Groups[2].Value.Trim();
                switch (m.Groups[1].Value.ToUpperInvariant())
                {
                    case "FILE":
                        var name = FileName().Match(argument);
                        if (!name.Success || !name.Groups[3].Value.Equals("BINARY", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Unsupported FILE line in {path}: {line.Trim()}");
                        file = new ImageFile(Path.Combine(folder, name.Groups[1].Success ? name.Groups[1].Value : name.Groups[2].Value));
                        files.Add(file);
                        break;
                    case "TRACK":
                        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var (mode, size) = parts[1].ToUpperInvariant() switch
                        {
                            "AUDIO" => (TrackMode.Audio, 2352),
                            "MODE1/2352" => (TrackMode.Mode1, 2352),
                            "MODE1/2048" => (TrackMode.Mode1, 2048),
                            "MODE2/2352" => (TrackMode.Mode2, 2352),
                            _ => throw new InvalidDataException($"Unsupported track type in {path}: {parts[1]}"),
                        };
                        track = new CueTrack
                        {
                            Number = int.Parse(parts[0]), Mode = mode, SectorSize = size,
                            File = file ?? throw new InvalidDataException($"TRACK before FILE in {path}"),
                        };
                        tracks.Add(track);
                        break;
                    case "PREGAP" when track is not null:
                        track.VirtualPregap = Frames(argument);
                        break;
                    case "INDEX" when track is not null:
                        var index = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (int.Parse(index[0]) == 0) track.Index0 = Frames(index[1]);
                        else if (int.Parse(index[0]) == 1) track.Index1 = Frames(index[1]);
                        break;
                }
            }
            if (tracks.Count == 0) throw new InvalidDataException($"No tracks in {path}");

            var layout = new List<Track>();
            var extents = new List<Extent>();
            long position = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t.VirtualPregap > 0)
                {
                    extents.Add(new Extent(position, t.VirtualPregap, t.Mode, 0, null));
                    position += t.VirtualPregap;
                }

                // The track's part of the file runs from its first index to the next track's first index, or to the end.
                long begin = t.Index0 ?? t.Index1;
                bool sharesFile = i + 1 < tracks.Count && ReferenceEquals(tracks[i + 1].File, t.File);
                long end = sharesFile ? tracks[i + 1].Index0 ?? tracks[i + 1].Index1 : t.File.Length / t.SectorSize;
                if (end <= begin) throw new InvalidDataException($"Track {t.Number} in {path} has no data.");

                var source = t.File;
                long byteOffset = begin * t.SectorSize;
                int sectorSize = t.SectorSize;
                extents.Add(new Extent(position, end - begin, t.Mode, sectorSize,
                    (index, destination) => source.Read(byteOffset + index * sectorSize, destination)));

                long start = position + (t.Index1 - begin);
                layout.Add(new Track(t.Number, t.Mode, start, position + (end - begin) - start, t.VirtualPregap + (t.Index1 - begin)));
                position += end - begin;
            }
            return new Media(MediaKind.CdRom, layout, extents, [.. files]);
        }
        catch
        {
            foreach (var f in files) f.Dispose();
            throw;
        }
    }

    private static long Frames(string msf)
    {
        var p = msf.Split(':');
        return (long.Parse(p[0]) * 60 + long.Parse(p[1])) * 75 + long.Parse(p[2]);
    }

    [GeneratedRegex(@"^\s*(\w+)\s+(.*)$")]
    private static partial Regex Command();

    [GeneratedRegex("""^(?:"([^"]+)"|(\S+))\s+(\w+)$""")]
    private static partial Regex FileName();
}
