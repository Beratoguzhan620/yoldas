using Yoldas.Core.Bookings;

namespace Yoldas.Core.Reviews;

/// <summary>Who a review is about, in which role.</summary>
public enum ReviewOf
{
    Driver,
    Passenger,
}

/// <summary>One side's word on a ride: one to five stars and, if they like, a few lines.</summary>
public sealed class Review
{
    public const int CommentMax = 500;

    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private Review()
    {
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public Guid TripId { get; private set; }

    public Guid AuthorId { get; private set; }

    public Guid SubjectId { get; private set; }

    public ReviewOf Of { get; private set; }

    public int Stars { get; private set; }

    public string Comment { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Review Write(Booking booking, Guid authorId, int stars, string? comment, DateTimeOffset now)
    {
        if (authorId != booking.DriverId && authorId != booking.PassengerId)
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no booking with this id.");
        }

        if (!booking.CanBeReviewed(now))
        {
            throw new DomainException(ErrorCodes.InvalidState, "Reviews are written in the two weeks after a ride that took place.");
        }

        if (stars is < 1 or > 5)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Give one to five stars.");
        }

        var byPassenger = authorId == booking.PassengerId;
        return new Review
        {
            Id = Guid.CreateVersion7(now), BookingId = booking.Id, TripId = booking.TripId, AuthorId = authorId,
            SubjectId = byPassenger ? booking.DriverId : booking.PassengerId, Of = byPassenger ? ReviewOf.Driver : ReviewOf.Passenger, Stars = stars,
            Comment = Text.CleanParagraphs(comment, CommentMax, "A comment"), CreatedAt = now,
        };
    }
}
