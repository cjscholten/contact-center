using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueTtsText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosedText",
                table: "Queues",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Voice",
                table: "Queues",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "nl_NL-pim-medium");

            migrationBuilder.AddColumn<string>(
                name: "WelcomeText",
                table: "Queues",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedText",
                table: "Queues");

            migrationBuilder.DropColumn(
                name: "Voice",
                table: "Queues");

            migrationBuilder.DropColumn(
                name: "WelcomeText",
                table: "Queues");
        }
    }
}
