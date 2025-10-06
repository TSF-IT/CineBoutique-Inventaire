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
            .GreaterThanOrEqualTo((short)1)
            .WithMessage("countType doit être supérieur ou égal à 1.");
    }
}
