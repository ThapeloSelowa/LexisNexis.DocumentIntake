using FluentValidation;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Behaviours
{
    // <summary>
    /// Runs FluentValidation validators before the handler is called.
    /// If any validator fails, a ValidationException is thrown and the handler never runs.
    /// The ExceptionMiddleware catches this and returns a 422 response with all error messages.
    /// </summary>
    public class ValidationBehaviour<TRequest, TResponse>(
        IEnumerable<IValidator<TRequest>> validators)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken ct)
        {
            if (!validators.Any()) return await next();

            var context = new ValidationContext<TRequest>(request);

            var failures = validators
                .Select(v => v.Validate(context))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
                
            return await next();
        }
    }
}
