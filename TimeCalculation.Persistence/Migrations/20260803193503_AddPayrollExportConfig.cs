using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollExportConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_export_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    grouping = table.Column<int>(type: "integer", nullable: false),
                    rounding_policy = table.Column<int>(type: "integer", nullable: false),
                    adjustment_earning_code = table.Column<string>(type: "text", nullable: false),
                    amount_scale = table.Column<int>(type: "integer", nullable: false),
                    hours_scale = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_export_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_export_profiles_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_earning_code_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    line_type = table.Column<int>(type: "integer", nullable: false),
                    line_code = table.Column<string>(type: "text", nullable: false),
                    earning_code = table.Column<string>(type: "text", nullable: false),
                    value_basis = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_earning_code_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_earning_code_mappings_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_earning_code_mappings_payroll_export_profiles_profi",
                        column: x => x.profile_id,
                        principalTable: "payroll_export_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_employee_identifiers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    external_employee_id = table.Column<string>(type: "text", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_employee_identifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_employee_identifiers_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_employee_identifiers_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_employee_identifiers_payroll_export_profiles_profil",
                        column: x => x.profile_id,
                        principalTable: "payroll_export_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_earning_code_mappings_client_id_profile_id",
                table: "payroll_earning_code_mappings",
                columns: new[] { "client_id", "profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_earning_code_mappings_client_id_profile_id_line_typ",
                table: "payroll_earning_code_mappings",
                columns: new[] { "client_id", "profile_id", "line_type", "line_code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_earning_code_mappings_profile_id",
                table: "payroll_earning_code_mappings",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_employee_identifiers_client_id_profile_id",
                table: "payroll_employee_identifiers",
                columns: new[] { "client_id", "profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_employee_identifiers_employee_id",
                table: "payroll_employee_identifiers",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_employee_identifiers_profile_id_employee_id",
                table: "payroll_employee_identifiers",
                columns: new[] { "profile_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_employee_identifiers_profile_id_external_employee_id",
                table: "payroll_employee_identifiers",
                columns: new[] { "profile_id", "external_employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_export_profiles_client_id",
                table: "payroll_export_profiles",
                column: "client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_earning_code_mappings");

            migrationBuilder.DropTable(
                name: "payroll_employee_identifiers");

            migrationBuilder.DropTable(
                name: "payroll_export_profiles");
        }
    }
}
