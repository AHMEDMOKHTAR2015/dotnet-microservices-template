using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Orders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stage",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stage", x => x.Id);
                    table.UniqueConstraint("AK_Stage_Name", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "StageTransition",
                columns: table => new
                {
                    CurrentStage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DestinationStage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageTransition", x => new { x.CurrentStage, x.ActionType, x.DestinationStage });
                });

            migrationBuilder.CreateTable(
                name: "Order",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubmittedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomerEmail = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LastModifiedById = table.Column<int>(type: "int", nullable: true),
                    LastModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Order", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Order_Stage_Stage",
                        column: x => x.Stage,
                        principalTable: "Stage",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderAction",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderAction_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLine_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    StageId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageHistory_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageHistory_Stage_StageId",
                        column: x => x.StageId,
                        principalTable: "Stage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Stage",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 101, "The customer created the order", "Draft" },
                    { 102, "Submitted for approval", "Submitted" },
                    { 103, "Approved by an order manager", "Approved" },
                    { 104, "Rejected by an order manager", "Rejected" }
                });

            migrationBuilder.InsertData(
                table: "StageTransition",
                columns: new[] { "ActionType", "CurrentStage", "DestinationStage" },
                values: new object[,]
                {
                    { "AddLine", "Draft", "Draft" },
                    { "SubmitOrder", "Draft", "Submitted" },
                    { "ApproveOrder", "Submitted", "Approved" },
                    { "RejectOrder", "Submitted", "Rejected" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Order_Stage",
                table: "Order",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAction_OrderId",
                table: "OrderAction",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLine_OrderId_ProductName",
                table: "OrderLine",
                columns: new[] { "OrderId", "ProductName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stage_Name",
                table: "Stage",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageHistory_OrderId",
                table: "StageHistory",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StageHistory_StageId",
                table: "StageHistory",
                column: "StageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderAction");

            migrationBuilder.DropTable(
                name: "OrderLine");

            migrationBuilder.DropTable(
                name: "StageHistory");

            migrationBuilder.DropTable(
                name: "StageTransition");

            migrationBuilder.DropTable(
                name: "Order");

            migrationBuilder.DropTable(
                name: "Stage");
        }
    }
}
