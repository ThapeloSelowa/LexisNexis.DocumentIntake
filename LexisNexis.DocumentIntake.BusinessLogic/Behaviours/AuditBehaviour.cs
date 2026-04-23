using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using MediatR;

namespace LexisNexis.DocumentIntake.BusinessLogic.Behaviours
{
    /// <summary>
    /// Automatically records an audit entry for every command that implements IAuditable.
    /// This centralises audit logic — individual handlers don't need to call IAuditService directly.
    /// </summary>
    public class AuditBehaviour<TRequest, TResponse>(IAuditService audit): IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request,RequestHandlerDelegate<TResponse> next,
            CancellationToken ct)
        {
            // Only audit commands that explicitly opt in
            if (request is not IAuditable auditable)
            {
                return await next();
            }

            var response = await next();

            await audit.RecordAsync(
                auditable.DocumentId,
                auditable.AuditEvent,
                ct: ct);

            return response;
        }
    }

    public interface IAuditable
    {
        DocumentId DocumentId { get; }
        AuditEvent AuditEvent { get; }
    }
}
