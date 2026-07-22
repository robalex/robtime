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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "state_minimum_wages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    state = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<LocalDate>(type: "date", nullable: false),
                    effective_to = table.Column<LocalDate>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_state_minimum_wages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_premium_policies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    premium_code = table.Column<string>(type: "text", nullable: false),
                    waiver_policy = table.Column<int>(type: "integer", nullable: false),
                    set_by = table.Column<string>(type: "text", nullable: false),
                    set_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    effective_from = table.Column<LocalDate>(type: "date", nullable: false),
                    effective_to = table.Column<LocalDate>(type: "date", nullable: true),
                    justification = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_premium_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_premium_policies_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "differential_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    day_schedule_mode = table.Column<int>(type: "integer", nullable: false),
                    days_of_week = table.Column<string>(type: "text", nullable: false),
                    day_of_week_range_start = table.Column<int>(type: "integer", nullable: false),
                    day_of_week_range_end = table.Column<int>(type: "integer", nullable: false),
                    specific_dates = table.Column<string>(type: "text", nullable: false),
                    window_start = table.Column<LocalTime>(type: "time", nullable: false),
                    window_end = table.Column<LocalTime>(type: "time", nullable: false),
                    adjustment_type = table.Column<int>(type: "integer", nullable: false),
                    adjustment_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    min_hours_in_window = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    min_hours_in_range = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    exclusivity_group = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_differential_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_differential_rules_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    middle_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    salutation = table.Column<string>(type: "text", nullable: false),
                    post_nominal_letters = table.Column<string>(type: "text", nullable: false),
                    minimum_wage = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    home_time_zone_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holiday_calendars",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    dates = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendars", x => x.id);
                    table.ForeignKey(
                        name: "fk_holiday_calendars_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    template_code = table.Column<string>(type: "text", nullable: true),
                    template_version = table.Column<int>(type: "integer", nullable: true),
                    rule_family_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<LocalDate>(type: "date", nullable: true),
                    effective_to = table.Column<LocalDate>(type: "date", nullable: true),
                    punch_pair_reset_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    max_shift_length_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    distance_between_shifts_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    expected_break_length_minutes = table.Column<int>(type: "integer", nullable: false),
                    expected_lunch_length_minutes = table.Column<int>(type: "integer", nullable: false),
                    rounding_rule_rounding_strategy = table.Column<int>(type: "integer", nullable: false),
                    rounding_rule_rounding_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    rounding_rule_rounding_grace_minutes = table.Column<int>(type: "integer", nullable: false),
                    shift_date_strategy = table.Column<int>(type: "integer", nullable: false),
                    active_premium_codes = table.Column<string>(type: "text", nullable: false),
                    active_differential_codes = table.Column<string>(type: "text", nullable: false),
                    workweek_start_day = table.Column<int>(type: "integer", nullable: false),
                    overtime_rule_weekly_overtime_threshold_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    overtime_rule_has_daily_overtime = table.Column<bool>(type: "boolean", nullable: false),
                    overtime_rule_daily_overtime_threshold_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    overtime_rule_daily_doubletime_threshold_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    overtime_rule_has_seventh_day_rule = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_pay_rules_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    base_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_positions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "punch_audits",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    punch_id = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    previous_values = table.Column<string>(type: "text", nullable: true),
                    new_values = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_punch_audits_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_rule_assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    pay_rule_id = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<LocalDate>(type: "date", nullable: false),
                    effective_to = table.Column<LocalDate>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_rule_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_pay_rule_assignments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pay_rule_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pay_rule_assignments_pay_rules_pay_rule_id",
                        column: x => x.pay_rule_id,
                        principalTable: "pay_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_position_assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    position_id = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<LocalDate>(type: "date", nullable: false),
                    effective_to = table.Column<LocalDate>(type: "date", nullable: true),
                    rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_position_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_position_assignments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_position_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_position_assignments_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "punches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    punch_time = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    rounded_punch_time = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    punch_time_zone_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    subtype = table.Column<int>(type: "integer", nullable: true),
                    position_id = table.Column<int>(type: "integer", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    bonus_kind = table.Column<int>(type: "integer", nullable: true),
                    counts_toward_regular_rate = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    device_punch_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punches", x => x.id);
                    table.ForeignKey(
                        name: "fk_punches_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_punches_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_punches_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_premium_policies_client_id_premium_code_effective_fr",
                table: "client_premium_policies",
                columns: new[] { "client_id", "premium_code", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_differential_rules_client_id",
                table: "differential_rules",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_position_assignments_client_id_employee_id_effecti",
                table: "employee_position_assignments",
                columns: new[] { "client_id", "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_position_assignments_employee_id",
                table: "employee_position_assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_position_assignments_position_id",
                table: "employee_position_assignments",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_client_id",
                table: "employees",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_client_id",
                table: "holiday_calendars",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_rule_assignments_client_id_employee_id_effective_from",
                table: "pay_rule_assignments",
                columns: new[] { "client_id", "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_rule_assignments_employee_id",
                table: "pay_rule_assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_rule_assignments_pay_rule_id",
                table: "pay_rule_assignments",
                column: "pay_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_rules_client_id",
                table: "pay_rules",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_rules_rule_family_id",
                table: "pay_rules",
                column: "rule_family_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_client_id",
                table: "positions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_punch_audits_client_id_punch_id",
                table: "punch_audits",
                columns: new[] { "client_id", "punch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_punches_client_id_employee_id_punch_time",
                table: "punches",
                columns: new[] { "client_id", "employee_id", "punch_time" });

            migrationBuilder.CreateIndex(
                name: "ix_punches_employee_id_device_id_device_punch_id",
                table: "punches",
                columns: new[] { "employee_id", "device_id", "device_punch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_punches_position_id",
                table: "punches",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_state_minimum_wages_state_effective_from",
                table: "state_minimum_wages",
                columns: new[] { "state", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_premium_policies");

            migrationBuilder.DropTable(
                name: "differential_rules");

            migrationBuilder.DropTable(
                name: "employee_position_assignments");

            migrationBuilder.DropTable(
                name: "holiday_calendars");

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

            migrationBuilder.DropTable(
                name: "clients");
        }
    }
}
