using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Data;
using BookDistributionAPI.Common;
using BookDistributionAPI.Features.Auth;
using BookDistributionAPI.Features.Invoices;
using BookDistributionAPI.Features.ReceiptVouchers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // Local Docker
        options.SaveToken = false;
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
        policy.WithOrigins("http://localhost:4200", "http://localhost:4300")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<InvoiceBusinessService>();
builder.Services.AddScoped<ReceiptVoucherBusinessService>();
builder.Services.AddScoped<IAcademicYearHelper, AcademicYearHelper>();

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
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    if (!await db.AcademicYears.AnyAsync())
        await SeedData.InitializeAsync(db);
}

app.UseMiddleware<ApiExceptionMiddleware>();

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

app.UseCors("AllowAngularDev");

app.UseAuthentication();
app.UseAuthorization();
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
