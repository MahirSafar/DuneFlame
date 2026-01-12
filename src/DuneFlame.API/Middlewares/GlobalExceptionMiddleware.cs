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
        catch (Exception ex)
        {
            // Loglama məntiqini ayırırıq
            LogException(ex);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static void LogException(Exception ex)
    {
        // Əgər istifadəçi xətasıdırsa (400, 404, 409), bunu Warning kimi yaz (Stack Trace lazım deyil)
        if (ex is BadRequestException || ex is NotFoundException || ex is ConflictException || ex is AuthenticationException)
        {
            Log.Warning("Business Logic Error: {Message}", ex.Message);
        }
        else
        {
            // Əgər sistem xətasıdırsa (500), bunu Error kimi yaz (Stack Trace lazımdır)
            Log.Error(ex, "System Failure: {Message}", ex.Message);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = MapExceptionToErrorResponse(exception);

        context.Response.StatusCode = response.StatusCode;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
    }

    private static ErrorResponse MapExceptionToErrorResponse(Exception exception)
    {
        return exception switch
        {
            BadRequestException badRequestEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = badRequestEx.Message
            },
            NotFoundException notFoundEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.NotFound,
                Message = notFoundEx.Message
            },
            ConflictException conflictEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.Conflict,
                Message = conflictEx.Message
            },
            AuthenticationException authEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.Unauthorized,
                Message = authEx.Message
            },
            _ => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Message = "An unexpected error occurred.",
                Details = exception.Message // Production-da bunu gizlətmək olar
            }
        };
    }
}