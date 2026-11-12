using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bepthaymao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchOwnershipAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "abandoned_threshold_minutes",
                table: "branches",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_no",
                table: "branches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_code",
                table: "branches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "lat",
                table: "branches",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "lng",
                table: "branches",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "branches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "sepay_account_ref",
                table: "branches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "zalo_recipient_ids",
                table: "branches",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateIndex(
                name: "ix_branches_owner_id",
                table: "branches",
                column: "owner_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_branches_abandoned_threshold",
                table: "branches",
                sql: "abandoned_threshold_minutes >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_branches_lat",
                table: "branches",
                sql: "lat BETWEEN -90 AND 90");

            migrationBuilder.AddCheckConstraint(
                name: "ck_branches_lng",
                table: "branches",
                sql: "lng BETWEEN -180 AND 180");

            migrationBuilder.AddForeignKey(
                name: "fk_branches_owners_owner_id",
                table: "branches",
                column: "owner_id",
                principalTable: "owners",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_branches_owners_owner_id",
                table: "branches");

            migrationBuilder.DropIndex(
                name: "ix_branches_owner_id",
                table: "branches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_branches_abandoned_threshold",
                table: "branches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_branches_lat",
                table: "branches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_branches_lng",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "abandoned_threshold_minutes",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "bank_account_no",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "bank_code",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "lat",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "lng",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "sepay_account_ref",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "zalo_recipient_ids",
                table: "branches");
        }
    }
}
