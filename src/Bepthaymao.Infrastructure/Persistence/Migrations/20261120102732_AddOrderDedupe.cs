using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dedupe_hash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "idempotency_key",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_dedupe_hash_created_at",
                table: "orders",
                columns: new[] { "dedupe_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_idempotency_key",
                table: "orders",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_dedupe_hash_created_at",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_idempotency_key",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "dedupe_hash",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "orders");
        }
    }
}
