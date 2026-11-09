using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Yoldas.Infrastructure.Persistence.Configurations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Yoldas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    from_meters = table.Column<int>(type: "integer", nullable: false),
                    to_meters = table.Column<int>(type: "integer", nullable: false),
                    pickup = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    dropoff = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    pickup_near = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dropoff_near = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    pickup_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dropoff_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price_per_seat_minor = table.Column<long>(type: "bigint", nullable: false),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cancelled_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    late_cancellation = table.Column<bool>(type: "boolean", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    respond_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_price", "price_per_seat_minor > 0");
                    table.CheckConstraint("ck_bookings_seats", "seats >= 1");
                    table.CheckConstraint("ck_bookings_span", "from_meters >= 0 AND to_meters > from_meters");
                    table.ForeignKey(
                        name: "fk_bookings_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bookings_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_passenger_id",
                        column: x => x.passenger_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_driver_id",
                table: "bookings",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_one_per_passenger",
                table: "bookings",
                columns: new[] { "trip_id", "passenger_id" },
                unique: true,
                filter: "status IN ('Requested', 'Confirmed', 'Boarded')");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_passenger_id_pickup_at",
                table: "bookings",
                columns: new[] { "passenger_id", "pickup_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_respond_by",
                table: "bookings",
                column: "respond_by",
                filter: "status = 'Requested'");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_trip_id_status",
                table: "bookings",
                columns: new[] { "trip_id", "status" });

            // How many seats are taken at the busiest point between two positions of a trip's route. The load only
            // rises where someone gets in, so it is enough to add up the seats at the start and at each boarding point.
            migrationBuilder.Sql($"""
                CREATE FUNCTION trip_seats_taken(trip uuid, from_m integer, to_m integer, except_booking uuid) RETURNS integer
                LANGUAGE sql STABLE AS $$
                    WITH spans AS (
                        SELECT from_meters, to_meters, seats FROM bookings
                        WHERE trip_id = trip AND status IN {BookingConfiguration.Holding}
                          AND id IS DISTINCT FROM except_booking AND from_meters < to_m AND from_m < to_meters
                    ), points AS (
                        SELECT from_m AS at UNION SELECT from_meters FROM spans WHERE from_meters > from_m
                    )
                    SELECT coalesce(max((SELECT coalesce(sum(seats), 0) FROM spans WHERE from_meters <= at AND at < to_meters)), 0)::integer FROM points
                $$;

                -- Every booking that takes seats is checked with the trip locked, so two confirmed at the same moment
                -- are counted one after the other and the second sees the first.
                CREATE FUNCTION bookings_check_seats() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE capacity integer;
                BEGIN
                    IF NEW.status NOT IN {BookingConfiguration.Holding} THEN
                        RETURN NEW;
                    END IF;
                    SELECT seats INTO capacity FROM trips WHERE id = NEW.trip_id FOR UPDATE;
                    IF trip_seats_taken(NEW.trip_id, NEW.from_meters, NEW.to_meters, NEW.id) + NEW.seats > capacity THEN
                        RAISE EXCEPTION 'trip % has fewer than % seats free between % m and % m', NEW.trip_id, NEW.seats, NEW.from_meters, NEW.to_meters
                            USING ERRCODE = 'check_violation', CONSTRAINT = '{BookingConfiguration.SeatsFree}';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER bookings_seats_free BEFORE INSERT OR UPDATE OF status, seats, from_meters, to_meters ON bookings
                    FOR EACH ROW EXECUTE FUNCTION bookings_check_seats();

                -- A driver can offer fewer seats only if the people already booked still fit.
                CREATE FUNCTION trips_check_seats() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.seats < OLD.seats AND trip_seats_taken(NEW.id, 0, NEW.length_meters, NULL) > NEW.seats THEN
                        RAISE EXCEPTION 'trip % has more than % seats booked on part of its route', NEW.id, NEW.seats
                            USING ERRCODE = 'check_violation', CONSTRAINT = '{BookingConfiguration.SeatsCoverBookings}';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trips_seats_cover_bookings BEFORE UPDATE OF seats ON trips
                    FOR EACH ROW EXECUTE FUNCTION trips_check_seats();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trips_seats_cover_bookings ON trips;
                DROP TRIGGER IF EXISTS bookings_seats_free ON bookings;
                DROP FUNCTION IF EXISTS trips_check_seats();
                DROP FUNCTION IF EXISTS bookings_check_seats();
                DROP FUNCTION IF EXISTS trip_seats_taken(uuid, integer, integer, uuid);
                """);

            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}
