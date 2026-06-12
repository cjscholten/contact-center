using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContactCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Queues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WelcomePrompt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClosedPrompt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AdHocClosed = table.Column<bool>(type: "boolean", nullable: false),
                    AdHocForwardNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Queues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboundNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QueueConfigId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboundNumbers_Queues_QueueConfigId",
                        column: x => x.QueueConfigId,
                        principalTable: "Queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpeningHoursWindow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QueueConfigId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Opens = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Closes = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningHoursWindow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningHoursWindow_Queues_QueueConfigId",
                        column: x => x.QueueConfigId,
                        principalTable: "Queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundNumbers_Number",
                table: "InboundNumbers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundNumbers_QueueConfigId",
                table: "InboundNumbers",
                column: "QueueConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningHoursWindow_QueueConfigId",
                table: "OpeningHoursWindow",
                column: "QueueConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_Queues_Name",
                table: "Queues",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundNumbers");

            migrationBuilder.DropTable(
                name: "OpeningHoursWindow");

            migrationBuilder.DropTable(
                name: "Queues");
        }
    }
}
