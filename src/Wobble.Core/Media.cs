using Microsoft.Win32.SafeHandles;

namespace Wobble.Core;

public enum MediaKind { CdRom, DvdRom }

public enum TrackMode { Audio, Mode1, Mode2 }

/// <summary>
/// A track. <see cref="Start"/> is the address of index 1; the <see cref="Pregap"/> blocks before it
/// (index 0) also belong to the track.
/// </summary>
public sealed record Track(int Number, TrackMode Mode, long Start, long Length, long Pregap = 0)
{
    /// <summary>Subchannel control nibble: data track or two-channel audio.</summary>
    public byte Control => Mode == TrackMode.Audio ? (byte)0x00 : (byte)0x04;
    public long End => Start + Length;
}

/// <summary>User data of a disc as 2048-byte blocks.</summary>
public interface IDiscImage : IDisposable
{
    long BlockCount { get; }
    void Read(long lba, Span<byte> destination);
}

/// <summary>A file read at byte offsets; reads past the end give zeros.</summary>
public sealed class ImageFile(string path) : IDisposable
{
    private readonly SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public long Length => RandomAccess.GetLength(handle);

    public void Read(long offset, Span<byte> destination)
    {
        int done = 0;
        while (done < destination.Length)
        {
            int n = RandomAccess.Read(handle, destination[done..], offset + done);
            if (n == 0) break;
            done += n;
        }
        destination[done..].Clear();
    }

    public void Dispose() => handle.Dispose();
}

public sealed class FileDiscImage(string path) : IDiscImage
{
    private readonly ImageFile file = new(path);

    // An image whose size is not a multiple of the block size ends in a zero-padded block.
    public long BlockCount => (file.Length + OpticalDrive.BlockSize - 1) / OpticalDrive.BlockSize;

    public void Read(long lba, Span<byte> destination) => file.Read(lba * OpticalDrive.BlockSize, destination);

    public void Dispose() => file.Dispose();
}

public sealed class MemoryDiscImage(byte[] data) : IDiscImage
{
    public long BlockCount => (data.Length + OpticalDrive.BlockSize - 1) / OpticalDrive.BlockSize;

    public void Read(long lba, Span<byte> destination)
    {
        destination.Clear();
        long offset = lba * OpticalDrive.BlockSize;
        int n = (int)Math.Min(destination.Length, data.Length - offset);
        data.AsSpan((int)offset, n).CopyTo(destination);
    }

    public void Dispose() { }
}

/// <summary>Reads whole sectors, by index from the start of a stretch of the disc.</summary>
public delegate void SectorReader(long index, Span<byte> destination);

/// <summary>
/// A stretch of the disc stored one way: raw 2352-byte sectors, cooked 2048-byte sectors, or a gap that is
/// not stored at all (<see cref="Read"/> is null) and reads as empty sectors.
/// </summary>
public sealed record Extent(long Start, long Count, TrackMode Mode, int SectorSize, SectorReader? Read);

/// <summary>A finalised disc: tracks, and sectors that can be read cooked or raw.</summary>
public sealed class Media : IDisposable
{
    /// <summary>Largest block count of an 80 minute CD (79:59:74 minus the 150 frame pregap).</summary>
    public const long MaxCdBlocks = 359_850;

    private readonly Extent[] extents;
    private readonly IDisposable[] owned;

    public Media(MediaKind kind, IReadOnlyList<Track> tracks, IEnumerable<Extent> extents, params IDisposable[] owned)
    {
        Kind = kind;
        Tracks = tracks;
        this.extents = [.. extents];
        this.owned = owned;
        BlockCount = this.extents.Length == 0 ? 0 : this.extents[^1].Start + this.extents[^1].Count;
    }

    /// <summary>A disc with one Mode 1 data track holding a cooked image.</summary>
    public Media(IDiscImage image, MediaKind kind) : this(
        kind,
        [new Track(1, TrackMode.Mode1, 0, image.BlockCount)],
        [new Extent(0, image.BlockCount, TrackMode.Mode1, OpticalDrive.BlockSize, image.Read)],
        image)
    {
    }

    public MediaKind Kind { get; }
    public IReadOnlyList<Track> Tracks { get; }

    /// <summary>Number of addressable blocks, which is also the address of the lead-out.</summary>
    public long BlockCount { get; }

    public ushort Profile => Kind == MediaKind.CdRom ? (ushort)0x0008 : (ushort)0x0010;

    public static Media FromImage(IDiscImage image, MediaKind? kind = null) =>
        new(image, kind ?? (image.BlockCount <= MaxCdBlocks ? MediaKind.CdRom : MediaKind.DvdRom));

    /// <summary>Opens a disc image: a cue sheet (.cue) or a cooked image such as an ISO.</summary>
    public static Media Open(string path, MediaKind? kind = null) =>
        Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase)
            ? CueSheet.Load(path)
            : FromImage(new FileDiscImage(path), kind);

    public Track TrackAt(long lba) => Tracks.Last(t => lba >= t.Start - t.Pregap);

    private Extent ExtentAt(long lba) => extents.Last(e => lba >= e.Start);

    /// <summary>Reads the complete 2352-byte sector.</summary>
    public void ReadRaw(long lba, Span<byte> raw)
    {
        var extent = ExtentAt(lba);
        if (extent.Read is not null && extent.SectorSize == SectorCodec.RawSize)
        {
            extent.Read(lba - extent.Start, raw);
            return;
        }

        raw.Clear();
        switch (extent.Mode)
        {
            case TrackMode.Mode1:
                Span<byte> user = stackalloc byte[OpticalDrive.BlockSize];
                extent.Read?.Invoke(lba - extent.Start, user);
                SectorCodec.EncodeMode1(lba, user, raw);
                break;
            case TrackMode.Mode2:
                SectorCodec.WriteHeader(raw, lba, 2);        // an empty formless sector
                break;
        }                                                    // an audio gap is silence
    }

    /// <summary>Reads the 2048 bytes of user data of a data sector. Returns false for an audio sector.</summary>
    public bool TryReadUserData(long lba, Span<byte> user)
    {
        var extent = ExtentAt(lba);
        if (extent.Mode == TrackMode.Audio) return false;
        if (extent.Read is null)
        {
            user.Clear();
        }
        else if (extent.SectorSize == OpticalDrive.BlockSize)
        {
            extent.Read(lba - extent.Start, user);
        }
        else
        {
            Span<byte> raw = stackalloc byte[SectorCodec.RawSize];
            extent.Read(lba - extent.Start, raw);
            // Mode 2 user data follows the 8-byte subheader.
            raw.Slice(extent.Mode == TrackMode.Mode1 ? 16 : 24, OpticalDrive.BlockSize).CopyTo(user);
        }
        return true;
    }

    public void Dispose()
    {
        foreach (var item in owned) item.Dispose();
    }
}
