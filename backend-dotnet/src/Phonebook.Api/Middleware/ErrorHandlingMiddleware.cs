using System.Text.Json;
using Phonebook.Api.Exceptions;

namespace Phonebook.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsUnsupportedRequestBodyContentType(context))
        {
            await WriteErrorAsync(context, StatusCodes.Status415UnsupportedMediaType, "Content-Type must be application/json.");
            return;
        }

        try
        {
            await _next(context);
        }
        catch (ContactNotFoundException)
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound, "Contact not found.");
        }
        catch (DuplicateContactException exception)
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, exception.Message);
        }
        catch (InvalidContactException exception)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected API error");
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.");
        }
    }

    private static bool IsUnsupportedRequestBodyContentType(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method))
        {
            return false;
        }

        string mediaType = context.Request.ContentType?.Split(';')[0].Trim() ?? string.Empty;
        return !mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { detail }), context.RequestAborted);
    }
}