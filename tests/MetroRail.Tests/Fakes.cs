using System.Text.Json;
using MetroRail.Common;

namespace MetroRail.Tests;

/// <summary>Stand-in for another service: maps a path to a JSON response (or throws like a 404).</summary>
public sealed class FakeService(Func<string, object?> respond) : IServiceClient
{
    public Task<JsonElement> GetAsync(string path)
    {
        var r = respond(path) ?? throw new NotFoundException("no live data", "no_live_data");
        return Task.FromResult(JsonSerializer.SerializeToElement(r, Json.Options));
    }
}
