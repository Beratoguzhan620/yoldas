using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Yoldas.IntegrationTests.Support;

/// <summary>A hub connection over the test server that records every message it hears, for tests to wait on.</summary>
public sealed class LiveClient : IAsyncDisposable
{
    private readonly ConcurrentQueue<(string Name, JsonElement Body)> _heard = new();
    private readonly SemaphoreSlim _arrived = new(0);

    private LiveClient(HubConnection connection, params string[] messages)
    {
        Connection = connection;
        foreach (var name in messages)
        {
            connection.On<JsonElement>(name, body =>
            {
                _heard.Enqueue((name, body));
                _arrived.Release();
            });
        }
    }

    public HubConnection Connection { get; }

    public IReadOnlyList<(string Name, JsonElement Body)> Heard => [.. _heard];

    /// <summary>Connects as the person and follows the trip; returns the client and the view it got on joining.</summary>
    public static async Task<(LiveClient Client, LiveViewDto View)> FollowAsync(ApiFactory factory, Person person, Guid tripId)
    {
        var client = new LiveClient(Build(factory, "/hubs/trips", person.Token), "TripChanged");
        await client.Connection.StartAsync();
        var view = await client.Connection.InvokeAsync<LiveViewDto>("Follow", tripId);
        return (client, view);
    }

    public static HubConnection Build(ApiFactory factory, string path, string? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions = TestApi.Json)
            .Build();

    /// <summary>Waits until a message matching the test has arrived, and returns it.</summary>
    public async Task<JsonElement> WaitForAsync(string name, Func<JsonElement, bool>? match = null, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            foreach (var (heardName, body) in _heard)
            {
                if (heardName == name && (match is null || match(body)))
                {
                    return body;
                }
            }

            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !await _arrived.WaitAsync(left))
            {
                throw new TimeoutException($"No {name} arrived in {seconds} s. Heard: {string.Join(", ", _heard.Select(h => h.Name))}");
            }
        }
    }

    /// <summary>Gives in-flight messages a moment, then reports whether any matching one arrived.</summary>
    public async Task<bool> HeardAsync(string name, Func<JsonElement, bool> match, int milliseconds = 750)
    {
        await Task.Delay(milliseconds);
        return _heard.Any(h => h.Name == name && match(h.Body));
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        _arrived.Dispose();
    }
}

public static class JsonExtensions
{
    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;

    public static bool IsNull(this JsonElement e) => e.ValueKind == JsonValueKind.Null;
}
