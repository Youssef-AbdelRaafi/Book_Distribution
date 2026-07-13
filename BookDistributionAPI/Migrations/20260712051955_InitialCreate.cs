using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookDistributionAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcademicYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Governorates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Governorates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Semesters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AcademicYearId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Semesters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Semesters_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GovernorateId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cities_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Grade = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SemesterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    StockQuantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                    table.CheckConstraint("CK_Books_Price_NonNegative", "[Price] >= 0");
                    table.CheckConstraint("CK_Books_StockQuantity_NonNegative", "[StockQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_Books_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GovernorateId = table.Column<int>(type: "INTEGER", nullable: false),
                    CityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Logo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OwnerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OwnerPhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ResponsibleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResponsiblePhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    LandlinePhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Shift1Start = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Shift1End = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Shift2Start = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    Shift2End = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    ResponseRating = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    PaymentRating = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Libraries_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Libraries_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceYear = table.Column<int>(type: "INTEGER", nullable: false),
                    TermCode = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    SemesterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    PrintStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResponsibleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ResponsiblePhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoices_PrintStatus", "[PrintStatus] IN ('pending', 'printed')");
                    table.CheckConstraint("CK_Invoices_TotalAmount_NonNegative", "[TotalAmount] >= 0");
                    table.CheckConstraint("CK_Invoices_Type", "[Type] IN ('order', 'refund', 'clearance')");
                    table.ForeignKey(
                        name: "FK_Invoices_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LibraryBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryBooks", x => x.Id);
                    table.CheckConstraint("CK_LibraryBooks_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_LibraryBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryBooks_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VoucherNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    VoucherYear = table.Column<int>(type: "INTEGER", nullable: false),
                    LibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    SemesterId = table.Column<int>(type: "INTEGER", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ChequeNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptVouchers", x => x.Id);
                    table.CheckConstraint("CK_ReceiptVouchers_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_ReceiptVouchers_PaymentMethod", "[PaymentMethod] IN ('cash', 'cheque')");
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    BookName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BookGrade = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(10,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceItems", x => x.Id);
                    table.CheckConstraint("CK_InvoiceItems_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_InvoiceItems_Total_NonNegative", "[Total] >= 0");
                    table.CheckConstraint("CK_InvoiceItems_UnitPrice_NonNegative", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYears_Name",
                table: "AcademicYears",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_SemesterId",
                table: "Books",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_SemesterId_Name_Grade",
                table: "Books",
                columns: new[] { "SemesterId", "Name", "Grade" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_GovernorateId",
                table: "Cities",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_GovernorateId_Name",
                table: "Cities",
                columns: new[] { "GovernorateId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Governorates_Name",
                table: "Governorates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_BookId",
                table: "InvoiceItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InvoiceId",
                table: "InvoiceItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Date",
                table: "Invoices",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LibraryId_InvoiceYear_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "LibraryId", "InvoiceYear", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LibraryId_SemesterId",
                table: "Invoices",
                columns: new[] { "LibraryId", "SemesterId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LibraryId_SemesterId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "LibraryId", "SemesterId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SemesterId",
                table: "Invoices",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SemesterId_Type",
                table: "Invoices",
                columns: new[] { "SemesterId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_CityId",
                table: "Libraries",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_GovernorateId_CityId_Name",
                table: "Libraries",
                columns: new[] { "GovernorateId", "CityId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryBooks_BookId",
                table: "LibraryBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryBooks_LibraryId",
                table: "LibraryBooks",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryBooks_LibraryId_BookId",
                table: "LibraryBooks",
                columns: new[] { "LibraryId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_Date",
                table: "ReceiptVouchers",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_LibraryId_SemesterId",
                table: "ReceiptVouchers",
                columns: new[] { "LibraryId", "SemesterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_SemesterId",
                table: "ReceiptVouchers",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_VoucherYear_VoucherNumber",
                table: "ReceiptVouchers",
                columns: new[] { "VoucherYear", "VoucherNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_AcademicYearId",
                table: "Semesters",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_AcademicYearId_Code",
                table: "Semesters",
                columns: new[] { "AcademicYearId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "InvoiceItems");

            migrationBuilder.DropTable(
                name: "LibraryBooks");

            migrationBuilder.DropTable(
                name: "ReceiptVouchers");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "Libraries");

            migrationBuilder.DropTable(
                name: "Semesters");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "AcademicYears");

            migrationBuilder.DropTable(
                name: "Governorates");
        }
    }
}
