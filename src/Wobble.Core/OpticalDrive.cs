using static Wobble.Core.Bytes;

namespace Wobble.Core;

/// <summary>
/// An emulated optical drive: the MMC command set over a drive and media state model.
/// It knows nothing about the transport that delivers commands.
/// </summary>
public sealed partial class OpticalDrive(DriveIdentity? identity = null, TimeProvider? clock = null) : IScsiDevice, IDisposable
{
    public const int BlockSize = 2048;

    /// <summary>The most blocks one read command may ask for (64 MiB), so a CDB cannot demand an unbounded buffer.</summary>
    public const int MaxTransferBlocks = 32768;

    private readonly Lock gate = new();
    private readonly DriveIdentity id = identity ?? DriveIdentity.Default;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly List<TraceEntry> trace = [];
    private readonly long started = (clock ?? TimeProvider.System).GetTimestamp();

    private Media? media;
    private bool trayOpen;
    private bool preventRemoval;
    private Sense? unitAttention = Sense.PowerOnReset;
    private byte mediaEvent;            // 0 none, 2 new media, 3 media removal
    private ushort readSpeedKbps;       // 0 means the maximum for the loaded media
    private long seq;

    /// <summary>The disc the drive can read right now: none while the tray is open.</summary>
    private Media? Current => trayOpen ? null : media;

    public Media? LoadedMedia { get { lock (gate) return Current; } }

    /// <summary>Puts a disc in the drive and closes the tray, replacing any disc already there.</summary>
    public void InsertMedia(Media disc)
    {
        lock (gate)
        {
            media?.Dispose();
            media = disc;
            trayOpen = false;
            mediaEvent = 2;
            unitAttention = Sense.MediumMayHaveChanged;
        }
    }

    /// <summary>Takes the disc out, as if the user removed it, even when removal is prevented.</summary>
    public void RemoveMedia()
    {
        lock (gate)
        {
            if (media is null) return;
            media.Dispose();
            media = null;
            mediaEvent = 3;
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            preventRemoval = false;
            unitAttention = Sense.PowerOnReset;
        }
    }

    public IReadOnlyList<TraceEntry> Trace { get { lock (gate) return trace.ToArray(); } }

    public void ClearTrace() { lock (gate) trace.Clear(); }

    public void Dispose() { lock (gate) { media?.Dispose(); media = null; } }

    public ScsiResult Execute(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> dataOut)
    {
        lock (gate)
        {
            int length = cdb.Length == 0 ? 0 : CdbLength(cdb[0]);
            var result = cdb.Length == 0 || cdb.Length < length
                ? ScsiResult.Check(Sense.InvalidFieldInCdb)
                : Dispatch(cdb[..length], dataOut);
            trace.Add(new TraceEntry(++seq, time.GetElapsedTime(started).TotalMilliseconds,
                Convert.ToHexString(cdb[..Math.Min(length, cdb.Length)]), dataOut.Length, result.DataIn.Length,
                result.Status, result.Sense?.ToString()));
            return result;
        }
    }

    private static int CdbLength(byte opcode) => (opcode >> 5) switch { 0 => 6, 1 or 2 => 10, 4 => 16, 5 => 12, _ => 10 };

    private ScsiResult Dispatch(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> dataOut)
    {
        byte op = cdb[0];

        // These commands answer even while a unit attention is pending, and leave it pending.
        bool reportsUnitAttention = op is not (0x12 or 0x03 or 0xA0 or 0x46 or 0x4A);
        if (reportsUnitAttention && unitAttention is { } pending)
        {
            unitAttention = null;
            return ScsiResult.Check(pending);
        }

        bool needsMedia = op is 0x25 or 0x28 or 0xA8 or 0x2B or 0x2F or 0x43 or 0x51 or 0x52 or 0xAD or 0xB9 or 0xBE;
        if (needsMedia && Current is null)
            return ScsiResult.Check(trayOpen ? Sense.NoMediumTrayOpen : Sense.NoMediumTrayClosed);

        return op switch
        {
            0x00 => Current is null
                ? ScsiResult.Check(trayOpen ? Sense.NoMediumTrayOpen : Sense.NoMediumTrayClosed)
                : ScsiResult.Good(),
            0x03 => RequestSense(cdb),
            0x12 => Inquiry(cdb),
            0x15 => ModeSelect(cdb[4], dataOut),
            0x1A => ModeSense(cdb, six: true),
            0x1B => StartStopUnit(cdb),
            0x1E => PreventAllow(cdb),
            0x25 => ReadCapacity(),
            0x28 => Read(U32(cdb, 2), U16(cdb, 7)),
            0x2B => U32(cdb, 2) < Current!.BlockCount ? ScsiResult.Good() : ScsiResult.Check(Sense.LbaOutOfRange),
            0x2F => InRange(U32(cdb, 2), U16(cdb, 7)) ? ScsiResult.Good() : ScsiResult.Check(Sense.LbaOutOfRange),
            0x35 => ScsiResult.Good(),
            0x43 => ReadToc(cdb),
            0x46 => GetConfiguration(cdb),
            0x4A => GetEventStatusNotification(cdb),
            0x51 => ReadDiscInformation(cdb),
            0x52 => ReadTrackInformation(cdb),
            0x55 => ModeSelect(U16(cdb, 7), dataOut),
            0x5A => ModeSense(cdb, six: false),
            0xA0 => ReportLuns(cdb),
            0xA8 => Read(U32(cdb, 2), U32(cdb, 6)),
            0xAC => GetPerformance(cdb),
            0xAD => ReadDiscStructure(cdb),
            0xB9 => ReadCdMsf(cdb),
            0xBB => SetCdSpeed(cdb),
            0xBD => MechanismStatus(cdb),
            0xBE => ReadCd(cdb),
            _ => ScsiResult.Check(Sense.InvalidOpcode),
        };
    }

    private ScsiResult RequestSense(ReadOnlySpan<byte> cdb)
    {
        // Sense is delivered with each failed command, so only a pending unit attention is left to report.
        var sense = unitAttention ?? Sense.None;
        unitAttention = null;
        return ScsiResult.Good(Fit(sense.ToFixedFormat(), cdb[4]));
    }

    private ScsiResult Inquiry(ReadOnlySpan<byte> cdb)
    {
        bool vitalPage = (cdb[1] & 0x01) != 0;
        int allocation = U16(cdb, 3);
        if (!vitalPage)
        {
            if (cdb[2] != 0) return ScsiResult.Check(Sense.InvalidFieldInCdb);
            var std = new byte[36];
            std[0] = 0x05;              // CD/DVD device
            std[1] = 0x80;              // removable medium
            std[2] = 0x05;              // SPC-3
            std[3] = 0x02;              // response data format
            std[4] = 31;                // additional length
            PutAscii(std, 8, 8, id.Vendor);
            PutAscii(std, 16, 16, id.Product);
            PutAscii(std, 32, 4, id.Revision);
            return ScsiResult.Good(Fit(std, allocation));
        }

        byte[] page;
        switch (cdb[2])
        {
            case 0x00:
                page = [0x05, 0x00, 0x00, 3, 0x00, 0x80, 0x83];
                break;
            case 0x80:
                page = new byte[4 + id.Serial.Length];
                page[0] = 0x05; page[1] = 0x80; page[3] = (byte)id.Serial.Length;
                PutAscii(page, 4, id.Serial.Length, id.Serial);
                break;
            case 0x83:
                int idLength = 8 + 16 + id.Serial.Length;
                page = new byte[4 + 4 + idLength];
                page[0] = 0x05; page[1] = 0x83; page[3] = (byte)(4 + idLength);
                page[4] = 0x02;         // ASCII
                page[5] = 0x01;         // T10 vendor identification, logical unit
                page[7] = (byte)idLength;
                PutAscii(page, 8, 8, id.Vendor);
                PutAscii(page, 16, 16, id.Product);
                PutAscii(page, 32, id.Serial.Length, id.Serial);
                break;
            default:
                return ScsiResult.Check(Sense.InvalidFieldInCdb);
        }
        return ScsiResult.Good(Fit(page, allocation));
    }

    private ScsiResult StartStopUnit(ReadOnlySpan<byte> cdb)
    {
        bool start = (cdb[4] & 0x01) != 0, loadEject = (cdb[4] & 0x02) != 0;
        if (!loadEject) return ScsiResult.Good();
        if (!start)
        {
            if (preventRemoval) return ScsiResult.Check(Sense.RemovalPrevented);
            if (!trayOpen && media is not null) mediaEvent = 3;
            trayOpen = true;
        }
        else if (trayOpen)
        {
            trayOpen = false;
            if (media is not null)
            {
                mediaEvent = 2;
                unitAttention = Sense.MediumMayHaveChanged;
            }
        }
        return ScsiResult.Good();
    }

    private ScsiResult PreventAllow(ReadOnlySpan<byte> cdb)
    {
        preventRemoval = (cdb[4] & 0x01) != 0;
        return ScsiResult.Good();
    }

    private ScsiResult ReadCapacity()
    {
        var data = new byte[8];
        Put32(data, 0, Current!.BlockCount - 1);
        Put32(data, 4, BlockSize);
        return ScsiResult.Good(data);
    }

    private bool InRange(long lba, long count) => lba + count <= Current!.BlockCount;

    private ScsiResult Read(long lba, long count)
    {
        if (!InRange(lba, count)) return ScsiResult.Check(Sense.LbaOutOfRange);
        if (count > MaxTransferBlocks) return ScsiResult.Check(Sense.InvalidFieldInCdb);
        var data = new byte[count * BlockSize];
        for (int i = 0; i < count; i++)
        {
            // READ returns user data, which an audio sector does not have.
            if (!Current!.TryReadUserData(lba + i, data.AsSpan(i * BlockSize, BlockSize)))
                return ScsiResult.Check(Sense.IllegalModeForThisTrack);
        }
        return ScsiResult.Good(data);
    }

    private ScsiResult ReadCd(ReadOnlySpan<byte> cdb) =>
        ReadCdSectors(U32(cdb, 2), U24(cdb, 6), (cdb[1] >> 2) & 0x07, cdb[9], cdb[10]);

    private ScsiResult ReadCdMsf(ReadOnlySpan<byte> cdb)
    {
        long start = (cdb[3] * 60 + cdb[4]) * 75 + cdb[5] - 150;
        long end = (cdb[6] * 60 + cdb[7]) * 75 + cdb[8] - 150;        // exclusive
        if (end < start) return ScsiResult.Check(Sense.InvalidFieldInCdb);
        return ReadCdSectors(start, end - start, (cdb[1] >> 2) & 0x07, cdb[9], cdb[10]);
    }

    /// <summary>
    /// READ CD and READ CD MSF: any selection of the fields of a raw sector, optionally followed by
    /// subchannel data, for each sector in turn.
    /// </summary>
    private ScsiResult ReadCdSectors(long lba, long count, int expectedType, byte fields, byte subchannelSelection)
    {
        var disc = Current!;
        if (disc.Kind != MediaKind.CdRom) return ScsiResult.Check(Sense.IncompatibleMedium);
        if (lba < 0 || lba + count > disc.BlockCount) return ScsiResult.Check(Sense.LbaOutOfRange);
        // C2 error pointers and corrected R to W subchannel are not modelled.
        int subchannel = subchannelSelection & 0x07;
        if (count > MaxTransferBlocks || (fields & 0x06) != 0 || subchannel > 2 || expectedType > 5)
            return ScsiResult.Check(Sense.InvalidFieldInCdb);

        using var output = new MemoryStream();
        Span<byte> raw = stackalloc byte[SectorCodec.RawSize];
        Span<byte> q = stackalloc byte[12];
        Span<byte> sub = stackalloc byte[96];

        for (long sector = lba; sector < lba + count; sector++)
        {
            var track = disc.TrackAt(sector);
            disc.ReadRaw(sector, raw);

            // Sector type as the CDB numbers it: 1 CD-DA, 2 Mode 1, 4 Mode 2 Form 1, 5 Mode 2 Form 2.
            int type = track.Mode switch
            {
                TrackMode.Audio => 1,
                TrackMode.Mode1 => 2,
                _ => (raw[18] & 0x20) != 0 ? 5 : 4,
            };
            if (expectedType != 0 && expectedType != type) return ScsiResult.Check(Sense.IllegalModeForThisTrack);

            if (type == 1)
            {
                if ((fields & 0x10) != 0) output.Write(raw);                 // an audio sector is all user data
            }
            else
            {
                var (user, parity) = type switch
                {
                    2 => (16..2064, 2064..2352),
                    4 => (24..2072, 2072..2352),
                    _ => (24..2348, 2348..2352),
                };
                if ((fields & 0x80) != 0) output.Write(raw[..12]);            // sync
                if ((fields & 0x20) != 0) output.Write(raw[12..16]);          // header
                if ((fields & 0x40) != 0 && type >= 4) output.Write(raw[16..24]);   // subheader
                if ((fields & 0x10) != 0) output.Write(raw[user]);
                if ((fields & 0x08) != 0) output.Write(raw[parity]);          // EDC and ECC
            }

            if (subchannel == 0) continue;
            bool pause = sector < track.Start;
            SectorCodec.WriteQ(q, track, sector);
            if (subchannel == 2)
            {
                // Formatted Q: the 12 bytes of Q, three unused bytes, then the P flag.
                sub[..16].Clear();
                q.CopyTo(sub);
                sub[15] = (byte)(pause ? 0x80 : 0);
                output.Write(sub[..16]);
            }
            else
            {
                // Raw P to W: one byte per channel bit position, P in bit 7 and Q in bit 6.
                for (int i = 0; i < 96; i++)
                    sub[i] = (byte)((pause ? 0x80 : 0) | ((q[i / 8] >> (7 - i % 8) & 1) != 0 ? 0x40 : 0));
                output.Write(sub);
            }
        }
        return ScsiResult.Good(output.ToArray());
    }

    private ScsiResult SetCdSpeed(ReadOnlySpan<byte> cdb)
    {
        ushort speed = U16(cdb, 2);
        readSpeedKbps = speed == 0xFFFF ? (ushort)0 : speed;
        return ScsiResult.Good();
    }

    private ScsiResult MechanismStatus(ReadOnlySpan<byte> cdb)
    {
        var data = new byte[8];
        data[1] = (byte)(trayOpen ? 0x10 : 0x00);   // door open
        return ScsiResult.Good(Fit(data, U16(cdb, 8)));
    }

    private static ScsiResult ReportLuns(ReadOnlySpan<byte> cdb)
    {
        var data = new byte[16];
        Put32(data, 0, 8);                           // one logical unit, LUN 0
        return ScsiResult.Good(Fit(data, U32(cdb, 6)));
    }

    /// <summary>Maximum read speed in kB/s for the loaded media: 48x CD or 16x DVD.</summary>
    private ushort MaxReadSpeedKbps => Current?.Kind == MediaKind.CdRom ? (ushort)8467 : (ushort)22160;

    private ushort CurrentReadSpeedKbps => readSpeedKbps == 0 ? MaxReadSpeedKbps : Math.Min(readSpeedKbps, MaxReadSpeedKbps);
}
