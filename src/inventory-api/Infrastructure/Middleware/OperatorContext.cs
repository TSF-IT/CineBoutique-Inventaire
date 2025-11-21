namespace CineBoutique.Inventory.Api.Infrastructure.Middleware
{
    internal sealed class OperatorContext
    {
        public OperatorContext(Guid operatorId, string? operatorName, string? sessionId)
        {
            OperatorId = operatorId;
            OperatorName = operatorName;
            SessionId = sessionId;
        }

        public Guid OperatorId { get; }

        public string? OperatorName { get; }

        public string? SessionId { get; }
    }
}
