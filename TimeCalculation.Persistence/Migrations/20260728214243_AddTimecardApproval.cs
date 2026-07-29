using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimecardApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "timecard_approvals",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<LocalDate>(type: "date", nullable: false),
                    period_end = table.Column<LocalDate>(type: "date", nullable: false),
                    approved_by_user_id = table.Column<string>(type: "text", nullable: false),
                    approved_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    unapproved_by_user_id = table.Column<string>(type: "text", nullable: true),
                    unapproved_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    snapshot_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timecard_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_timecard_approvals_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_timecard_approvals_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_timecard_approvals_client_id_employee_id_period_start_perio",
                table: "timecard_approvals",
                columns: new[] { "client_id", "employee_id", "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_timecard_approvals_employee_id",
                table: "timecard_approvals",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "timecard_approvals");
        }
    }
}
