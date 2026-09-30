using System;
using Microsoft.EntityFrameworkCore.Migrations;
using TradeRock.Core.Storage;

#nullable disable

namespace TradeRock.Core.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Accounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_StartedAt",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Name",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Pokemon_Sha256",
                table: "Pokemon");

            migrationBuilder.DropIndex(
                name: "IX_Boxes_Order",
                table: "Boxes");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "TrainerPresets",
                type: "TEXT",
                nullable: false,
                defaultValue: OwnerScope.LocalOwner); // o que já existia é do dono do PC

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Trades",
                type: "TEXT",
                nullable: false,
                defaultValue: OwnerScope.LocalOwner); // o que já existia é do dono do PC

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Tags",
                type: "TEXT",
                nullable: false,
                defaultValue: OwnerScope.LocalOwner); // o que já existia é do dono do PC

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Pokemon",
                type: "TEXT",
                nullable: false,
                defaultValue: OwnerScope.LocalOwner); // o que já existia é do dono do PC

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Boxes",
                type: "TEXT",
                nullable: false,
                defaultValue: OwnerScope.LocalOwner); // o que já existia é do dono do PC

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Disabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastLoginAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerPresets_OwnerId",
                table: "TrainerPresets",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_OwnerId_StartedAt",
                table: "Trades",
                columns: new[] { "OwnerId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_OwnerId_Name",
                table: "Tags",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pokemon_OwnerId_Sha256",
                table: "Pokemon",
                columns: new[] { "OwnerId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boxes_OwnerId_Order",
                table: "Boxes",
                columns: new[] { "OwnerId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TrainerPresets_OwnerId",
                table: "TrainerPresets");

            migrationBuilder.DropIndex(
                name: "IX_Trades_OwnerId_StartedAt",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Tags_OwnerId_Name",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Pokemon_OwnerId_Sha256",
                table: "Pokemon");

            migrationBuilder.DropIndex(
                name: "IX_Boxes_OwnerId_Order",
                table: "Boxes");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "TrainerPresets");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Pokemon");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Boxes");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_StartedAt",
                table: "Trades",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pokemon_Sha256",
                table: "Pokemon",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boxes_Order",
                table: "Boxes",
                column: "Order");
        }
    }
}
