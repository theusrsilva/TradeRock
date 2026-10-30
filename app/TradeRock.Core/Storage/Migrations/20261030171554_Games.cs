using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeRock.Core.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Games : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Todos os Pokémon de antes da Entrega 14 são do FireRed/LeafGreen.
            migrationBuilder.AddColumn<string>(
                name: "Family",
                table: "Pokemon",
                type: "TEXT",
                nullable: false,
                defaultValue: "Frlg");

            migrationBuilder.AddColumn<byte>(
                name: "Form",
                table: "Pokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Family",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "Form",
                table: "Pokemon");
        }
    }
}
