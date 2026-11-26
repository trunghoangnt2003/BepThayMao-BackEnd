using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceZaloWithTelegramChatId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "zalo_recipient_ids",
                table: "branches");

            migrationBuilder.AddColumn<long>(
                name: "telegram_chat_id",
                table: "branches",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "telegram_chat_id",
                table: "branches");

            migrationBuilder.AddColumn<List<string>>(
                name: "zalo_recipient_ids",
                table: "branches",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }
    }
}
