using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Yoldas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TripDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "driver_rating_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "driver_rating_sum",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "passenger_rating_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "passenger_rating_sum",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finished_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "live_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "live_km_per_hour",
                table: "trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "live_meters_along",
                table: "trips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "live_meters_off",
                table: "trips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "live_position",
                table: "trips",
                type: "geography (point, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "boarded_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "boarding_code",
                table: "bookings",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "wrong_codes",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    of = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_stars", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reviews_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_users_subject_id",
                        column: x => x.subject_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trips_open",
                table: "trips",
                column: "status",
                filter: "status IN ('Scheduled', 'Started')");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_author_id",
                table: "reviews",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_booking_id_author_id",
                table: "reviews",
                columns: new[] { "booking_id", "author_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_subject_id_of_created_at",
                table: "reviews",
                columns: new[] { "subject_id", "of", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_trips_open",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "driver_rating_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "driver_rating_sum",
                table: "users");

            migrationBuilder.DropColumn(
                name: "passenger_rating_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "passenger_rating_sum",
                table: "users");

            migrationBuilder.DropColumn(
                name: "finished_at",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "live_at",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "live_km_per_hour",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "live_meters_along",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "live_meters_off",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "live_position",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "boarded_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "boarding_code",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "wrong_codes",
                table: "bookings");
        }
    }
}
