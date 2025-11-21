namespace CineBoutique.Inventory.Api.Services.Exceptions
{
    public class ResourceConflictException : Exception
    {
        public ResourceConflictException()
        {
        }

        public ResourceConflictException(string message)
            : base(message)
        {
        }

        public ResourceConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
