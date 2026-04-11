using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearnLuxembourgish.Data.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationAudioCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranslationAudios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LuxembourgishText = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    TextHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AudioData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationAudios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationAudios_TextHash",
                table: "TranslationAudios",
                column: "TextHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranslationAudios");
        }
    }
}
