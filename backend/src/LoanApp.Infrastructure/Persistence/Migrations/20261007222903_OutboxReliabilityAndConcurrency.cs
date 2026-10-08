using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxReliabilityAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "OutboxMessages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "OutboxMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "OutboxMessages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderingKey",
                table: "OutboxMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "OutboxMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LoanApplications",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Customers",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            // Backfill rows written before this migration, so the unique index can be created and ordering still applies.
            migrationBuilder.Sql("""
                UPDATE OutboxMessages
                SET EventId = NEWID(),
                    OrderingKey = COALESCE(JSON_VALUE(Payload, '$.Customer.Id'), CONVERT(nvarchar(100), Id))
                WHERE EventId = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_EventId",
                table: "OutboxMessages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt",
                table: "OutboxMessages",
                column: "ProcessedAt",
                filter: "[ProcessedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Unprocessed",
                table: "OutboxMessages",
                columns: new[] { "OrderingKey", "Id" },
                filter: "[ProcessedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_EventId",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Unprocessed",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "OrderingKey",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "Id",
                filter: "[ProcessedAt] IS NULL AND [FailedAt] IS NULL");
        }
    }
}
