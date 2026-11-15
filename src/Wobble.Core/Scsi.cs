namespace Wobble.Core;

public static class ScsiStatus
{
    public const byte Good = 0x00;
    public const byte CheckCondition = 0x02;
}

/// <summary>Sense key, additional sense code and qualifier.</summary>
public readonly record struct Sense(byte Key, byte Asc, byte Ascq)
{
    public static readonly Sense None = new(0x00, 0x00, 0x00);
    public static readonly Sense NoMediumTrayClosed = new(0x02, 0x3A, 0x01);
    public static readonly Sense NoMediumTrayOpen = new(0x02, 0x3A, 0x02);
    public static readonly Sense InvalidOpcode = new(0x05, 0x20, 0x00);
    public static readonly Sense LbaOutOfRange = new(0x05, 0x21, 0x00);
    public static readonly Sense InvalidFieldInCdb = new(0x05, 0x24, 0x00);
    public static readonly Sense LunNotSupported = new(0x05, 0x25, 0x00);
    public static readonly Sense IncompatibleMedium = new(0x05, 0x30, 0x02);
    public static readonly Sense SavingNotSupported = new(0x05, 0x39, 0x00);
    public static readonly Sense RemovalPrevented = new(0x05, 0x53, 0x02);
    public static readonly Sense MediumMayHaveChanged = new(0x06, 0x28, 0x00);
    public static readonly Sense PowerOnReset = new(0x06, 0x29, 0x00);

    /// <summary>Fixed format sense data, 18 bytes.</summary>
    public byte[] ToFixedFormat()
    {
        var b = new byte[18];
        b[0] = 0x70;
        b[2] = Key;
        b[7] = 10;
        b[12] = Asc;
        b[13] = Ascq;
        return b;
    }

    public override string ToString() => $"{Key:X2}/{Asc:X2}/{Ascq:X2}";
}

public sealed record ScsiResult(byte Status, byte[] DataIn, Sense? Sense)
{
    public static ScsiResult Good(byte[]? dataIn = null) => new(ScsiStatus.Good, dataIn ?? [], null);
    public static ScsiResult Check(Sense sense) => new(ScsiStatus.CheckCondition, [], sense);
}

/// <summary>The seam between the MMC core and any transport (iSCSI, a miniport bridge, a USB gadget).</summary>
public interface IScsiDevice
{
    /// <param name="cdb">The command descriptor block. Allocation lengths are read from it.</param>
    /// <param name="dataOut">Data sent with the command, empty for reads.</param>
    ScsiResult Execute(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> dataOut);

    /// <summary>A logical unit or target reset from the transport.</summary>
    void Reset();
}

public sealed record TraceEntry(long Seq, double TimeMs, string Cdb, int DataOutLength, int DataInLength, byte Status, string? Sense);

public sealed record DriveIdentity(string Vendor, string Product, string Revision, string Serial)
{
    public static readonly DriveIdentity Default = new("WOBBLE", "VIRTUAL DVD-ROM", "0.1", "WOBBLE0001");
}
