using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueOfferMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfferMode",
                table: "Queues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AutoDispatch");

            migrationBuilder.AddColumn<string>(
                name: "RoutingStrategy",
                table: "Queues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "LongestIdle");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfferMode",
                table: "Queues");

            migrationBuilder.DropColumn(
                name: "RoutingStrategy",
                table: "Queues");
        }
    }
}
