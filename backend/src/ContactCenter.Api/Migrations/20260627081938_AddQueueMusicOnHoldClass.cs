using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueMusicOnHoldClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MusicOnHoldClass",
                table: "Queues",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MusicOnHoldClass",
                table: "Queues");
        }
    }
}
