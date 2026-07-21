using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Data;
using BookDistributionAPI.Common;
using BookDistributionAPI.Features.Auth;
using BookDistributionAPI.Features.Invoices;
using BookDistributionAPI.Features.ReceiptVouchers;
using BookDistributionAPI.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "BookDistributionAPI")
    .CreateLogger();

builder.Host.UseSerilog();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
authOptions.Validate();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !isDevelopment;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = authOptions.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<InvoiceBusinessService>();
builder.Services.AddScoped<ReceiptVoucherBusinessService>();
builder.Services.AddScoped<IAcademicYearHelper, AcademicYearHelper>();
builder.Services.AddSingleton<LoginRateLimiter>();

builder.Services.AddControllers(options =>
    {
        options.Filters.Add(new AuthorizeFilter());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "قيمة غير صحيحة" : e.ErrorMessage);

        return new BadRequestObjectResult(ApiResponse<object>.Fail(string.Join(" - ", errors)));
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Enable WAL mode for better concurrent read performance
    try { await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;"); }
    catch { /* WAL mode may not be supported on all systems */ }
    await db.Database.MigrateAsync();

    if (!await db.AcademicYears.AnyAsync())
        await SeedData.InitializeAsync(db, scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

    await SeedData.SeedGuestDemoDataAsync(db);
}

app.UseMiddleware<ApiExceptionMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    await next();
});

// Serving Angular SPA
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
            ctx.Context.Response.Headers.Append("Expires", "-1");
        }
        else
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
        }
    }
});

var dataRoot = builder.Configuration["App:DataDirectory"]
    ?? Environment.GetEnvironmentVariable("APP_DATA_DIR")
    ?? Path.Combine(app.Environment.ContentRootPath, "data");
var uploadsRoot = builder.Configuration["App:UploadsPath"]
    ?? Environment.GetEnvironmentVariable("APP_UPLOADS_DIR")
    ?? Path.Combine(dataRoot, "uploads");
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
        ctx.Context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    }
});

app.UseCors("AllowAngularDev");

app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }))
    .AllowAnonymous();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    // Utility for hashing passwords (only accessible locally or for setup)
    app.MapGet("/api/setup/hash", (string password) =>
    {
        byte[] salt = new byte[128 / 8];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000,
            numBytesRequested: 256 / 8));
        return $"pbkdf2-sha256$100000${Convert.ToBase64String(salt)}${hashed}";
    }).AllowAnonymous();
}

app.MapFallbackToFile("index.html");

app.Run();
