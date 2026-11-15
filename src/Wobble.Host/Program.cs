using System.Net;
using Wobble.Core;
using Wobble.Iscsi;

// wobble: serves one emulated optical drive over iSCSI, with a local HTTP control API for tests.
//   wobble [--image <path>] [--media cd|dvd] [--port 3260] [--control-port 3261] [--bind 127.0.0.1] [--iqn <name>]

var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--") || i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Bad argument '{args[i]}'. Usage: wobble [--image <path>] [--media cd|dvd] [--port 3260] [--control-port 3261] [--bind 127.0.0.1] [--iqn <name>]");
        return 2;
    }
    options[args[i][2..]] = args[i + 1];
}

string bind = options.GetValueOrDefault("bind", "127.0.0.1");
int port = int.Parse(options.GetValueOrDefault("port", "3260"));
int controlPort = int.Parse(options.GetValueOrDefault("control-port", "3261"));
string iqn = options.GetValueOrDefault("iqn", "iqn.2026-11.test.wobble:drive0");

using var drive = new OpticalDrive();
if (options.TryGetValue("image", out var image)) drive.InsertMedia(Open(image, options.GetValueOrDefault("media")));

await using var target = new IscsiTarget(drive, iqn, new IPEndPoint(IPAddress.Parse(bind), port));
target.Start();

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://{bind}:{controlPort}");
var app = builder.Build();

app.MapGet("/status", () => Results.Json(new
{
    target = iqn,
    port = target.LocalEndpoint.Port,
    media = drive.LoadedMedia is { } m ? new { kind = m.Kind.ToString(), blocks = m.BlockCount } : null,
}));

// Insert a disc ({"image": "<path>", "kind": "cd"|"dvd"|null}) or remove it ({"image": null}).
app.MapPut("/media", (MediaRequest request) =>
{
    if (request.Image is null)
    {
        drive.RemoveMedia();
        return Results.NoContent();
    }
    if (!File.Exists(request.Image)) return Results.BadRequest(new { error = $"Image not found: {request.Image}" });
    if (request.Kind is not (null or "cd" or "dvd")) return Results.BadRequest(new { error = "kind must be cd or dvd" });
    drive.InsertMedia(Open(request.Image, request.Kind));
    return Results.NoContent();
});

app.MapGet("/trace", () => Results.Json(drive.Trace));

// Back to a known state: no disc, empty trace.
app.MapPost("/reset", () =>
{
    drive.RemoveMedia();
    drive.ClearTrace();
    return Results.NoContent();
});

Console.WriteLine($"wobble: iSCSI target {iqn} on {target.LocalEndpoint}, control API on http://{bind}:{controlPort}");
await app.RunAsync();
return 0;

// An image is a cue sheet (.cue) or a cooked image such as an ISO; kind only applies to cooked images.
static Media Open(string path, string? kind) =>
    Media.Open(path, kind switch { "cd" => MediaKind.CdRom, "dvd" => MediaKind.DvdRom, _ => null });

internal sealed record MediaRequest(string? Image, string? Kind);
