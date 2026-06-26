using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSipPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SipPassword",
                table: "Agents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "changeme-dev");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SipPassword",
                table: "Agents");
        }
    }
}
