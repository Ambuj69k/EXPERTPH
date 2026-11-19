using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Manufacturing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QcSpecs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcSpecs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcSpecs_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcSpecs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    BatchSizeUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recipes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QcSpecId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SampleNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TesterSignature = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcSamples_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcSamples_QcSpecs_QcSpecId",
                        column: x => x.QcSpecId,
                        principalTable: "QcSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcSamples_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcSpecTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QcSpecId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Requirement = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcSpecTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcSpecTests_QcSpecs_QcSpecId",
                        column: x => x.QcSpecId,
                        principalTable: "QcSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcSpecTests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MfgDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Mrp = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PlannedUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MaterialCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OutputUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OutputBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MaterialsIssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProducedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionSignature = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Batches_OutputBatchId",
                        column: x => x.OutputBatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeLines_Products_MaterialProductId",
                        column: x => x.MaterialProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeLines_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QcSampleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QcSpecTestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Pass = table.Column<bool>(type: "bit", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcResults_QcSamples_QcSampleId",
                        column: x => x.QcSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcResults_QcSpecTests_QcSpecTestId",
                        column: x => x.QcSpecTestId,
                        principalTable: "QcSpecTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcResults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialConsumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_ProductionBatches_ProductionBatchId",
                        column: x => x.ProductionBatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_BatchId",
                table: "MaterialConsumptions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_ProductionBatchId",
                table: "MaterialConsumptions",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_TenantId_BatchId",
                table: "MaterialConsumptions",
                columns: new[] { "TenantId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_BranchId",
                table: "ProductionBatches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_OutputBatchId",
                table: "ProductionBatches",
                column: "OutputBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ProductId",
                table: "ProductionBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_RecipeId",
                table: "ProductionBatches",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_TenantId_ProductId_BatchNo",
                table: "ProductionBatches",
                columns: new[] { "TenantId", "ProductId", "BatchNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_TenantId_Status",
                table: "ProductionBatches",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QcResults_QcSampleId",
                table: "QcResults",
                column: "QcSampleId");

            migrationBuilder.CreateIndex(
                name: "IX_QcResults_QcSpecTestId",
                table: "QcResults",
                column: "QcSpecTestId");

            migrationBuilder.CreateIndex(
                name: "IX_QcResults_TenantId_QcSampleId",
                table: "QcResults",
                columns: new[] { "TenantId", "QcSampleId" });

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_BatchId",
                table: "QcSamples",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_QcSpecId",
                table: "QcSamples",
                column: "QcSpecId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_TenantId_BatchId",
                table: "QcSamples",
                columns: new[] { "TenantId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_QcSpecs_ProductId",
                table: "QcSpecs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSpecs_TenantId_ProductId",
                table: "QcSpecs",
                columns: new[] { "TenantId", "ProductId" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_QcSpecs_TenantId_ProductId_VersionNo",
                table: "QcSpecs",
                columns: new[] { "TenantId", "ProductId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcSpecTests_QcSpecId",
                table: "QcSpecTests",
                column: "QcSpecId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSpecTests_TenantId",
                table: "QcSpecTests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeLines_MaterialProductId",
                table: "RecipeLines",
                column: "MaterialProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeLines_RecipeId",
                table: "RecipeLines",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeLines_TenantId",
                table: "RecipeLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_ProductId",
                table: "Recipes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_TenantId_ProductId",
                table: "Recipes",
                columns: new[] { "TenantId", "ProductId" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_TenantId_ProductId_VersionNo",
                table: "Recipes",
                columns: new[] { "TenantId", "ProductId", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialConsumptions");

            migrationBuilder.DropTable(
                name: "QcResults");

            migrationBuilder.DropTable(
                name: "RecipeLines");

            migrationBuilder.DropTable(
                name: "ProductionBatches");

            migrationBuilder.DropTable(
                name: "QcSamples");

            migrationBuilder.DropTable(
                name: "QcSpecTests");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "QcSpecs");
        }
    }
}
