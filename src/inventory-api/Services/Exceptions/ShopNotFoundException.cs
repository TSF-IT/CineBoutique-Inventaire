namespace CineBoutique.Inventory.Api.Services.Exceptions
{
    public sealed class ShopNotFoundException : ResourceNotFoundException
    {
        public ShopNotFoundException()
            : base("La boutique demandée est introuvable.")
        {
        }

        public ShopNotFoundException(string message)
            : base(message)
        {
        }

        public ShopNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
