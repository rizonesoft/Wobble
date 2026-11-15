using static Wobble.Core.Bytes;

namespace Wobble.Core;

public sealed partial class OpticalDrive
{
    // ---- READ TOC/PMA/ATIP ------------------------------------------------------------------

    private ScsiResult ReadToc(ReadOnlySpan<byte> cdb)
    {
        bool msf = (cdb[1] & 0x02) != 0;
        int format = cdb[2] & 0x0F;
        if (format == 0) format = cdb[9] >> 6;       // older hosts put the format in the control byte
        long leadOut = Current!.BlockCount;
        byte[] data;

        switch (format)
        {
            case 0:                                  // formatted TOC: track 1 and the lead-out
                int from = cdb[6];
                if (from > 1 && from != 0xAA) return ScsiResult.Check(Sense.InvalidFieldInCdb);
                bool withTrack = from <= 1;
                data = new byte[4 + (withTrack ? 16 : 8)];
                data[2] = 1;                         // first track
                data[3] = 1;                         // last track
                int at = 4;
                if (withTrack) { PutTocEntry(data, at, 1, 0, msf); at += 8; }
                PutTocEntry(data, at, 0xAA, leadOut, msf);
                break;

            case 1:                                  // session information
                data = new byte[12];
                data[2] = 1;                         // first session
                data[3] = 1;                         // last session
                PutTocEntry(data, 4, 1, 0, msf);     // first track of the last session
                break;

            case 2:                                  // full TOC: always minute, second, frame
                data = new byte[4 + 4 * 11];
                data[2] = 1;
                data[3] = 1;
                var end = Msf(leadOut);
                PutFullTocEntry(data, 4, 0xA0, 1, 0, 0);            // first track number, disc type CD-ROM
                PutFullTocEntry(data, 15, 0xA1, 1, 0, 0);           // last track number
                PutFullTocEntry(data, 26, 0xA2, end.M, end.S, end.F);
                PutFullTocEntry(data, 37, 0x01, 0, 2, 0);           // track 1 starts at 00:02:00
                break;

            default:                                 // PMA, ATIP and CD-TEXT do not exist on this disc
                return ScsiResult.Check(Sense.InvalidFieldInCdb);
        }

        // The length field counts everything after itself.
        Put16(data, 0, data.Length - 2);
        return ScsiResult.Good(Fit(data, U16(cdb, 7)));
    }

    private static void PutTocEntry(Span<byte> data, int at, byte track, long lba, bool msf)
    {
        data[at + 1] = 0x14;                         // ADR 1, data track
        data[at + 2] = track;
        if (msf)
        {
            var (m, s, f) = Msf(lba);
            data[at + 5] = m; data[at + 6] = s; data[at + 7] = f;
        }
        else Put32(data, at + 4, lba);
    }

    private static void PutFullTocEntry(Span<byte> data, int at, byte point, byte pmin, byte psec, byte pframe)
    {
        data[at] = 1;                                // session
        data[at + 1] = 0x14;
        data[at + 3] = point;
        data[at + 8] = pmin; data[at + 9] = psec; data[at + 10] = pframe;
    }

    // ---- READ DISC INFORMATION, READ TRACK INFORMATION ---------------------------------------

    private ScsiResult ReadDiscInformation(ReadOnlySpan<byte> cdb)
    {
        if ((cdb[1] & 0x07) != 0) return ScsiResult.Check(Sense.InvalidFieldInCdb);
        var data = new byte[34];
        Put16(data, 0, 32);
        data[2] = 0x0E;                              // not erasable, last session complete, disc complete
        data[3] = 1;                                 // first track
        data[4] = 1;                                 // sessions
        data[5] = 1;                                 // first track in last session
        data[6] = 1;                                 // last track in last session
        data[7] = 0x20;                              // unrestricted use
        Put32(data, 16, 0xFFFFFFFF);                 // no lead-in to append at
        Put32(data, 20, 0xFFFFFFFF);                 // no lead-out to extend
        return ScsiResult.Good(Fit(data, U16(cdb, 7)));
    }

    private ScsiResult ReadTrackInformation(ReadOnlySpan<byte> cdb)
    {
        uint address = U32(cdb, 2);
        switch (cdb[1] & 0x03)
        {
            case 0: if (address >= Current!.BlockCount) return ScsiResult.Check(Sense.LbaOutOfRange); break;
            case 1 or 2: if (address != 1) return ScsiResult.Check(Sense.InvalidFieldInCdb); break;
            default: return ScsiResult.Check(Sense.InvalidFieldInCdb);
        }
        var data = new byte[36];
        Put16(data, 0, 34);
        data[2] = 1;                                 // track
        data[3] = 1;                                 // session
        data[5] = 0x04;                              // data track, recorded uninterrupted
        data[6] = 0x01;                              // Mode 1
        Put32(data, 24, Current!.BlockCount);        // track size
        return ScsiResult.Good(Fit(data, U16(cdb, 7)));
    }

    // ---- READ DISC STRUCTURE ----------------------------------------------------------------

    private ScsiResult ReadDiscStructure(ReadOnlySpan<byte> cdb)
    {
        if (Current!.Kind != MediaKind.DvdRom) return ScsiResult.Check(Sense.IncompatibleMedium);
        if ((cdb[1] & 0x0F) != 0 || cdb[6] != 0) return ScsiResult.Check(Sense.InvalidFieldInCdb);
        byte[] data;
        switch (cdb[7])
        {
            case 0x00:                               // physical format information
                data = new byte[4 + 2048];
                data[4] = 0x01;                      // DVD-ROM book, part version 1
                data[5] = 0x02;                      // 120 mm, 10.08 Mbps
                data[6] = 0x01;                      // one layer, embossed
                Put32(data, 8, 0x030000);            // first sector of the data area
                Put32(data, 12, 0x030000 + Current.BlockCount - 1);
                break;
            case 0x01:                               // copyright: no protection, all regions
                data = new byte[8];
                break;
            case 0x04:                               // manufacturing information: none recorded
                data = new byte[4 + 2048];
                break;
            default:
                return ScsiResult.Check(Sense.InvalidFieldInCdb);
        }
        Put16(data, 0, data.Length - 2);
        return ScsiResult.Good(Fit(data, U16(cdb, 8)));
    }

    // ---- GET CONFIGURATION ------------------------------------------------------------------

    private ScsiResult GetConfiguration(ReadOnlySpan<byte> cdb)
    {
        int requestType = cdb[1] & 0x03;
        if (requestType == 3) return ScsiResult.Check(Sense.InvalidFieldInCdb);
        int start = U16(cdb, 2);

        using var body = new MemoryStream();
        foreach (var (code, bytes) in Features())
        {
            bool current = (bytes[2] & 0x01) != 0;
            bool wanted = requestType switch
            {
                0 => code >= start,
                1 => code >= start && current,
                _ => code == start,
            };
            if (wanted) body.Write(bytes);
        }

        var data = new byte[8 + body.Length];
        Put32(data, 0, data.Length - 4);
        Put16(data, 6, Current?.Profile ?? 0);
        body.ToArray().CopyTo(data, 8);
        return ScsiResult.Good(Fit(data, U16(cdb, 7)));
    }

    private IEnumerable<(int Code, byte[] Bytes)> Features()
    {
        bool cd = Current?.Kind == MediaKind.CdRom, dvd = Current?.Kind == MediaKind.DvdRom;

        // Profile list: every profile the drive supports, highest first.
        yield return Feature(0x0000, 0, persistent: true, current: true,
            [0x00, 0x10, (byte)(dvd ? 1 : 0), 0, 0x00, 0x08, (byte)(cd ? 1 : 0), 0]);
        // Core: SCSI physical interface, device busy events.
        yield return Feature(0x0001, 2, true, true, [0, 0, 0, 0x01, 0x01, 0, 0, 0]);
        // Morphing: operational change events, polled.
        yield return Feature(0x0002, 1, true, true, [0x02, 0, 0, 0]);
        // Removable medium: tray loader, eject, lock.
        yield return Feature(0x0003, 0, true, true, [0x29, 0, 0, 0]);
        // Random readable: 2048 byte blocks.
        yield return Feature(0x0010, 0, false, cd || dvd, [0, 0, 0x08, 0, 0, (byte)(dvd ? 0x10 : 0x01), 0, 0]);
        // MultiRead and CD read: current with a CD.
        yield return Feature(0x001D, 0, false, cd, []);
        yield return Feature(0x001E, 2, false, cd, [0, 0, 0, 0]);
        // DVD read: current with a DVD.
        yield return Feature(0x001F, 1, false, dvd, [0, 0, 0, 0]);
        // Power management and timeout.
        yield return Feature(0x0100, 0, true, true, []);
        yield return Feature(0x0105, 1, true, true, [0, 0, 0, 0]);
        // Drive serial number, space-padded to a multiple of four bytes.
        var serial = new byte[(id.Serial.Length + 3) / 4 * 4];
        PutAscii(serial, 0, serial.Length, id.Serial);
        yield return Feature(0x0108, 0, true, true, serial);
    }

    private static (int, byte[]) Feature(int code, int version, bool persistent, bool current, byte[] payload)
    {
        var bytes = new byte[4 + payload.Length];
        Put16(bytes, 0, code);
        bytes[2] = (byte)(version << 2 | (persistent ? 0x02 : 0) | (current ? 0x01 : 0));
        bytes[3] = (byte)payload.Length;
        payload.CopyTo(bytes, 4);
        return (code, bytes);
    }

    // ---- GET EVENT STATUS NOTIFICATION ------------------------------------------------------

    private const byte SupportedEventClasses = 0x16;     // operational change, power management, media

    private ScsiResult GetEventStatusNotification(ReadOnlySpan<byte> cdb)
    {
        if ((cdb[1] & 0x01) == 0) return ScsiResult.Check(Sense.InvalidFieldInCdb);   // only polling is supported
        int requested = cdb[4] & SupportedEventClasses;
        int allocation = U16(cdb, 7);

        if (requested == 0)
            return ScsiResult.Good(Fit([0, 0, 0x80, SupportedEventClasses], allocation));

        // Report the media class when it has news, otherwise the lowest requested class with nothing to say.
        int eventClass = (requested & 0x10) != 0 && mediaEvent != 0 ? 4
            : (requested & 0x02) != 0 ? 1
            : (requested & 0x04) != 0 ? 2 : 4;

        var data = new byte[8];
        Put16(data, 0, 4);
        data[2] = (byte)eventClass;
        data[3] = SupportedEventClasses;
        switch (eventClass)
        {
            case 2:
                data[5] = 0x01;                          // power status: active
                break;
            case 4:
                data[4] = mediaEvent;
                data[5] = (byte)((trayOpen ? 0x01 : 0) | (Current is not null ? 0x02 : 0));
                mediaEvent = 0;
                break;
        }
        return ScsiResult.Good(Fit(data, allocation));
    }

    // ---- GET PERFORMANCE --------------------------------------------------------------------

    private ScsiResult GetPerformance(ReadOnlySpan<byte> cdb)
    {
        int maxDescriptors = U16(cdb, 8);
        byte[] data;
        switch (cdb[10])
        {
            case 0x00:                                   // nominal read performance
                bool one = maxDescriptors > 0 && Current is not null;
                data = new byte[8 + (one ? 16 : 0)];
                if (one)
                {
                    Put32(data, 8, 0);
                    Put32(data, 12, CurrentReadSpeedKbps);
                    Put32(data, 16, Current!.BlockCount - 1);
                    Put32(data, 20, CurrentReadSpeedKbps);
                }
                break;
            case 0x03:                                   // write speed descriptors: a read-only drive has none
                data = new byte[8];
                break;
            default:
                return ScsiResult.Check(Sense.InvalidFieldInCdb);
        }
        Put32(data, 0, data.Length - 4);
        return ScsiResult.Good(data);
    }

    // ---- MODE SENSE, MODE SELECT ------------------------------------------------------------

    private ScsiResult ModeSense(ReadOnlySpan<byte> cdb, bool six)
    {
        int pageCode = cdb[2] & 0x3F, pageControl = cdb[2] >> 6;
        if (pageControl == 3) return ScsiResult.Check(Sense.SavingNotSupported);

        using var pages = new MemoryStream();
        foreach (var page in ModePages())
        {
            if (pageCode != 0x3F && page[0] != pageCode) continue;
            // No field is changeable, so the changeable-values mask is all zeros after the page header.
            if (pageControl == 1) Array.Clear(page, 2, page.Length - 2);
            pages.Write(page);
        }
        if (pages.Length == 0) return ScsiResult.Check(Sense.InvalidFieldInCdb);

        int header = six ? 4 : 8;
        var data = new byte[header + pages.Length];
        if (six) data[0] = (byte)(data.Length - 1);
        else Put16(data, 0, data.Length - 2);
        pages.ToArray().CopyTo(data, header);
        return ScsiResult.Good(Fit(data, six ? cdb[4] : U16(cdb, 7)));
    }

    private IEnumerable<byte[]> ModePages()
    {
        // Read/write error recovery: three read retries.
        yield return [0x01, 0x0A, 0x00, 0x03, 0, 0, 0, 0, 0, 0, 0, 0];
        // Power condition: no timers.
        yield return [0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        // Timeout and protect.
        yield return [0x1D, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        // MM capabilities and mechanical status.
        var caps = new byte[32];
        caps[0] = 0x2A;
        caps[1] = 30;
        caps[2] = 0x0B;                                  // reads CD-R, CD-RW and DVD-ROM
        caps[4] = 0x40;                                  // multisession
        caps[6] = 0x29;                                  // tray loader, eject, lock
        Put16(caps, 8, MaxReadSpeedKbps);
        Put16(caps, 12, 2048);                           // buffer size in kB
        Put16(caps, 14, CurrentReadSpeedKbps);
        yield return caps;
    }

    private static ScsiResult ModeSelect(int parameterListLength, ReadOnlySpan<byte> dataOut)
    {
        // No mode page has a changeable field (see ModeSense), so a valid parameter list changes nothing.
        return dataOut.Length < parameterListLength
            ? ScsiResult.Check(Sense.InvalidFieldInCdb)
            : ScsiResult.Good();
    }
}
