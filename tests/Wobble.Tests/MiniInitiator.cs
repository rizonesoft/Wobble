using System.Net;
using System.Net.Sockets;
using Wobble.Core;
using Wobble.Iscsi;

namespace Wobble.Tests;

/// <summary>Just enough of an iSCSI initiator to exercise the target in tests.</summary>
internal sealed class MiniInitiator(TcpClient tcp) : IDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly NetworkStream stream = tcp.GetStream();
    private uint cmdSn = 1, expStatSn, taskTag;

    public sealed record Outcome(byte Status, byte[] Data, byte[] Sense, byte Flags, uint Residual, int DataPdus);

    public static async Task<MiniInitiator> ConnectAsync(IPEndPoint endpoint)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(endpoint);
        return new MiniInitiator(tcp);
    }

    /// <summary>Logs in with one request that goes from operational negotiation straight to full feature phase.</summary>
    public async Task<(int Status, Dictionary<string, string> Keys)> LoginAsync(params (string Key, string Value)[] keys)
    {
        var pdu = Pdu.Create(0x43, Pdu.FormatText(keys.Select(k => new KeyValuePair<string, string>(k.Key, k.Value))));
        pdu.Header[1] = 0x80 | 1 << 2 | 3;
        pdu.Header[8] = 0x80;                                   // a random-format ISID
        pdu.Header[13] = 0x01;
        Bytes.Put32(pdu.Header, 16, ++taskTag);
        Bytes.Put32(pdu.Header, 24, cmdSn);
        await pdu.WriteAsync(stream, None);

        var response = await ReadAsync();
        return (response.Header[36] << 8 | response.Header[37], Pdu.ParseText(response.Data).ToDictionary(p => p.Key, p => p.Value));
    }

    public Task<(int Status, Dictionary<string, string> Keys)> LoginNormalAsync(string target, int maxReceive = 65536) => LoginAsync(
        ("InitiatorName", "iqn.2026-11.test.wobble:tests"), ("SessionType", "Normal"), ("TargetName", target),
        ("HeaderDigest", "None"), ("DataDigest", "None"), ("InitialR2T", "Yes"), ("ImmediateData", "Yes"),
        ("MaxRecvDataSegmentLength", maxReceive.ToString()), ("MaxBurstLength", "262144"), ("FirstBurstLength", "65536"));

    public async Task<List<KeyValuePair<string, string>>> TextAsync(string key, string value)
    {
        var pdu = Pdu.Create(0x04, Pdu.FormatText([new(key, value)]));
        pdu.Header[1] = 0x80;
        Bytes.Put32(pdu.Header, 16, ++taskTag);
        Bytes.Put32(pdu.Header, 20, 0xFFFFFFFF);
        Bytes.Put32(pdu.Header, 24, cmdSn++);
        Bytes.Put32(pdu.Header, 28, expStatSn);
        await pdu.WriteAsync(stream, None);
        return Pdu.ParseText((await ReadAsync()).Data);
    }

    /// <summary>
    /// Runs one SCSI command. Data-Out starts with up to one first burst of immediate data (none when
    /// <paramref name="immediateData"/> is false) and the rest follows when the target asks with R2T.
    /// </summary>
    public async Task<Outcome> CommandAsync(byte[] cdb, int expectedIn = 0, byte[]? dataOut = null, bool immediateData = true, byte lun = 0)
    {
        var pdu = Pdu.Create(0x01, dataOut is not null && immediateData ? dataOut[..Math.Min(dataOut.Length, 65536)] : []);
        pdu.Header[1] = (byte)(0x80 | (dataOut is not null ? 0x20 : expectedIn > 0 ? 0x40 : 0));
        pdu.Header[9] = lun;
        Bytes.Put32(pdu.Header, 16, ++taskTag);
        Bytes.Put32(pdu.Header, 20, dataOut?.Length ?? expectedIn);
        Bytes.Put32(pdu.Header, 24, cmdSn++);
        Bytes.Put32(pdu.Header, 28, expStatSn);
        cdb.CopyTo(pdu.Header, 32);
        await pdu.WriteAsync(stream, None);

        var data = new MemoryStream();
        int dataPdus = 0;
        while (true)
        {
            var r = await ReadAsync();
            switch (r.Opcode)
            {
                case 0x25:
                    Assert.Equal(data.Length, Bytes.U32(r.Header, 40));        // data arrives in order
                    data.Write(r.Data);
                    dataPdus++;
                    if ((r.Header[1] & 0x01) != 0)
                        return new Outcome(r.Header[3], data.ToArray(), [], r.Header[1], Bytes.U32(r.Header, 44), dataPdus);
                    break;
                case 0x21:
                    return new Outcome(r.Header[3], data.ToArray(), r.Data.Length > 2 ? r.Data[2..] : [], r.Header[1], Bytes.U32(r.Header, 44), dataPdus);
                case 0x31:
                    int offset = (int)Bytes.U32(r.Header, 40), want = (int)Bytes.U32(r.Header, 44);
                    var send = Pdu.Create(0x05, dataOut!.AsSpan(offset, want).ToArray());
                    send.Header[1] = 0x80;
                    Bytes.Put32(send.Header, 16, taskTag);
                    r.Header.AsSpan(20, 4).CopyTo(send.Header.AsSpan(20));     // target transfer tag
                    Bytes.Put32(send.Header, 28, expStatSn);
                    Bytes.Put32(send.Header, 40, offset);
                    await send.WriteAsync(stream, None);
                    break;
                default:
                    Assert.Fail($"Unexpected PDU opcode 0x{r.Opcode:X2}");
                    break;
            }
        }
    }

    private async Task<Pdu> ReadAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pdu = await Pdu.ReadAsync(stream, timeout.Token) ?? throw new EndOfStreamException("The target closed the connection.");
        // R2T and non-final Data-In carry no status, so they do not advance StatSN.
        bool carriesStatus = pdu.Opcode is not 0x31 && !(pdu.Opcode == 0x25 && (pdu.Header[1] & 0x01) == 0);
        if (carriesStatus) expStatSn = Bytes.U32(pdu.Header, 24) + 1;
        return pdu;
    }

    public void Dispose() => tcp.Dispose();
}
