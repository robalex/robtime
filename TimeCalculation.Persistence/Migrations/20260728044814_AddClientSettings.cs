using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_settings",
                columns: table => new
                {
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    require_punch_edit_approval = table.Column<bool>(type: "boolean", nullable: false),
                    show_full_pay_itemization_to_employees = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_settings", x => x.client_id);
                    table.ForeignKey(
                        name: "fk_client_settings_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_settings");
        }
    }
}
