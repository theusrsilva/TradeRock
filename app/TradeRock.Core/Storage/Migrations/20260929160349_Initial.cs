using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeRock.Core.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pokemon",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<int>(type: "INTEGER", nullable: false),
                    Species = table.Column<ushort>(type: "INTEGER", nullable: false),
                    SpeciesName = table.Column<string>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<byte>(type: "INTEGER", nullable: false),
                    IsShiny = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEgg = table.Column<bool>(type: "INTEGER", nullable: false),
                    Gender = table.Column<byte>(type: "INTEGER", nullable: false),
                    OtName = table.Column<string>(type: "TEXT", nullable: false),
                    Tid = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Sid = table.Column<ushort>(type: "INTEGER", nullable: false),
                    OriginGame = table.Column<string>(type: "TEXT", nullable: false),
                    HeldItem = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    SourceFileName = table.Column<string>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pokemon", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EngineId = table.Column<string>(type: "TEXT", nullable: false),
                    IsSimulation = table.Column<bool>(type: "INTEGER", nullable: false),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    TrainerName = table.Column<string>(type: "TEXT", nullable: false),
                    Tid = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Sid = table.Column<ushort>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    TradesPlanned = table.Column<int>(type: "INTEGER", nullable: false),
                    TradesCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCode = table.Column<string>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true),
                    EventsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Tid = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Sid = table.Column<ushort>(type: "INTEGER", nullable: true),
                    Game = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradeRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TradeIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SentPokemonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivedPokemonId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeEntries_Pokemon_ReceivedPokemonId",
                        column: x => x.ReceivedPokemonId,
                        principalTable: "Pokemon",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeEntries_Pokemon_SentPokemonId",
                        column: x => x.SentPokemonId,
                        principalTable: "Pokemon",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeEntries_Trades_TradeRecordId",
                        column: x => x.TradeRecordId,
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pokemon_Sha256",
                table: "Pokemon",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pokemon_Species",
                table: "Pokemon",
                column: "Species");

            migrationBuilder.CreateIndex(
                name: "IX_TradeEntries_ReceivedPokemonId",
                table: "TradeEntries",
                column: "ReceivedPokemonId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeEntries_SentPokemonId",
                table: "TradeEntries",
                column: "SentPokemonId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeEntries_TradeRecordId",
                table: "TradeEntries",
                column: "TradeRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_StartedAt",
                table: "Trades",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TradeEntries");

            migrationBuilder.DropTable(
                name: "TrainerPresets");

            migrationBuilder.DropTable(
                name: "Pokemon");

            migrationBuilder.DropTable(
                name: "Trades");
        }
    }
}
