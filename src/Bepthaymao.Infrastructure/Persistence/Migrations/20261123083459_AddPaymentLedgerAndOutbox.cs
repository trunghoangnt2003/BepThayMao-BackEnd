using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLedgerAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "late_payment",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "overpaid",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "overpaid_amount",
                table: "orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_account_no",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_bank_code",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "restored_from_expiry",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "dish_stats",
                columns: table => new
                {
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dish_stats", x => new { x.dish_id, x.branch_id });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    consumer = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.consumer, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "operational_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operational_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_audits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    se_pay_transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    method = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    overpaid_amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    action_required = table.Column<bool>(type: "boolean", nullable: false),
                    payload_hash = table.Column<string>(type: "text", nullable: false),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_published_at_occurred_at",
                table: "outbox_messages",
                columns: new[] { "published_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_branch_id_received_at",
                table: "payments",
                columns: new[] { "branch_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id",
                table: "payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_se_pay_transaction_id",
                table: "payments",
                column: "se_pay_transaction_id",
                unique: true);
            migrationBuilder.Sql("UPDATE orders o SET payment_bank_code = b.bank_code, payment_account_no = b.bank_account_no FROM branches b WHERE b.id = o.branch_id");
            migrationBuilder.Sql("INSERT INTO dish_stats (dish_id, branch_id, order_count) SELECT i.dish_id, o.branch_id, SUM(i.quantity) FROM order_items i JOIN orders o ON o.id = i.order_id WHERE o.status <> 'Cancelled' AND o.payment_status IN ('Paid', 'LatePaid') GROUP BY i.dish_id, o.branch_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION decrement_cancelled_order_stats() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    UPDATE dish_stats s SET order_count = s.order_count - i.quantity
                    FROM (SELECT dish_id, SUM(quantity) AS quantity FROM order_items WHERE order_id = NEW.id GROUP BY dish_id) i
                    WHERE s.dish_id = i.dish_id AND s.branch_id = NEW.branch_id;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER cancelled_order_stats AFTER UPDATE OF status ON orders
                FOR EACH ROW WHEN (OLD.status <> 'Cancelled' AND NEW.status = 'Cancelled' AND OLD.payment_status <> 'Unpaid')
                EXECUTE FUNCTION decrement_cancelled_order_stats();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS cancelled_order_stats ON orders; DROP FUNCTION IF EXISTS decrement_cancelled_order_stats();");
            migrationBuilder.DropTable(
                name: "dish_stats");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "operational_alerts");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "payment_audits");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropColumn(
                name: "late_payment",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "overpaid",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "overpaid_amount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_account_no",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_bank_code",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "restored_from_expiry",
                table: "orders");
        }
    }
}
