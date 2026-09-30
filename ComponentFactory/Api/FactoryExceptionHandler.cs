using ComponentFactory.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace ComponentFactory.Api;

public sealed class FactoryExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, response) = MapError(exception);
        context.Response.StatusCode = status;

        await context.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }

    private static (int Status, ErrorResponse Response) MapError(Exception exception)
    {
        if (exception is ComponentFactoryException factoryException)
        {
            return (
                GetStatusCode(factoryException.Error),
                new ErrorResponse(
                    factoryException.Message,
                    factoryException.ProjectUrl,
                    factoryException.Stage));
        }

        if (exception is OperationCanceledException)
        {
            return (
                StatusCodes.Status504GatewayTimeout,
                new ErrorResponse("Operation cancelled or timed out before project creation."));
        }

        return (
            StatusCodes.Status502BadGateway,
            new ErrorResponse("Generation failed before project creation. Check GitLab access and template configuration."));
    }

    private static int GetStatusCode(FactoryError error)
    {
        return error switch
        {
            FactoryError.InvalidName or FactoryError.InvalidTemplate => StatusCodes.Status400BadRequest,
            FactoryError.ProjectAlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status502BadGateway
        };
    }

    private sealed record ErrorResponse(
        string Error,
        string? ProjectUrl = null,
        string? Stage = null);
}
