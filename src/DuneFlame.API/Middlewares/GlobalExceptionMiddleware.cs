using DuneFlame.API.Models;
using DuneFlame.Domain.Exceptions;
using Serilog;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;

namespace DuneFlame.API.Middlewares;

public class GlobalExceptionMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch(Exception ex)
        {
            Log.Error(ex, "An exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = MapExceptionToErrorResponse(exception);

        context.Response.StatusCode = response.StatusCode;

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    private static ErrorResponse MapExceptionToErrorResponse(Exception exception)
    {
        return exception switch
        {
            BadRequestException badRequestEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = badRequestEx.Message,
                Details = null
            },
            NotFoundException notFoundEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.NotFound,
                Message = notFoundEx.Message,
                Details = null
            },
            ConflictException conflictEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.Conflict,
                Message = conflictEx.Message,
                Details = null
            },
            AuthenticationException authEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.Unauthorized,
                Message = authEx.Message,
                Details = null
            },
            _ => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Message = "An unexpected error occurred. Please try again later.",
                Details = exception.Message
            }
        };
    }
}
