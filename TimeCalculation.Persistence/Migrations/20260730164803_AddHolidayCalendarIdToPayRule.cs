using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayCalendarIdToPayRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "holiday_calendar_id",
                table: "pay_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_rules_holiday_calendar_id",
                table: "pay_rules",
                column: "holiday_calendar_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pay_rules_holiday_calendars_holiday_calendar_id",
                table: "pay_rules",
                column: "holiday_calendar_id",
                principalTable: "holiday_calendars",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pay_rules_holiday_calendars_holiday_calendar_id",
                table: "pay_rules");

            migrationBuilder.DropIndex(
                name: "ix_pay_rules_holiday_calendar_id",
                table: "pay_rules");

            migrationBuilder.DropColumn(
                name: "holiday_calendar_id",
                table: "pay_rules");
        }
    }
}
