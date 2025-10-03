using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class DeleteShopUserRequestValidator : AbstractValidator<DeleteShopUserRequest>
{
    public DeleteShopUserRequestValidator()
    {
        RuleFor(request => request.Id)
            .NotEmpty().WithMessage("L'identifiant de l'utilisateur est requis.");
    }
}
