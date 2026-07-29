using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeCalculation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPunchImportBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "import_batch_id",
                table: "punches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "punch_import_batches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    punch_count = table.Column<int>(type: "integer", nullable: false),
                    imported_by_user_id = table.Column<string>(type: "text", nullable: false),
                    imported_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_by_user_id = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_import_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_punch_import_batches_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_punches_import_batch_id",
                table: "punches",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_punch_import_batches_client_id_imported_at",
                table: "punch_import_batches",
                columns: new[] { "client_id", "imported_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_punches_punch_import_batches_import_batch_id",
                table: "punches",
                column: "import_batch_id",
                principalTable: "punch_import_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_punches_punch_import_batches_import_batch_id",
                table: "punches");

            migrationBuilder.DropTable(
                name: "punch_import_batches");

            migrationBuilder.DropIndex(
                name: "ix_punches_import_batch_id",
                table: "punches");

            migrationBuilder.DropColumn(
                name: "import_batch_id",
                table: "punches");
        }
    }
}
