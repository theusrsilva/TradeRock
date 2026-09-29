using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeRock.Core.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Legality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Fateful",
                table: "Pokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Legality",
                table: "Pokemon",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalityPkhexVersion",
                table: "Pokemon",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fateful",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "Legality",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "LegalityPkhexVersion",
                table: "Pokemon");
        }
    }
}
