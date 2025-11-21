namespace CineBoutique.Inventory.Api.Models
{
    public sealed class ConflictRunQtyDto
    {
        public Guid RunId { get; set; }

        public short CountType { get; set; }

        public int Quantity { get; set; }
    }
}
