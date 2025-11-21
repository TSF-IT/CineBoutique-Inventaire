namespace CineBoutique.Inventory.Api.Models
{
    public sealed class SessionConflictObservationDto
    {
        public Guid RunId { get; set; }

        public short CountType { get; set; }

        public int Quantity { get; set; }

        public string? CountedBy { get; set; }

        public DateTimeOffset CountedAtUtc { get; set; }
    }

    public sealed class SessionConflictItemDto
    {
        public Guid ProductId { get; set; }

        public string ProductRef { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public IReadOnlyList<SessionConflictObservationDto> Observations { get; set; } = [];

        public double? SampleVariance { get; set; }

        public int? ResolvedQuantity { get; set; }
    }

    public sealed class SessionConflictsResponseDto
    {
        public Guid SessionId { get; set; }

        public IReadOnlyList<SessionConflictItemDto> Items { get; set; } = [];
    }

    public sealed class SessionResolvedConflictDto
    {
        public Guid ProductId { get; set; }

        public string ProductRef { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int ResolvedQuantity { get; set; }

        public string ResolutionRule { get; set; } = string.Empty;

        public DateTimeOffset ResolvedAtUtc { get; set; }
    }

    public sealed class SessionResolvedConflictsResponseDto
    {
        public Guid SessionId { get; set; }

        public IReadOnlyList<SessionResolvedConflictDto> Items { get; set; } = [];
    }
}
