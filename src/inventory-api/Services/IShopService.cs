using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services;

public interface IShopService
{
    Task<IReadOnlyList<ShopDto>> GetAsync(string? kind, CancellationToken cancellationToken);

    Task<ShopDto> CreateAsync(CreateShopRequest request, CancellationToken cancellationToken);

    Task<ShopDto> UpdateAsync(UpdateShopRequest request, CancellationToken cancellationToken);

    Task<ShopDto> DeleteAsync(DeleteShopRequest request, CancellationToken cancellationToken);
}
