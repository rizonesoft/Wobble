using System.Net;
using System.Net.Sockets;
using Wobble.Core;
using static Wobble.Core.Bytes;
using Pair = System.Collections.Generic.KeyValuePair<string, string>;

namespace Wobble.Iscsi;

/// <summary>One iSCSI connection, which is also the whole session: login, then commands one at a time.</summary>
internal sealed class IscsiConnection(TcpClient client, IScsiDevice device, string targetName)
{
    private const int MaxReceiveSegment = 262144;
    private const uint MaxExpectedTransfer = 64 * 1024 * 1024;
    private const uint NoTag = 0xFFFFFFFF;

    private readonly NetworkStream stream = client.GetStream();
    private readonly Queue<Pdu> deferred = new();      // PDUs that arrived while a write was collecting its data
    private uint statSn = 1;
    private uint expCmdSn;
    private uint lastTransferTag;
    private int initiatorMaxReceive = 8192;
    private int maxBurst = MaxReceiveSegment;
    private bool discovery;

    public async Task RunAsync(CancellationToken cancel)
    {
        if (!await LoginAsync(cancel)) return;
        while (true)
        {
            var pdu = deferred.Count > 0 ? deferred.Dequeue() : await Pdu.ReadAsync(stream, cancel);
            if (pdu is null || !await HandleAsync(pdu, cancel)) return;
        }
    }

    /// <summary>Writes StatSN, ExpCmdSN and MaxCmdSN into a response.</summary>
    private void Stamp(Pdu response, bool carriesStatus = true)
    {
        Put32(response.Header, 24, statSn);
        if (carriesStatus) statSn++;
        Put32(response.Header, 28, expCmdSn);
        Put32(response.Header, 32, expCmdSn + 31);     // a window of 32 commands
    }

    // ---- Login ------------------------------------------------------------------------------

    private async Task<bool> LoginAsync(CancellationToken cancel)
    {
        string? requestedTarget = null;
        bool namesChecked = false, declaredReceiveLength = false;

        while (true)
        {
            var pdu = await Pdu.ReadAsync(stream, cancel);
            if (pdu is null || pdu.Opcode != 0x03) return false;
            var h = pdu.Header;
            bool transit = (h[1] & 0x80) != 0;
            int stage = (h[1] >> 2) & 0x03, next = h[1] & 0x03;
            expCmdSn = U32(h, 24);

            var reply = new List<Pair>();
            (byte Class, byte Detail) status = (0, 0);
            foreach (var (key, value) in Pdu.ParseText(pdu.Data))
            {
                switch (key)
                {
                    case "InitiatorName" or "InitiatorAlias":
                        break;
                    case "SessionType":
                        discovery = value == "Discovery";
                        break;
                    case "TargetName":
                        requestedTarget = value;
                        break;
                    case "AuthMethod":
                        if (Offers(value, "None")) reply.Add(new(key, "None")); else status = (2, 1);
                        break;
                    case "HeaderDigest" or "DataDigest":
                        if (Offers(value, "None")) reply.Add(new(key, "None")); else status = (2, 0);
                        break;
                    case "MaxRecvDataSegmentLength":
                        if (!int.TryParse(value, out initiatorMaxReceive) || initiatorMaxReceive < 512) status = (2, 0);
                        break;
                    case "MaxBurstLength":
                        if (int.TryParse(value, out int burst) && burst >= 512)
                        {
                            maxBurst = Math.Min(burst, MaxReceiveSegment);
                            reply.Add(new(key, maxBurst.ToString()));
                        }
                        else status = (2, 0);
                        break;
                    case "FirstBurstLength":
                        if (int.TryParse(value, out int first)) reply.Add(new(key, Math.Min(first, 65536).ToString()));
                        else status = (2, 0);
                        break;
                    case "ImmediateData" or "DefaultTime2Wait":
                        reply.Add(new(key, value));
                        break;
                    case "InitialR2T" or "DataPDUInOrder" or "DataSequenceInOrder":
                        reply.Add(new(key, "Yes"));
                        break;
                    case "MaxConnections" or "MaxOutstandingR2T":
                        reply.Add(new(key, "1"));
                        break;
                    case "ErrorRecoveryLevel" or "DefaultTime2Retain":
                        reply.Add(new(key, "0"));
                        break;
                    default:
                        reply.Add(new(key, "NotUnderstood"));
                        break;
                }
            }

            if (status == (0, 0) && !namesChecked)
            {
                if (!discovery && requestedTarget is null) status = (2, 7);              // missing parameter
                else if (!discovery && requestedTarget != targetName) status = (2, 3);   // target not found
                else
                {
                    reply.Add(new("TargetPortalGroupTag", "1"));
                    namesChecked = true;
                }
            }

            bool ok = status == (0, 0);
            if (ok && stage == 1 && !declaredReceiveLength)
            {
                reply.Add(new("MaxRecvDataSegmentLength", MaxReceiveSegment.ToString()));
                declaredReceiveLength = true;
            }

            bool final = ok && transit && next == 3;
            var response = Pdu.Create(0x23, ok ? Pdu.FormatText(reply) : []);
            var r = response.Header;
            r[1] = (byte)(stage << 2 | (ok && transit ? 0x80 | next : 0));
            h.AsSpan(8, 6).CopyTo(r.AsSpan(8));                                          // ISID
            if (final) Put16(r, 14, U16(h, 14) != 0 ? U16(h, 14) : 1);                   // TSIH
            h.AsSpan(16, 4).CopyTo(r.AsSpan(16));                                        // initiator task tag
            r[36] = status.Class;
            r[37] = status.Detail;
            Stamp(response);
            await response.WriteAsync(stream, cancel);
            if (!ok) return false;
            if (final) return true;
        }
    }

    private static bool Offers(string list, string choice) => list.Split(',').Contains(choice);

    // ---- Full feature phase -----------------------------------------------------------------

    /// <summary>
    /// Applies command sequencing. On one in-order connection every numbered request must carry the expected
    /// CmdSN; anything else is a duplicate or out of the window and is ignored (RFC 7143, 4.2.2.1).
    /// </summary>
    private bool Accept(Pdu pdu)
    {
        bool numbered = pdu.Opcode is 0x00 or 0x01 or 0x02 or 0x04 or 0x06;
        if (!numbered || pdu.Immediate) return true;
        if (U32(pdu.Header, 24) != expCmdSn) return false;
        expCmdSn++;
        return true;
    }

    private async Task<bool> HandleAsync(Pdu pdu, CancellationToken cancel)
    {
        if (!Accept(pdu)) return true;

        switch (pdu.Opcode)
        {
            case 0x00:                                      // NOP-Out
                await NopAsync(pdu, cancel);
                return true;

            case 0x01 when !discovery:                      // SCSI command
                return await ScsiCommandAsync(pdu, cancel);

            case 0x02:                                      // task management
                await TaskManagementAsync(pdu, cancel);
                return true;

            case 0x04:                                      // text request
                var text = Reply(pdu, 0x24, Pdu.FormatText(TextReply(pdu)));
                Put32(text.Header, 20, NoTag);
                await SendAsync(text, cancel);
                return true;

            case 0x05:                                      // Data-Out for a transfer that is no longer open
                return true;

            case 0x06:                                      // logout
                await SendAsync(Reply(pdu, 0x26), cancel);
                return false;

            default:
                var reject = Pdu.Create(0x3F, pdu.Header);
                reject.Header[1] = 0x80;
                reject.Header[2] = 0x05;                    // command not supported
                Put32(reject.Header, 16, NoTag);
                await SendAsync(reject, cancel);
                return true;
        }
    }

    private async Task NopAsync(Pdu pdu, CancellationToken cancel)
    {
        if (pdu.InitiatorTaskTag == NoTag) return;          // an answer to a ping, not a ping
        var ping = Reply(pdu, 0x20, pdu.Data);
        pdu.Header.AsSpan(8, 8).CopyTo(ping.Header.AsSpan(8));
        Put32(ping.Header, 20, NoTag);
        await SendAsync(ping, cancel);
    }

    /// <summary>
    /// Commands run to completion one at a time, so by the time a task management request is read there is
    /// nothing left to abort, except a write still collecting its data, which the caller ends.
    /// </summary>
    private async Task TaskManagementAsync(Pdu pdu, CancellationToken cancel)
    {
        int function = pdu.Header[1] & 0x7F;
        byte outcome;
        switch (function)
        {
            case 1 or 2 or 4:                               // abort task, abort task set, clear task set
                outcome = 0;                                // function complete
                break;
            case 5 or 6 or 7:                               // logical unit reset, target warm and cold reset
                device.Reset();
                outcome = 0;
                break;
            case 8:
                outcome = 4;                                // task reassignment not supported
                break;
            default:
                outcome = 5;                                // function not supported
                break;
        }
        var response = Reply(pdu, 0x22);
        response.Header[2] = outcome;
        await SendAsync(response, cancel);
    }

    /// <summary>A final response PDU carrying the request's initiator task tag.</summary>
    private static Pdu Reply(Pdu request, byte opcode, byte[]? data = null)
    {
        var response = Pdu.Create(opcode, data);
        response.Header[1] = 0x80;
        request.Header.AsSpan(16, 4).CopyTo(response.Header.AsSpan(16));
        return response;
    }

    private async Task SendAsync(Pdu response, CancellationToken cancel, bool carriesStatus = true)
    {
        Stamp(response, carriesStatus);
        await response.WriteAsync(stream, cancel);
    }

    private List<Pair> TextReply(Pdu request)
    {
        var reply = new List<Pair>();
        var local = (IPEndPoint)client.Client.LocalEndPoint!;
        foreach (var (key, value) in Pdu.ParseText(request.Data))
        {
            if (key != "SendTargets") reply.Add(new(key, "NotUnderstood"));
            else if (value is "All" or "" || value == targetName)
            {
                reply.Add(new("TargetName", targetName));
                reply.Add(new("TargetAddress", $"{local.Address}:{local.Port},1"));
            }
        }
        return reply;
    }

    private async Task<bool> ScsiCommandAsync(Pdu pdu, CancellationToken cancel)
    {
        var h = pdu.Header;
        bool write = (h[1] & 0x20) != 0;
        uint expected = U32(h, 20);
        if (expected > MaxExpectedTransfer) return false;

        byte[]? dataOut = pdu.Data;
        if (write && dataOut.Length < expected)
        {
            (dataOut, bool aborted) = await SolicitAsync(pdu, expected, cancel);
            if (aborted) return true;                       // the initiator gave the task up: no response is owed
            if (dataOut is null) return false;
        }

        int lun = (h[8] & 0x3F) << 8 | h[9];
        var result = lun == 0 ? device.Execute(h.AsSpan(32, 16), dataOut) : OtherLun(h.AsSpan(32, 16));

        // How the data the device produced compares with what the initiator asked for.
        long produced = write ? 0 : result.DataIn.Length;
        int length = (int)Math.Min(produced, expected);
        byte fit = 0;
        uint residual = 0;
        if (produced > expected) { fit = 0x04; residual = (uint)(produced - expected); }             // overflow
        else if (!write && produced < expected) { fit = 0x02; residual = (uint)(expected - produced); } // underflow

        if (result.Status == ScsiStatus.Good && length > 0)
        {
            // Data goes out in sequences of at most MaxBurstLength, each ending with the F bit.
            // The last PDU of the last sequence also carries the status.
            uint dataSn = 0;
            int inSequence = 0;
            for (int offset = 0; offset < length;)
            {
                int n = Math.Min(Math.Min(initiatorMaxReceive, length - offset), maxBurst - inSequence);
                bool last = offset + n == length;
                bool endsSequence = last || inSequence + n == maxBurst;
                inSequence = endsSequence ? 0 : inSequence + n;
                var dataIn = Reply(pdu, 0x25, result.DataIn.AsSpan(offset, n).ToArray());
                var d = dataIn.Header;
                d[1] = (byte)((endsSequence ? 0x80 : 0) | (last ? 0x01 | fit : 0));
                h.AsSpan(8, 8).CopyTo(d.AsSpan(8));
                Put32(d, 20, NoTag);
                Put32(d, 36, dataSn++);
                Put32(d, 40, offset);
                if (last) Put32(d, 44, residual);
                await SendAsync(dataIn, cancel, carriesStatus: last);
                offset += n;
            }
            return true;
        }

        byte[] sense = [];
        if (result.Sense is { } s)
        {
            var fixedFormat = s.ToFixedFormat();
            sense = new byte[2 + fixedFormat.Length];
            Put16(sense, 0, fixedFormat.Length);
            fixedFormat.CopyTo(sense, 2);
        }
        var response = Reply(pdu, 0x21, sense);
        response.Header[1] = (byte)(0x80 | fit);
        response.Header[3] = result.Status;
        Put32(response.Header, 44, residual);
        await SendAsync(response, cancel);
        return true;
    }

    /// <summary>Only LUN 0 exists. Other LUNs answer INQUIRY and REPORT LUNS as SPC requires.</summary>
    private ScsiResult OtherLun(ReadOnlySpan<byte> cdb)
    {
        switch (cdb[0])
        {
            case 0x12:
                var inquiry = new byte[36];
                inquiry[0] = 0x7F;                           // no device at this LUN
                inquiry[3] = 0x02;
                inquiry[4] = 31;
                return ScsiResult.Good(Fit(inquiry, U16(cdb, 3)));
            case 0xA0:
                return device.Execute(cdb, []);
            default:
                return ScsiResult.Check(Sense.LunNotSupported);
        }
    }

    /// <summary>
    /// Collects the rest of a write's data with R2T. Returns no data on a protocol error, which ends the
    /// connection, and Aborted when the initiator ended the task with a task management request.
    /// </summary>
    private async Task<(byte[]? Data, bool Aborted)> SolicitAsync(Pdu command, uint expected, CancellationToken cancel)
    {
        var buffer = new byte[expected];
        command.Data.CopyTo(buffer, 0);
        uint offset = (uint)command.Data.Length, r2tSn = 0;

        while (offset < expected)
        {
            uint want = Math.Min(expected - offset, (uint)maxBurst);
            uint tag = ++lastTransferTag;
            var r2t = Reply(command, 0x31);
            command.Header.AsSpan(8, 8).CopyTo(r2t.Header.AsSpan(8));
            Put32(r2t.Header, 20, tag);
            Put32(r2t.Header, 36, r2tSn++);
            Put32(r2t.Header, 40, offset);
            Put32(r2t.Header, 44, want);
            await SendAsync(r2t, cancel, carriesStatus: false);

            uint received = 0, dataSn = 0;
            while (true)
            {
                var pdu = await Pdu.ReadAsync(stream, cancel);
                if (pdu is null) return (null, false);

                if (pdu.Opcode == 0x05 && U32(pdu.Header, 20) == tag)
                {
                    // Data must be for this task, in order, contiguous, and exactly what was asked for.
                    if (pdu.InitiatorTaskTag != command.InitiatorTaskTag
                        || U32(pdu.Header, 36) != dataSn++
                        || U32(pdu.Header, 40) != offset + received
                        || received + (uint)pdu.Data.Length > want)
                        return (null, false);
                    pdu.Data.CopyTo(buffer, offset + received);
                    received += (uint)pdu.Data.Length;
                    if ((pdu.Header[1] & 0x80) == 0) continue;
                    if (received != want) return (null, false);
                    break;
                }

                switch (pdu.Opcode)
                {
                    case 0x00 when Accept(pdu):
                        await NopAsync(pdu, cancel);
                        break;
                    case 0x02 when Accept(pdu):
                        int function = pdu.Header[1] & 0x7F;
                        bool endsThisTask = function is 2 or 4 or 5 or 6 or 7
                            || (function == 1 && U32(pdu.Header, 20) == command.InitiatorTaskTag);
                        await TaskManagementAsync(pdu, cancel);
                        if (endsThisTask) return (null, true);
                        break;
                    case 0x00 or 0x02:
                        break;                              // out of sequence: ignored
                    default:
                        deferred.Enqueue(pdu);              // handled once this write has its data
                        break;
                }
            }
            offset += want;
        }
        return (buffer, false);
    }
}
