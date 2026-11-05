using DistrictSync.Application.Common;
using DistrictSync.Application.Rules.Dsl;
using DistrictSync.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Api.Middleware;

/// <summary>Maps domain and application exceptions to RFC 7807 problem details with stable status codes.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment environment, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail, extensions) = exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, "Validation failed", v.Message, new Dictionary<string, object?> { ["errors"] = v.Errors }),
            RuleSyntaxException r => (StatusCodes.Status400BadRequest, "Invalid rule condition", r.Message, new Dictionary<string, object?> { ["position"] = r.Position }),
            NotFoundException n => (StatusCodes.Status404NotFound, "Not found", n.Message, null),
            TenantIsolationViolationException => (StatusCodes.Status404NotFound, "Not found", "The requested resource was not found.", null),
            ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden", f.Message, null),
            ConcurrencyConflictException c => (StatusCodes.Status412PreconditionFailed, "Precondition failed", c.Message, null),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent modification", "The resource was modified by another request. Reload and retry.", null),
            InvalidStateTransitionException s => (StatusCodes.Status409Conflict, "Invalid state transition", s.Message, new Dictionary<string, object?> { ["entity"] = s.Entity, ["state"] = s.From, ["action"] = s.Action }),
            DomainException d => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", d.Message, new Dictionary<string, object?> { ["code"] = d.Code }),
            BadHttpRequestException b => (b.StatusCode, "Bad request", b.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", environment.IsDevelopment() ? exception.ToString() : "An unexpected error occurred.", null),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogDebug(exception, "Request {Method} {Path} rejected with {Status}", httpContext.Request.Method, httpContext.Request.Path, status);
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
