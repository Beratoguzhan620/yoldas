namespace Yoldas.Api.Features.Bookings;

public sealed class SweeperOptions
{
    public const string Section = "Sweeper";

    /// <summary>Off in tests, which run passes by hand.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed record SweepPass(int Expired, int TripsClosed);

/// <summary>
/// Lets requests lapse when the driver does not answer, cancels trips that never started (as the driver's), and
/// finishes trips nobody finished. Safe on several instances: rows carry a version.
/// </summary>
public sealed partial class BookingSweeper(IServiceScopeFactory scopes, ILogger<BookingSweeper> logger)
{
    public async Task<SweepPass> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var expired = await bookings.ExpireAsync(ct);
        var closed = await scope.ServiceProvider.GetRequiredService<Live.TripDayService>().LapseAsync(ct);
        var live = scope.ServiceProvider.GetRequiredService<Live.LivePublisher>();
        foreach (var trip in closed)
        {
            await live.PublishAsync(trip);
        }

        if (expired + closed.Count > 0)
        {
            LogPass(logger, expired, closed.Count);
        }

        return new SweepPass(expired, closed.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sweep: {Expired} requests lapsed, {Trips} trips cancelled or finished by the clock")]
    private static partial void LogPass(ILogger logger, int expired, int trips);
}

public sealed partial class SweeperWorker(BookingSweeper sweeper, Microsoft.Extensions.Options.IOptions<SweeperOptions> options, ILogger<SweeperWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await sweeper.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A sweep failed; the next one tries again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
