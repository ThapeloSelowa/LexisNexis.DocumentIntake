
using System.ComponentModel;

namespace LexisNexis.DocumentIntake.BusinessLogic.Models
{
    /// <summary>
    /// Standard error envelope returned for ALL error responses.
    /// Having a consistent error shape makes it easy for API consumers
    /// to handle errors generically.
    /// </summary>
    public record ErrorResponse
    {
        /// <example>a3f2b1c4d5e6...</example>
        [Description("Unique ID for this request. Use this when reporting errors to the team.")]
        public required string TransactionId { get; init; }

        /// <example>422</example>
        public required int Status { get; init; }

        /// <example>Validation Failed</example>
        public required string Title { get; init; }

        [Description("Human-readable explanation of the error.")]
        public string? Detail { get; init; }

        [Description("List of validation error messages. Present only on 422 responses.")]
        public List<string>? Errors { get; init; }
    }
}
