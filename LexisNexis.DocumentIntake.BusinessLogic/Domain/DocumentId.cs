namespace LexisNexis.DocumentIntake.BusinessLogic.Domain
{
    /// <summary>
    /// Strongly typed document identifier.
    /// Using a wrapper instead of raw Guid to prevent accidentally passing the wrong
    /// Guid to the wrong method.
    /// </summary>
    public readonly record struct DocumentId(Guid Value)
    {
        public static DocumentId New() => new(Guid.NewGuid());
        public static DocumentId Parse(string value) => new(Guid.Parse(value));
        public override string ToString() => Value.ToString();
    }
}
