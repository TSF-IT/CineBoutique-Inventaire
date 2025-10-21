using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(request => request)
            .NotNull();

        RuleFor(request => request.Code)
            .Cascade(CascadeMode.Stop)
            .Must(code => !string.IsNullOrWhiteSpace(code))
            .WithMessage("Le code est requis.")
            .MaximumLength(32)
            .Matches("^[A-Za-z0-9-_]+$")
            .WithMessage("Le code ne doit contenir que des lettres, chiffres, tirets ou underscores.");

        RuleFor(request => request.Label)
            .Cascade(CascadeMode.Stop)
            .Must(label => !string.IsNullOrWhiteSpace(label))
            .WithMessage("Le libellé est requis.")
            .MaximumLength(128);
    }
}
