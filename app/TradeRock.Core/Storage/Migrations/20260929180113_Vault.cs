using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeRock.Core.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Vault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoxId",
                table: "Pokemon",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedAt",
                table: "Pokemon",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "Pid",
                table: "Pokemon",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Slot",
                table: "Pokemon",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Boxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boxes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PokemonTags",
                columns: table => new
                {
                    PokemonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PokemonTags", x => new { x.PokemonId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PokemonTags_Pokemon_PokemonId",
                        column: x => x.PokemonId,
                        principalTable: "Pokemon",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PokemonTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pokemon_BoxId_Slot",
                table: "Pokemon",
                columns: new[] { "BoxId", "Slot" },
                unique: true,
                filter: "\"BoxId\" IS NOT NULL AND \"Slot\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pokemon_DeletedAt",
                table: "Pokemon",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Boxes_Order",
                table: "Boxes",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_PokemonTags_TagId",
                table: "PokemonTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pokemon_Boxes_BoxId",
                table: "Pokemon",
                column: "BoxId",
                principalTable: "Boxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pokemon_Boxes_BoxId",
                table: "Pokemon");

            migrationBuilder.DropTable(
                name: "Boxes");

            migrationBuilder.DropTable(
                name: "PokemonTags");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Pokemon_BoxId_Slot",
                table: "Pokemon");

            migrationBuilder.DropIndex(
                name: "IX_Pokemon_DeletedAt",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "BoxId",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "Pid",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "Slot",
                table: "Pokemon");
        }
    }
}
