using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    MiddleName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Salutation = table.Column<string>(type: "text", nullable: false),
                    PostNominalLetters = table.Column<string>(type: "text", nullable: false),
                    MinimumWage = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    HomeTimeZoneId = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pay_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    PunchPairResetHours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxShiftLengthHours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    DistanceBetweenShiftsHours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ExpectedBreakLengthMinutes = table.Column<int>(type: "integer", nullable: false),
                    ExpectedLunchLengthMinutes = table.Column<int>(type: "integer", nullable: false),
                    RoundingRule_RoundingStrategy = table.Column<int>(type: "integer", nullable: false),
                    RoundingRule_RoundingIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    RoundingRule_RoundingGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    ShiftDateStrategy = table.Column<int>(type: "integer", nullable: false),
                    ActivePremiumCodes = table.Column<string>(type: "text", nullable: false),
                    WorkweekStartDay = table.Column<int>(type: "integer", nullable: false),
                    OvertimeRule_WeeklyOvertimeThresholdHours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    OvertimeRule_HasDailyOvertime = table.Column<bool>(type: "boolean", nullable: false),
                    OvertimeRule_DailyOvertimeThresholdHours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    OvertimeRule_DailyDoubletimeThresholdHours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    OvertimeRule_HasSeventhDayRule = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pay_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    BaseRate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "punch_audits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PunchId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    PreviousValues = table.Column<string>(type: "text", nullable: true),
                    NewValues = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_punch_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "state_minimum_wages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    State = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<LocalDate>(type: "date", nullable: false),
                    EffectiveTo = table.Column<LocalDate>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_state_minimum_wages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pay_rule_assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PayRuleId = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<LocalDate>(type: "date", nullable: false),
                    EffectiveTo = table.Column<LocalDate>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pay_rule_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pay_rule_assignments_pay_rules_PayRuleId",
                        column: x => x.PayRuleId,
                        principalTable: "pay_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_position_assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PositionId = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<LocalDate>(type: "date", nullable: false),
                    EffectiveTo = table.Column<LocalDate>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_position_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_position_assignments_positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "punches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PunchTime = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    RoundedPunchTime = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PunchTimeZoneId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Subtype = table.Column<int>(type: "integer", nullable: true),
                    PositionId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    BonusKind = table.Column<int>(type: "integer", nullable: true),
                    CountsTowardRegularRate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    DevicePunchId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_punches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_punches_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_punches_positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "positions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_position_assignments_EmployeeId_EffectiveFrom",
                table: "employee_position_assignments",
                columns: new[] { "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_position_assignments_PositionId",
                table: "employee_position_assignments",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_pay_rule_assignments_EmployeeId_EffectiveFrom",
                table: "pay_rule_assignments",
                columns: new[] { "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_pay_rule_assignments_PayRuleId",
                table: "pay_rule_assignments",
                column: "PayRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_punch_audits_PunchId",
                table: "punch_audits",
                column: "PunchId");

            migrationBuilder.CreateIndex(
                name: "IX_punches_EmployeeId_DeviceId_DevicePunchId",
                table: "punches",
                columns: new[] { "EmployeeId", "DeviceId", "DevicePunchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_punches_EmployeeId_PunchTime",
                table: "punches",
                columns: new[] { "EmployeeId", "PunchTime" });

            migrationBuilder.CreateIndex(
                name: "IX_punches_PositionId",
                table: "punches",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_state_minimum_wages_State_EffectiveFrom",
                table: "state_minimum_wages",
                columns: new[] { "State", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "employee_position_assignments");

            migrationBuilder.DropTable(
                name: "pay_rule_assignments");

            migrationBuilder.DropTable(
                name: "punch_audits");

            migrationBuilder.DropTable(
                name: "punches");

            migrationBuilder.DropTable(
                name: "state_minimum_wages");

            migrationBuilder.DropTable(
                name: "pay_rules");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "positions");
        }
    }
}
