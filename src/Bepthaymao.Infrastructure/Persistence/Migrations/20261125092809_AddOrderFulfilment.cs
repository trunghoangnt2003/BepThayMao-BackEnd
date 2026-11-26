using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFulfilment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "abandoned_alerted_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepted_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason_code",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_recorded_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE orders SET paid_recorded_at = paid_at WHERE paid_at IS NOT NULL AND paid_recorded_at IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_orders_abandoned_scan",
                table: "orders",
                columns: new[] { "status", "abandoned_alerted_at", "paid_recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_abandoned_scan",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "abandoned_alerted_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "accepted_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_reason_code",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "paid_recorded_at",
                table: "orders");
        }
    }
}
