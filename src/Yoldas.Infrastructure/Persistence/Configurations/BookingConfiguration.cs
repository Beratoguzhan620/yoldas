using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yoldas.Core.Bookings;
using Yoldas.Core.Reviews;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;

namespace Yoldas.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <summary>The name the seat trigger reports when a booking would need more seats than are free.</summary>
    public const string SeatsFree = "ck_bookings_seats_free";

    /// <summary>The name the trip trigger reports when a trip would offer fewer seats than are booked.</summary>
    public const string SeatsCoverBookings = "ck_trips_seats_cover_bookings";

    /// <summary>The statuses that take seats.</summary>
    public const string Holding = "('Confirmed', 'Boarded')";

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", t =>
        {
            t.HasCheckConstraint("ck_bookings_span", "from_meters >= 0 AND to_meters > from_meters");
            t.HasCheckConstraint("ck_bookings_seats", "seats >= 1");
            t.HasCheckConstraint("ck_bookings_price", "price_per_seat_minor > 0");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Pickup).HasColumnType("geography (point, 4326)");
        builder.Property(b => b.Dropoff).HasColumnType("geography (point, 4326)");
        builder.Property(b => b.PickupNear).HasMaxLength(60);
        builder.Property(b => b.DropoffNear).HasMaxLength(60);
        builder.Property(b => b.Message).HasMaxLength(Booking.MessageMax);
        builder.Property(b => b.Reason).HasMaxLength(Booking.ReasonMax);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(b => b.CancelledBy).HasConversion<string>().HasMaxLength(10);
        builder.Property(b => b.BoardingCode).HasMaxLength(4);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(b => b.TotalMinor);
        builder.Ignore(b => b.Holds);
        builder.Ignore(b => b.IsOpen);
        builder.Ignore(b => b.Span);
        builder.HasOne<Trip>().WithMany().HasForeignKey(b => b.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.PassengerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.DriverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => new { b.TripId, b.Status });
        builder.HasIndex(b => new { b.PassengerId, b.PickupAt });
        // One live booking per passenger per trip; to change seats or places, cancel and book again.
        builder.HasIndex(b => new { b.TripId, b.PassengerId }).IsUnique().HasDatabaseName("ix_bookings_one_per_passenger")
            .HasFilter("status IN ('Requested', 'Confirmed', 'Boarded')");
        builder.HasIndex(b => b.RespondBy).HasFilter("status = 'Requested'");
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_stars", "stars BETWEEN 1 AND 5"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Of).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.Comment).HasMaxLength(Review.CommentMax);
        builder.HasOne<Booking>().WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.SubjectId).OnDelete(DeleteBehavior.Restrict);
        // One review per side per ride, enforced by the database.
        builder.HasIndex(r => new { r.BookingId, r.AuthorId }).IsUnique();
        builder.HasIndex(r => new { r.SubjectId, r.Of, r.CreatedAt });
    }
}
