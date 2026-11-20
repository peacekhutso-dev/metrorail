using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace MetroRail.Common;

/// <summary>
/// Domain events between services (NATS in production). Subjects follow
/// &lt;domain&gt;.&lt;entity&gt;.&lt;event&gt;, e.g. tracking.train.state_changed, billing.payment.succeeded.
/// Payloads are snake_case JSON objects; schemas in contracts/asyncapi/metrorail-events.yaml.
/// Handlers must be idempotent (events can be redelivered).
/// </summary>
public delegate Task EventHandler(string subject, JsonElement payload);

public interface IEventBus
{
    Task PublishAsync(string subject, object payload);
    void Subscribe(string subject, EventHandler handler);
}

public static class Subjects
{
    /// <summary>NATS-style wildcards: '*' = one token, '>' = the rest.</summary>
    public static bool Matches(string pattern, string subject)
    {
        var p = pattern.Split('.');
        var s = subject.Split('.');
        for (var i = 0; i < p.Length; i++)
        {
            if (p[i] == ">") return true;
            if (i >= s.Length || (p[i] != "*" && p[i] != s[i])) return false;
        }
        return p.Length == s.Length;
    }
}

/// <summary>Process-local bus for tests and single-process dev. Delivers synchronously (awaited).</summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<EventHandler>> _handlers = new();
    public List<(string Subject, JsonElement Payload)> Published { get; } = [];

    public async Task PublishAsync(string subject, object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload, Json.Options);
        lock (Published) Published.Add((subject, element));
        foreach (var (pattern, hs) in _handlers.ToArray())
            if (Subjects.Matches(pattern, subject))
                foreach (var h in hs.ToArray()) await h(subject, element);
    }

    public void Subscribe(string subject, EventHandler handler) =>
        _handlers.GetOrAdd(subject, _ => []).Add(handler);

    public IEnumerable<string> PublishedSubjects
    {
        get { lock (Published) return Published.Select(p => p.Subject).ToList(); }
    }
}

/// <summary>NATS core pub/sub with a queue group per service (one replica handles each event).</summary>
public sealed class NatsEventBus(ServiceSettings settings, ILogger<NatsEventBus> log) : IEventBus, IHostedService, IAsyncDisposable
{
    private readonly List<(string Subject, EventHandler Handler)> _subs = [];
    private readonly CancellationTokenSource _stop = new();
    private NatsConnection? _nc;

    public async Task PublishAsync(string subject, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json.Options);
        await Connection().PublishAsync(subject, bytes);
    }

    public void Subscribe(string subject, EventHandler handler) => _subs.Add((subject, handler));

    private NatsConnection Connection() =>
        _nc ??= new NatsConnection(new NatsOpts { Url = settings.NatsUrl, Name = settings.ServiceName });

    public async Task StartAsync(CancellationToken ct)
    {
        await Connection().ConnectAsync();
        foreach (var (subject, handler) in _subs)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var msg in Connection().SubscribeAsync<byte[]>(subject, queueGroup: settings.ServiceName,
                                   cancellationToken: _stop.Token))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(msg.Data ?? []);
                        await handler(msg.Subject, doc.RootElement.Clone());
                    }
                    catch (Exception e)
                    {
                        log.LogError(e, "event_handler_failed {Subject}", msg.Subject);
                    }
                }
            }, CancellationToken.None);
        }
        log.LogInformation("nats_connected {Url} subscriptions={Count}", settings.NatsUrl, _subs.Count);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _stop.CancelAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_nc is not null) await _nc.DisposeAsync();
    }
}
