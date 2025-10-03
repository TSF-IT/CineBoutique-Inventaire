using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services;

public interface IShopUserService
{
    Task<IReadOnlyList<ShopUserDto>> GetAsync(Guid shopId, CancellationToken cancellationToken);

    Task<ShopUserDto> CreateAsync(Guid shopId, CreateShopUserRequest request, CancellationToken cancellationToken);

    Task<ShopUserDto> UpdateAsync(Guid shopId, UpdateShopUserRequest request, CancellationToken cancellationToken);

    Task<ShopUserDto> SoftDeleteAsync(Guid shopId, DeleteShopUserRequest request, CancellationToken cancellationToken);
}
