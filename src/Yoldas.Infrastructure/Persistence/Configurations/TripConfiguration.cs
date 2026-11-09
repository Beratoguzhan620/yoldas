using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;

namespace Yoldas.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", t => t.HasCheckConstraint("ck_vehicles_seats", $"passenger_seats BETWEEN 1 AND {Vehicle.MaxPassengerSeats}"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Make).HasMaxLength(30);
        builder.Property(v => v.Model).HasMaxLength(30);
        builder.Property(v => v.Colour).HasMaxLength(20);
        builder.Property(v => v.Plate).HasMaxLength(12);
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.OwnerId).OnDelete(DeleteBehavior.Cascade);
        // One car per driver.
        builder.HasIndex(v => v.OwnerId).IsUnique();
    }
}

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("trips", t =>
        {
            t.HasCheckConstraint("ck_trips_seats", $"seats BETWEEN 1 AND {Vehicle.MaxPassengerSeats}");
            t.HasCheckConstraint("ck_trips_price", "price_per_seat_minor > 0");
            t.HasCheckConstraint("ck_trips_length", "length_meters > 0");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Path).HasColumnType("geography (linestring, 4326)");
        builder.Property(t => t.Notes).HasMaxLength(Trip.NotesMax);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(t => t.Route);
        builder.Ignore(t => t.Duration);
        builder.Ignore(t => t.ArrivalAt);
        builder.HasMany(t => t.Stops).WithOne().HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Stops).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.DriverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(t => t.VehicleId).OnDelete(DeleteBehavior.Restrict);
        // Search: trips whose line passes near a place, among those leaving on a day.
        builder.HasIndex(t => t.Path).HasMethod("gist");
        builder.HasIndex(t => t.DepartureAt).HasFilter("status = 'Scheduled'");
        builder.HasIndex(t => new { t.DriverId, t.DepartureAt });
    }
}

internal sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("trip_stops");
        builder.HasKey(s => new { s.TripId, s.Index });
        builder.Property(s => s.Name).HasMaxLength(60);
        builder.Property(s => s.Location).HasColumnType("geography (point, 4326)");
    }
}
