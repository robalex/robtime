using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPeriodConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<LocalDate>(
                name: "pay_period_anchor",
                table: "pay_rules",
                type: "date",
                nullable: false,
                defaultValue: new NodaTime.LocalDate(1, 1, 1));

            // defaultValue: 1 (PayPeriodFrequency.BiWeekly), not EF's auto-inferred 0 (Weekly) — matches
            // PayRule.PayPeriodFrequency's C# default, which this generated migration doesn't read.
            migrationBuilder.AddColumn<int>(
                name: "pay_period_frequency",
                table: "pay_rules",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pay_period_anchor",
                table: "pay_rules");

            migrationBuilder.DropColumn(
                name: "pay_period_frequency",
                table: "pay_rules");
        }
    }
}
