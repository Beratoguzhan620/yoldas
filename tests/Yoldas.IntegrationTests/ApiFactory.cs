using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Yoldas.IntegrationTests.Support;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Yoldas.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL, shared by the whole collection, on a clock tests can move.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine").Build();

    /// <summary>Tests run one after another in this collection, so moving the clock affects only the test that does it.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string ConnectionString => _database.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Yoldas", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:AttemptsPerMinute", "100000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Today in Turkey on the test clock, plus some days.</summary>
    public DateOnly Day(int fromToday) => Core.Clock.Today(Clock.GetUtcNow()).AddDays(fromToday);

    /// <summary>A time in Turkey some days from today: Day(1) at 08:00 is tomorrow morning.</summary>
    public DateTimeOffset At(int fromToday, int hour, int minute = 0) => Core.Clock.StartOf(Day(fromToday)).AddHours(hour).AddMinutes(minute);

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue($"p{i}", parameters[i]);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
