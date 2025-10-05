using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class StartRunRequestValidator : AbstractValidator<StartRunRequest>
{
    public StartRunRequestValidator()
    {
        RuleFor(request => request.ShopId)
            .NotEmpty();

        RuleFor(request => request.OwnerUserId)
            .NotEmpty();

        RuleFor(request => request.CountType)
            .Must(countType => countType is 1 or 2 or 3)
            .WithMessage("countType doit valoir 1, 2 ou 3.");
    }
}
