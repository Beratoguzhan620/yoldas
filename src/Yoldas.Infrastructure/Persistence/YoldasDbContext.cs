using Microsoft.EntityFrameworkCore;
using Yoldas.Core.Bookings;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;

namespace Yoldas.Infrastructure.Persistence;

/// <summary>Accounts, their cars, the trips drivers offer and the seats passengers take on them.</summary>
public sealed class YoldasDbContext(DbContextOptions<YoldasDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<TripStop> TripStops => Set<TripStop>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YoldasDbContext).Assembly);
    }
}
