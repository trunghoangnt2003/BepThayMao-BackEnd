using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCancellationKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "cancel_reason",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_kind",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Đơn hủy trước migration này chỉ có hai nguyên nhân: hết hạn hoặc khách hủy. cancel_reason từ nay là lý do tự do của nhân sự.
            migrationBuilder.Sql("""
                UPDATE orders SET cancellation_kind = 'Expired', cancel_reason = NULL WHERE status = 'Cancelled' AND cancel_reason = 'expired';
                UPDATE orders SET cancellation_kind = 'ByCustomer', cancel_reason = NULL WHERE status = 'Cancelled' AND cancel_reason = 'customer';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_amount_positive",
                table: "payments",
                sql: "amount > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_amount_positive",
                table: "payments");

            migrationBuilder.Sql("""
                UPDATE orders SET cancel_reason = 'expired' WHERE cancellation_kind = 'Expired';
                UPDATE orders SET cancel_reason = 'customer' WHERE cancellation_kind = 'ByCustomer';
                UPDATE orders SET cancel_reason = LEFT(cancel_reason, 50) WHERE LENGTH(cancel_reason) > 50;
                """);

            migrationBuilder.DropColumn(
                name: "cancellation_kind",
                table: "orders");

            migrationBuilder.AlterColumn<string>(
                name: "cancel_reason",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
