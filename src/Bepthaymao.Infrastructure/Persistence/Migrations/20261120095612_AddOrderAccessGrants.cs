using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAccessGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_access_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_access_grants_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_access_grants_order_id",
                table: "order_access_grants",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_access_grants_token_hash",
                table: "order_access_grants",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_access_grants");
        }
    }
}
