using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BookDistributionAPI.Common;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(0, ex, "Business rule violation: {Message}", ex.Message);
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(1, ex, "Database constraint violation: {Message}", ex.InnerException?.Message ?? ex.Message);
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "البيانات المرسلة غير صحيحة أو مرتبطة بسجلات أخرى");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled API exception.");
            var message = _environment.IsDevelopment()
                ? ex.Message
                : "حدث خطأ غير متوقع. الرجاء المحاولة مرة أخرى";
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, message);
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = ApiResponse<object>.Fail(message);
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
