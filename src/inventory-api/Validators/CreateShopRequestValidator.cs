using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class CreateShopRequestValidator : AbstractValidator<CreateShopRequest>
{
    public CreateShopRequestValidator()
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Le nom de la boutique est obligatoire.")
            .Must(name => name.Trim().Length > 0).WithMessage("Le nom de la boutique ne peut pas être vide.")
            .MaximumLength(256).WithMessage("Le nom de la boutique ne peut pas dépasser 256 caractères.");
    }
}
