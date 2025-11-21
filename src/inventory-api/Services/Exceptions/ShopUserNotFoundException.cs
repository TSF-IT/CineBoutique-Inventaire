namespace CineBoutique.Inventory.Api.Services.Exceptions
{
    public sealed class ShopUserNotFoundException : ResourceNotFoundException
    {
        public ShopUserNotFoundException()
            : base("L'utilisateur demandé est introuvable dans cette boutique.")
        {
        }

        public ShopUserNotFoundException(string message)
            : base(message)
        {
        }

        public ShopUserNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
