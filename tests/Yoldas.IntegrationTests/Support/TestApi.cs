using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yoldas.IntegrationTests.Support;

/// <summary>A signed-in person with an HTTP client that carries their token.</summary>
public sealed class Person(ApiFactory factory, SessionDto session) : IDisposable
{
    public HttpClient Http { get; } = Client(factory, session.AccessToken);

    public Guid Id => session.User.Id;

    public string Email => session.User.Email;

    public string Name => session.User.DisplayName;

    public string Token => session.AccessToken;

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Person> RegisterAsync(this ApiFactory factory, string name = "Ayşe Yılmaz", string? email = null)
    {
        using var anonymous = factory.CreateClient();
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password = Password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    /// <summary>Signs in again, for tests that move the clock past the first token's life.</summary>
    public static async Task<Person> SignInAsync(this ApiFactory factory, string email, string password = Password)
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    public static readonly object[] SamsunToAnkara =
    [
        new { name = "Samsun Otogar", latitude = 41.2601, longitude = 36.2847 },
        new { name = "Havza", latitude = 40.9700, longitude = 35.6622 },
        new { name = "Çorum", latitude = 40.5506, longitude = 34.9556 },
        new { name = "AŞTİ", latitude = 39.9180, longitude = 32.8100 },
    ];

    public static readonly (double Lat, double Lng) Samsun = (41.2867, 36.3300);
    public static readonly (double Lat, double Lng) Havza = (40.9700, 35.6622);
    public static readonly (double Lat, double Lng) Corum = (40.5506, 34.9556);
    public static readonly (double Lat, double Lng) Ankara = (39.9334, 32.8597);

    /// <summary>Gives the person a car: a white Clio for four passengers.</summary>
    public static async Task DriveAsync(this Person driver, int seats = 4)
    {
        var response = await driver.Http.PutAsJsonAsync("/api/me/vehicle", new { make = "Renault", model = "Clio", colour = "Beyaz", plate = "55 ABC 123", passengerSeats = seats });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A trip body: Samsun to Ankara by Havza and Çorum unless told otherwise, three seats at 400 TL.</summary>
    public static object Trip(DateTimeOffset departureAt, object[]? stops = null, int seats = 3, decimal price = 400m, bool instantBook = false, string? notes = null) =>
        new { stops = stops ?? SamsunToAnkara, departureAt, seats, pricePerSeat = price, instantBook, notes };

    public static async Task<TripDto> OfferAsync(this Person driver, object body)
    {
        var response = await driver.Http.PostAsJsonAsync("/api/trips", body);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Offering a trip failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return await response.ReadAsync<TripDto>();
    }

    /// <summary>Asks for seats from one place to another; the response is returned as it is, for tests that expect a refusal.</summary>
    public static Task<HttpResponseMessage> RequestAsync(this Person passenger, Guid tripId, (double Lat, double Lng) from, (double Lat, double Lng) to, int seats = 1, string? message = null) =>
        passenger.Http.PostAsJsonAsync("/api/bookings", new { tripId, fromLat = from.Lat, fromLng = from.Lng, toLat = to.Lat, toLng = to.Lng, seats, message });

    public static async Task<BookingDto> BookAsync(this Person passenger, Guid tripId, (double Lat, double Lng) from, (double Lat, double Lng) to, int seats = 1, string? message = null)
    {
        var response = await passenger.RequestAsync(tripId, from, to, seats, message);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Booking failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return await response.ReadAsync<BookingDto>();
    }

    public static string Search((double Lat, double Lng) from, (double Lat, double Lng) to, string extra = "") =>
        FormattableString.Invariant($"/api/trips?fromLat={from.Lat}&fromLng={from.Lng}&toLat={to.Lat}&toLng={to.Lng}{extra}");

    public static async Task<HttpResponseMessage> PostAsync(this Person person, string path, object? body = null) =>
        await person.Http.PostAsJsonAsync(path, body ?? new { });

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
