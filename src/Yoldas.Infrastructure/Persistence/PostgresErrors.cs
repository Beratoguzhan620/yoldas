using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Yoldas.Infrastructure.Persistence;

public static class PostgresErrors
{
    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraint = null) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraint is null || string.Equals(pg.ConstraintName, constraint, StringComparison.Ordinal));

    /// <summary>The seat trigger refused the row: on some part of the route, fewer seats are free than asked for.</summary>
    public static bool IsSeatsTaken(this Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.CheckViolation } pg
        && pg.ConstraintName is Configurations.BookingConfiguration.SeatsFree or Configurations.BookingConfiguration.SeatsCoverBookings;

    public static bool IsForeignKeyViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
