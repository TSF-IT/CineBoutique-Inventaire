using System.Linq;
using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class CreateShopUserRequestValidator : AbstractValidator<CreateShopUserRequest>
{
    public CreateShopUserRequestValidator()
    {
        RuleFor(request => request.Login)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Le login est obligatoire.")
            .Must(login => login.Trim().Length > 0).WithMessage("Le login ne peut pas être vide.")
            .Must(HasNoWhitespace).WithMessage("Le login ne peut pas contenir d'espaces.")
            .MaximumLength(128).WithMessage("Le login ne peut pas dépasser 128 caractères.");

        RuleFor(request => request.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Le nom d'affichage est obligatoire.")
            .Must(name => name.Trim().Length > 0).WithMessage("Le nom d'affichage ne peut pas être vide.")
            .MaximumLength(256).WithMessage("Le nom d'affichage ne peut pas dépasser 256 caractères.");
    }

    private static bool HasNoWhitespace(string value) => value.All(ch => !char.IsWhiteSpace(ch));
}
