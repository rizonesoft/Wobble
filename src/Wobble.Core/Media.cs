using Microsoft.Win32.SafeHandles;

namespace Wobble.Core;

public enum MediaKind { CdRom, DvdRom }

/// <summary>User data of a disc as 2048-byte blocks.</summary>
public interface IDiscImage : IDisposable
{
    long BlockCount { get; }
    void Read(long lba, Span<byte> destination);
}

public sealed class FileDiscImage : IDiscImage
{
    private readonly SafeFileHandle handle;
    private readonly long length;

    public FileDiscImage(string path)
    {
        handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        length = RandomAccess.GetLength(handle);
        BlockCount = (length + OpticalDrive.BlockSize - 1) / OpticalDrive.BlockSize;
    }

    public long BlockCount { get; }

    public void Read(long lba, Span<byte> destination)
    {
        long offset = lba * OpticalDrive.BlockSize;
        int done = 0;
        while (done < destination.Length)
        {
            int n = RandomAccess.Read(handle, destination[done..], offset + done);
            if (n == 0) break;
            done += n;
        }
        // An image whose size is not a multiple of the block size ends in a zero-padded block.
        destination[done..].Clear();
    }

    public void Dispose() => handle.Dispose();
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

/// <summary>A finalised single-session, single-track data disc.</summary>
public sealed class Media(IDiscImage image, MediaKind kind) : IDisposable
{
    /// <summary>Largest block count of an 80 minute CD (79:59:74 minus the 150 frame pregap).</summary>
    public const long MaxCdBlocks = 359_850;

    public IDiscImage Image { get; } = image;
    public MediaKind Kind { get; } = kind;
    public long BlockCount => Image.BlockCount;
    public ushort Profile => Kind == MediaKind.CdRom ? (ushort)0x0008 : (ushort)0x0010;

    public static Media FromImage(IDiscImage image, MediaKind? kind = null) =>
        new(image, kind ?? (image.BlockCount <= MaxCdBlocks ? MediaKind.CdRom : MediaKind.DvdRom));

    public void Dispose() => Image.Dispose();
}
