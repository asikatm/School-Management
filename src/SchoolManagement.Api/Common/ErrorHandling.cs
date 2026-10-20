using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Infrastructure.Common;

namespace SchoolManagement.Api.Common;

/// <summary>Maps exceptions to RFC 7807 problem responses; unexpected errors never leak details.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message, "not_found"),
            ForbiddenException => (StatusCodes.Status403Forbidden, exception.Message, "forbidden"),
            DomainException d => (d.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest, d.Message, d.Code),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was changed by someone else. Reload and try again.", "concurrency"),
            DbUpdateException => (StatusCodes.Status409Conflict, "The change conflicts with existing data (for example a duplicate number).", "db_conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server_error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception");
        else if (exception is DbUpdateException)
            logger.LogWarning(exception, "Database update rejected");

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code, ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }
}

/// <summary>Runs the FluentValidation validator for every action argument that has one.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest });
            return;
        }
        await next();
    }
}
