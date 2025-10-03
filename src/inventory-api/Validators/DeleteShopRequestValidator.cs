using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class DeleteShopRequestValidator : AbstractValidator<DeleteShopRequest>
{
    public DeleteShopRequestValidator()
    {
        RuleFor(request => request.Id)
            .NotEmpty().WithMessage("L'identifiant de la boutique est requis.");
    }
}
