using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Features.AcademicYears;
using BookDistributionAPI.Features.Semesters;
using BookDistributionAPI.Features.Governorates;
using BookDistributionAPI.Features.Libraries;
using BookDistributionAPI.Features.Books;
using BookDistributionAPI.Features.Invoices;
using BookDistributionAPI.Features.Settings;
using BookDistributionAPI.Features.ReceiptVouchers;
using BookDistributionAPI.Features.Users;

namespace BookDistributionAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<LibraryBook> LibraryBooks => Set<LibraryBook>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ReceiptVoucher> ReceiptVouchers => Set<ReceiptVoucher>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AcademicYear>()
            .Property(a => a.Name)
            .HasMaxLength(20);

        modelBuilder.Entity<AcademicYear>()
            .HasIndex(a => a.Name)
            .IsUnique();

        modelBuilder.Entity<Semester>()
            .Property(s => s.Name)
            .HasMaxLength(50);

        modelBuilder.Entity<Semester>()
            .Property(s => s.Code)
            .HasMaxLength(5);

        modelBuilder.Entity<Semester>()
            .HasIndex(s => new { s.AcademicYearId, s.Code })
            .IsUnique();

        modelBuilder.Entity<Semester>()
            .HasIndex(s => s.AcademicYearId);

        modelBuilder.Entity<Governorate>()
            .Property(g => g.Name)
            .HasMaxLength(100);

        modelBuilder.Entity<Governorate>()
            .HasIndex(g => g.Name)
            .IsUnique();

        modelBuilder.Entity<City>()
            .Property(c => c.Name)
            .HasMaxLength(100);

        modelBuilder.Entity<City>()
            .HasIndex(c => new { c.GovernorateId, c.Name })
            .IsUnique();

        modelBuilder.Entity<City>()
            .HasIndex(c => c.GovernorateId);

        modelBuilder.Entity<Book>()
            .Property(b => b.Name)
            .HasMaxLength(200);

        modelBuilder.Entity<Book>()
            .Property(b => b.Grade)
            .HasMaxLength(100);

        modelBuilder.Entity<Book>()
            .Property(b => b.Subject)
            .HasMaxLength(100);

        modelBuilder.Entity<Book>()
            .Property(b => b.Price)
            .HasColumnType("decimal(10,3)");

        modelBuilder.Entity<Book>()
            .HasIndex(b => new { b.SemesterId, b.Name, b.Grade })
            .IsUnique();

        modelBuilder.Entity<Book>()
            .HasIndex(b => b.SemesterId);

        modelBuilder.Entity<Book>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_Books_Price_NonNegative", "[Price] >= 0");
                t.HasCheckConstraint("CK_Books_StockQuantity_NonNegative", "[StockQuantity] >= 0");
            });

        modelBuilder.Entity<Library>()
            .Property(l => l.Name)
            .HasMaxLength(200);

        modelBuilder.Entity<Library>()
            .Property(l => l.Logo)
            .HasMaxLength(500);

        modelBuilder.Entity<Library>()
            .Property(l => l.OwnerName)
            .HasMaxLength(100);

        modelBuilder.Entity<Library>()
            .Property(l => l.OwnerPhone)
            .HasMaxLength(30);

        modelBuilder.Entity<Library>()
            .Property(l => l.ResponsibleName)
            .HasMaxLength(100);

        modelBuilder.Entity<Library>()
            .Property(l => l.ResponsiblePhone)
            .HasMaxLength(30);

        modelBuilder.Entity<Library>()
            .Property(l => l.LandlinePhone)
            .HasMaxLength(30);

        modelBuilder.Entity<Library>()
            .Property(l => l.Shift1Start)
            .HasMaxLength(5);

        modelBuilder.Entity<Library>()
            .Property(l => l.Shift1End)
            .HasMaxLength(5);

        modelBuilder.Entity<Library>()
            .Property(l => l.Shift2Start)
            .HasMaxLength(5);

        modelBuilder.Entity<Library>()
            .Property(l => l.Shift2End)
            .HasMaxLength(5);

        modelBuilder.Entity<Library>()
            .Property(l => l.ResponseRating)
            .HasMaxLength(20);

        modelBuilder.Entity<Library>()
            .Property(l => l.PaymentRating)
            .HasMaxLength(20);

        modelBuilder.Entity<Library>()
            .Property(l => l.Notes)
            .HasMaxLength(1000);

        modelBuilder.Entity<Library>()
            .HasIndex(l => new { l.GovernorateId, l.CityId, l.Name });

        modelBuilder.Entity<Library>()
            .HasQueryFilter(l => l.IsActive);

        modelBuilder.Entity<LibraryBook>()
            .ToTable(t => t.HasCheckConstraint("CK_LibraryBooks_Quantity_NonNegative", "[Quantity] >= 0"));

        modelBuilder.Entity<Invoice>()
            .Property(i => i.TermCode)
            .HasMaxLength(5);

        modelBuilder.Entity<Invoice>()
            .Property(i => i.Type)
            .HasMaxLength(20);

        modelBuilder.Entity<Invoice>()
            .Property(i => i.PrintStatus)
            .HasMaxLength(20);

        modelBuilder.Entity<Invoice>()
            .Property(i => i.ResponsibleName)
            .HasMaxLength(100);

        modelBuilder.Entity<Invoice>()
            .Property(i => i.ResponsiblePhone)
            .HasMaxLength(30);

        modelBuilder.Entity<Invoice>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_Invoices_Type", "[Type] IN ('order', 'refund', 'clearance')");
                t.HasCheckConstraint("CK_Invoices_PrintStatus", "[PrintStatus] IN ('pending', 'printed')");
                t.HasCheckConstraint("CK_Invoices_TotalAmount_NonNegative", "[TotalAmount] >= 0");
            });

        modelBuilder.Entity<InvoiceItem>()
            .Property(ii => ii.BookName)
            .HasMaxLength(200);

        modelBuilder.Entity<InvoiceItem>()
            .Property(ii => ii.BookGrade)
            .HasMaxLength(100);

        modelBuilder.Entity<InvoiceItem>()
            .Property(ii => ii.UnitPrice)
            .HasColumnType("decimal(10,3)");

        modelBuilder.Entity<InvoiceItem>()
            .Property(ii => ii.Total)
            .HasColumnType("decimal(10,3)");

        modelBuilder.Entity<Invoice>()
            .Property(i => i.TotalAmount)
            .HasColumnType("decimal(10,3)");

        modelBuilder.Entity<InvoiceItem>()
            .HasIndex(ii => ii.BookId);

        modelBuilder.Entity<InvoiceItem>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_InvoiceItems_Quantity_Positive", "[Quantity] > 0");
                t.HasCheckConstraint("CK_InvoiceItems_UnitPrice_NonNegative", "[UnitPrice] >= 0");
                t.HasCheckConstraint("CK_InvoiceItems_Total_NonNegative", "[Total] >= 0");
            });

        modelBuilder.Entity<AppSetting>()
            .Property(s => s.Key)
            .HasMaxLength(100);

        modelBuilder.Entity<AppSetting>()
            .Property(s => s.Value)
            .HasMaxLength(4000);

        modelBuilder.Entity<AppSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        modelBuilder.Entity<Invoice>()
            .Ignore(i => i.DisplayNumber);

        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(i => i.IsActive);

        modelBuilder.Entity<Semester>()
            .HasOne(s => s.AcademicYear)
            .WithMany(a => a.Semesters)
            .HasForeignKey(s => s.AcademicYearId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<City>()
            .HasOne(c => c.Governorate)
            .WithMany(g => g.Cities)
            .HasForeignKey(c => c.GovernorateId);

        modelBuilder.Entity<Library>()
            .HasOne(l => l.Governorate)
            .WithMany()
            .HasForeignKey(l => l.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Library>()
            .HasOne(l => l.City)
            .WithMany()
            .HasForeignKey(l => l.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Book>()
            .HasQueryFilter(b => b.IsActive);

        modelBuilder.Entity<LibraryBook>()
            .HasQueryFilter(lb => lb.Book!.IsActive);
        modelBuilder.Entity<InvoiceItem>()
            .HasQueryFilter(ii => ii.Book!.IsActive);

        modelBuilder.Entity<Book>()
            .HasOne(b => b.Semester)
            .WithMany(s => s.Books)
            .HasForeignKey(b => b.SemesterId);

        modelBuilder.Entity<LibraryBook>()
            .HasOne(lb => lb.Library)
            .WithMany(l => l.LibraryBooks)
            .HasForeignKey(lb => lb.LibraryId);

        modelBuilder.Entity<LibraryBook>()
            .HasOne(lb => lb.Book)
            .WithMany(b => b.LibraryBooks)
            .HasForeignKey(lb => lb.BookId);

        modelBuilder.Entity<LibraryBook>()
            .HasIndex(lb => new { lb.LibraryId, lb.BookId })
            .IsUnique();

        modelBuilder.Entity<LibraryBook>()
            .HasIndex(lb => lb.LibraryId);

        modelBuilder.Entity<LibraryBook>()
            .HasIndex(lb => lb.BookId);

        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.Library)
            .WithMany(l => l.Invoices)
            .HasForeignKey(i => i.LibraryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.Semester)
            .WithMany(s => s.Invoices)
            .HasForeignKey(i => i.SemesterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Invoice)
            .WithMany(i => i.Items)
            .HasForeignKey(ii => ii.InvoiceId);

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Book)
            .WithMany()
            .HasForeignKey(ii => ii.BookId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InvoiceItem>()
            .HasIndex(ii => ii.InvoiceId);

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.LibraryId, i.SemesterId, i.InvoiceNumber })
            .IsUnique();

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.LibraryId, i.InvoiceYear, i.InvoiceNumber });

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.LibraryId, i.SemesterId });

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => i.SemesterId);

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.SemesterId, i.Type });

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => i.Date);

        // ---- ReceiptVoucher ----
        modelBuilder.Entity<ReceiptVoucher>()
            .Ignore(rv => rv.DisplayNumber);

        modelBuilder.Entity<ReceiptVoucher>()
            .HasQueryFilter(rv => rv.IsActive);

        modelBuilder.Entity<ReceiptVoucher>()
            .Property(rv => rv.Amount)
            .HasColumnType("decimal(10,3)");

        modelBuilder.Entity<ReceiptVoucher>()
            .Property(rv => rv.PaymentMethod)
            .HasMaxLength(20);

        modelBuilder.Entity<ReceiptVoucher>()
            .Property(rv => rv.ChequeNumber)
            .HasMaxLength(50);

        modelBuilder.Entity<ReceiptVoucher>()
            .Property(rv => rv.BankName)
            .HasMaxLength(100);

        modelBuilder.Entity<ReceiptVoucher>()
            .Property(rv => rv.Purpose)
            .HasMaxLength(500);

        modelBuilder.Entity<ReceiptVoucher>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_ReceiptVouchers_Amount_Positive", "[Amount] > 0");
                t.HasCheckConstraint("CK_ReceiptVouchers_PaymentMethod", "[PaymentMethod] IN ('cash', 'cheque')");
            });

        modelBuilder.Entity<ReceiptVoucher>()
            .HasIndex(rv => new { rv.LibraryId, rv.SemesterId });

        modelBuilder.Entity<ReceiptVoucher>()
            .HasOne(rv => rv.Library)
            .WithMany()
            .HasForeignKey(rv => rv.LibraryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReceiptVoucher>()
            .HasOne(rv => rv.Semester)
            .WithMany()
            .HasForeignKey(rv => rv.SemesterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReceiptVoucher>()
            .HasIndex(rv => new { rv.VoucherYear, rv.VoucherNumber })
            .IsUnique();

        modelBuilder.Entity<ReceiptVoucher>()
            .HasIndex(rv => rv.Date);

        // ---- User ----
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.Username)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasMaxLength(50);

        modelBuilder.Entity<User>()
            .Property(u => u.PasswordHash)
            .HasMaxLength(500);
    }
}
