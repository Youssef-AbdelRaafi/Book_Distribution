using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using BookDistributionAPI.Features.Auth;
using BookDistributionAPI.Features.AcademicYears;
using BookDistributionAPI.Features.Analytics;
using BookDistributionAPI.Features.Books;
using BookDistributionAPI.Features.Governorates;
using BookDistributionAPI.Features.Invoices;
using BookDistributionAPI.Features.Libraries;
using BookDistributionAPI.Features.ReceiptVouchers;
using BookDistributionAPI.Features.Semesters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDistributionAPI.Tests;

public sealed class FinancialIntegrityTests
{
    [Fact]
    public async Task ReceiptVoucher_RejectsPaymentAboveOutstandingBalance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.AddInvoiceDataAsync();
        var service = new ReceiptVoucherBusinessService(database.Context, new AcademicYearHelper(database.Context));

        var firstVoucher = await service.CreateAsync(new CreateReceiptVoucherDto
        {
            LibraryId = data.Library.Id,
            SemesterId = data.Semester.Id,
            Amount = 60m,
            PaymentMethod = "cash",
            Purpose = "partial payment",
            Date = new DateTime(2026, 8, 1)
        });

        Assert.Equal(60m, firstVoucher.Amount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreateReceiptVoucherDto
        {
            LibraryId = data.Library.Id,
            SemesterId = data.Semester.Id,
            Amount = 40.001m,
            PaymentMethod = "cash",
            Purpose = "overpayment",
            Date = new DateTime(2026, 8, 1)
        }));

        Assert.Equal(1, await database.Context.ReceiptVouchers.CountAsync());
    }

    [Fact]
    public async Task Dashboard_DoesNotCountUnassignedVoucherInSemesterTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.AddInvoiceDataAsync();

        database.Context.ReceiptVouchers.AddRange(
            new ReceiptVoucher
            {
                VoucherNumber = 1,
                VoucherYear = 2026,
                LibraryId = data.Library.Id,
                LibraryName = data.Library.Name,
                SemesterId = data.Semester.Id,
                Amount = 25m,
                PaymentMethod = "cash",
                Purpose = "term payment",
                Date = new DateTime(2026, 8, 1)
            },
            new ReceiptVoucher
            {
                VoucherNumber = 2,
                VoucherYear = 2026,
                LibraryId = data.Library.Id,
                LibraryName = data.Library.Name,
                SemesterId = null,
                Amount = 75m,
                PaymentMethod = "cash",
                Purpose = "unassigned legacy payment",
                Date = new DateTime(2026, 8, 1)
            });
        await database.Context.SaveChangesAsync();

        var controller = new AnalyticsController(database.Context);
        var action = await controller.GetDashboard(data.Semester.Id, null, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action);
        var response = Assert.IsType<ApiResponse<DashboardAnalyticsDto>>(result.Value);
        Assert.NotNull(response.Data);
        Assert.Equal(25m, response.Data.TotalCollected);
        Assert.Equal(75m, response.Data.TotalOutstanding);
    }

    [Fact]
    public async Task Clearance_RequiresFullSettlementBeforeClosingLibraryAccount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.AddInvoiceDataAsync();
        var invoiceService = new InvoiceBusinessService(database.Context);
        var receiptService = new ReceiptVoucherBusinessService(database.Context, new AcademicYearHelper(database.Context));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoiceService.CreateClearanceAsync(data.Library.Id, data.Semester.Id));

        await receiptService.CreateAsync(new CreateReceiptVoucherDto
        {
            LibraryId = data.Library.Id,
            SemesterId = data.Semester.Id,
            Amount = 100m,
            PaymentMethod = "cash",
            Purpose = "final payment",
            Date = new DateTime(2026, 8, 1)
        });

        var clearance = await invoiceService.CreateClearanceAsync(data.Library.Id, data.Semester.Id);

        Assert.Equal("clearance", clearance.Type);
        Assert.Equal(100m, clearance.TotalAmount);
        Assert.Single(clearance.Items);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async Task<(Library Library, Semester Semester)> AddInvoiceDataAsync()
        {
            var year = new AcademicYear { Name = "2026-2027", IsActive = true };
            Context.AcademicYears.Add(year);
            await Context.SaveChangesAsync();

            var semester = new Semester
            {
                AcademicYearId = year.Id,
                Name = "Term A",
                Code = "A",
                IsActive = true
            };
            var governorate = new Governorate { Name = "Governorate" };
            Context.AddRange(semester, governorate);
            await Context.SaveChangesAsync();

            var city = new City { Name = "City", GovernorateId = governorate.Id };
            Context.Cities.Add(city);
            await Context.SaveChangesAsync();

            var library = new Library
            {
                Name = "Library",
                GovernorateId = governorate.Id,
                CityId = city.Id,
                IsActive = true
            };
            Context.Libraries.Add(library);
            await Context.SaveChangesAsync();

            var book = new Book
            {
                Name = "Book",
                Grade = "Grade",
                Subject = "Subject",
                SemesterId = semester.Id,
                Price = 100m,
                StockQuantity = 10,
                IsActive = true
            };
            Context.Books.Add(book);
            await Context.SaveChangesAsync();

            Context.Invoices.Add(new Invoice
            {
                InvoiceNumber = 1,
                InvoiceYear = 2026,
                TermCode = "A",
                Type = "order",
                LibraryId = library.Id,
                LibraryName = library.Name,
                SemesterId = semester.Id,
                Date = new DateTime(2026, 8, 1),
                TotalAmount = 100m,
                PrintStatus = "pending",
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        BookId = book.Id,
                        BookName = book.Name,
                        BookGrade = book.Grade,
                        Quantity = 1,
                        UnitPrice = book.Price,
                        Total = book.Price
                    }
                }
            });
            await Context.SaveChangesAsync();

            return (library, semester);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
