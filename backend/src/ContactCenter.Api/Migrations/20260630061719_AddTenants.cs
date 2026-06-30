using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContactCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Queues_Name",
                table: "Queues");

            migrationBuilder.DropIndex(
                name: "IX_Agents_Name",
                table: "Agents");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Queues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Contacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Realm = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            // Backfill: bestaande (single-tenant) data toewijzen aan de standaard-tenant 'default'
            // (realm 'contactcenter'). Idempotent; doet niets op een verse database.
            migrationBuilder.Sql(@"
                INSERT INTO ""Tenants"" (""Slug"", ""DisplayName"", ""Realm"", ""Enabled"")
                SELECT 'default', 'Standaard', 'contactcenter', true
                WHERE NOT EXISTS (SELECT 1 FROM ""Tenants"" WHERE ""Slug"" = 'default');

                UPDATE ""Queues""   SET ""TenantId"" = t.""Id"" FROM ""Tenants"" t WHERE t.""Slug"" = 'default' AND ""Queues"".""TenantId""   = 0;
                UPDATE ""Agents""   SET ""TenantId"" = t.""Id"" FROM ""Tenants"" t WHERE t.""Slug"" = 'default' AND ""Agents"".""TenantId""   = 0;
                UPDATE ""Contacts"" SET ""TenantId"" = t.""Id"" FROM ""Tenants"" t WHERE t.""Slug"" = 'default' AND ""Contacts"".""TenantId"" = 0;
                UPDATE ""Settings"" SET ""TenantId"" = t.""Id"" FROM ""Tenants"" t WHERE t.""Slug"" = 'default' AND ""Settings"".""TenantId"" = 0;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_TenantId",
                table: "Settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Queues_TenantId_Name",
                table: "Queues",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Endpoint",
                table: "Agents",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_TenantId_Name",
                table: "Agents",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Realm",
                table: "Tenants",
                column: "Realm",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Settings_TenantId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Queues_TenantId_Name",
                table: "Queues");

            migrationBuilder.DropIndex(
                name: "IX_Agents_Endpoint",
                table: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_Agents_TenantId_Name",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Queues");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Agents");

            migrationBuilder.CreateIndex(
                name: "IX_Queues_Name",
                table: "Queues",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Name",
                table: "Agents",
                column: "Name",
                unique: true);
        }
    }
}
