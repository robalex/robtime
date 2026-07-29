using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPunchChangeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "punch_change_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    punch_id = table.Column<int>(type: "integer", nullable: true),
                    change_kind = table.Column<int>(type: "integer", nullable: false),
                    requested_punch_time = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    requested_punch_time_zone_id = table.Column<string>(type: "text", nullable: true),
                    requested_kind = table.Column<int>(type: "integer", nullable: true),
                    requested_subtype = table.Column<int>(type: "integer", nullable: true),
                    requested_position_id = table.Column<int>(type: "integer", nullable: true),
                    requested_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    requested_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    requested_bonus_kind = table.Column<int>(type: "integer", nullable: true),
                    requested_counts_toward_regular_rate = table.Column<bool>(type: "boolean", nullable: true),
                    requester_user_id = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reviewer_user_id = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_punch_change_requests_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_punch_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_punch_change_requests_client_id_status_employee_id",
                table: "punch_change_requests",
                columns: new[] { "client_id", "status", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_punch_change_requests_employee_id",
                table: "punch_change_requests",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "punch_change_requests");
        }
    }
}
