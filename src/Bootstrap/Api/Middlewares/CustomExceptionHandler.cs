using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Middlewares;

public class CustomExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = exception switch
        {
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        // Conflito de concorrência: outra requisição gravou o mesmo recurso depois da leitura. Nada
        // foi gravado nem publicado; o cliente pode repetir a chamada e receberá o resultado
        // conforme o novo estado do recurso.
        var problemDetails = exception is DbUpdateConcurrencyException
            ? new ProblemDetails
            {
                Title = "Conflito de concorrência",
                Type = exception.GetType().Name,
                Detail = "O recurso foi alterado por outra operação ao mesmo tempo. Tente novamente."
            }
            : new ProblemDetails
            {
                Title = "An error occurred",
                Type = exception.GetType().Name,
                Detail = exception.Message
            };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });
    }
}
