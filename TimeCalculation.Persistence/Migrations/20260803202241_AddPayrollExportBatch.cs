using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollExportBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_export_batches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<LocalDate>(type: "date", nullable: false),
                    period_end = table.Column<LocalDate>(type: "date", nullable: false),
                    employee_count = table.Column<int>(type: "integer", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    file_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    exported_by_user_id = table.Column<string>(type: "text", nullable: false),
                    exported_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    voided_by_user_id = table.Column<string>(type: "text", nullable: true),
                    voided_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_export_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_export_batches_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_export_batches_payroll_export_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "payroll_export_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_export_batches_client_id_profile_id_exported_at",
                table: "payroll_export_batches",
                columns: new[] { "client_id", "profile_id", "exported_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_export_batches_profile_id_period_start_period_end",
                table: "payroll_export_batches",
                columns: new[] { "profile_id", "period_start", "period_end" },
                unique: true,
                filter: "voided_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_export_batches");
        }
    }
}
