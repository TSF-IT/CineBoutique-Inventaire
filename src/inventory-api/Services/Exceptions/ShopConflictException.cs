namespace CineBoutique.Inventory.Api.Services.Exceptions
{
    public sealed class ShopConflictException : ResourceConflictException
    {
        public ShopConflictException()
        {
        }

        public ShopConflictException(string message)
            : base(message)
        {
        }

        public ShopConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
