using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace MetroRail.Common;

/// <summary>
/// One error shape for every service:
/// {"error": {"code": "ticket_not_found", "message": "...", "request_id": "..."}}
/// </summary>
public class AppException(string message, string code = "bad_request", int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public object? Details { get; init; }
}

public sealed class NotFoundException(string message, string code = "not_found") : AppException(message, code, 404);
public sealed class ConflictException(string message, string code = "conflict") : AppException(message, code, 409);
public sealed class UnauthorizedException(string message, string code = "unauthorized") : AppException(message, code, 401);
public sealed class ForbiddenException(string message, string code = "forbidden") : AppException(message, code, 403);

public sealed class ValidationException(string message, object? details = null)
    : AppException(message, "validation_error", 422)
{
    public new object? Details { get; } = details;
}

/// <summary>Small, explicit request validation (same rules the Python version enforced).</summary>
public static class Check
{
    public static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException($"{field} is required", new[] { field }) : value;

    public static string Matches(string? value, string pattern, string field, string? message = null)
    {
        if (value is null || !Regex.IsMatch(value, pattern))
            throw new ValidationException(message ?? $"{field} is not valid", new[] { field });
        return value;
    }

    public static int Range(int value, int min, int max, string field) =>
        value < min || value > max ? throw new ValidationException($"{field} must be between {min} and {max}", new[] { field }) : value;

    public static void That(bool ok, string field, string message)
    {
        if (!ok) throw new ValidationException(message, new[] { field });
    }
}

internal static class ErrorMiddleware
{
    public static async Task Handle(HttpContext ctx, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (AppException e)
        {
            var details = e is ValidationException v ? v.Details : e.Details;
            await Write(ctx, e.Status, e.Code, e.Message, details);
        }
        catch (BadHttpRequestException e)
        {
            await Write(ctx, 422, "validation_error", "Request is not valid", new[] { e.Message });
        }
        catch (JsonException e)
        {
            await Write(ctx, 422, "validation_error", "Request is not valid", new[] { e.Message });
        }
    }

    public static Task Write(HttpContext ctx, int status, string code, string message, object? details = null)
    {
        if (ctx.Response.HasStarted) return Task.CompletedTask;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        var err = new Dictionary<string, object?>
        {
            ["code"] = code, ["message"] = message, ["request_id"] = ctx.TraceIdentifier,
        };
        if (details is not null) err["details"] = details;
        return ctx.Response.WriteAsJsonAsync(new { error = err }, Json.Options);
    }
}
