using System.Net.Http.Json;
using System.Text.Json;

namespace Wobble.Testing;

public sealed record TraceLine(long Seq, double TimeMs, string Cdb, int DataOutLength, int DataInLength, byte Status, string? Sense);

/// <summary>Client for the emulator's HTTP control API.</summary>
public sealed class WobbleControl(string baseAddress) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = new() { BaseAddress = new Uri(baseAddress) };

    /// <summary>Inserts a disc image. <paramref name="kind"/> is "cd", "dvd" or null to choose by size.</summary>
    public async Task InsertMediaAsync(string image, string? kind = null) =>
        (await http.PutAsJsonAsync("/media", new { image, kind })).EnsureSuccessStatusCode();

    public async Task RemoveMediaAsync() =>
        (await http.PutAsJsonAsync("/media", new { image = (string?)null })).EnsureSuccessStatusCode();

    public async Task<IReadOnlyList<TraceLine>> TraceAsync() =>
        await http.GetFromJsonAsync<List<TraceLine>>("/trace", Json) ?? [];

    public async Task ResetAsync() => (await http.PostAsync("/reset", null)).EnsureSuccessStatusCode();

    public void Dispose() => http.Dispose();
}
