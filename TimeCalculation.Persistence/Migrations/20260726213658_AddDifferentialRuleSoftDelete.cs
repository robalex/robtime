using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDifferentialRuleSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "differential_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "differential_rules");
        }
    }
}
