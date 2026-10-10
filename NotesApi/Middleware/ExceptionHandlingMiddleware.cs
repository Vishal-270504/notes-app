using Microsoft.Extensions.Logging;
using NotesApi.Models;

namespace NotesApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Guid.NewGuid().ToString();
        context.Items["TraceId"] = traceId;
        context.Response.Headers["X-Trace-Id"] = traceId;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception TraceId {TraceId}", traceId);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                var error = new ErrorResponse(500, "InternalServerError", "An unexpected error occurred. Please try again later.", traceId);
                await context.Response.WriteAsJsonAsync(error);
            }
        }
    }
}
