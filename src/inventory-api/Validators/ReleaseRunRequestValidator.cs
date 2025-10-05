using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class ReleaseRunRequestValidator : AbstractValidator<ReleaseRunRequest>
{
    public ReleaseRunRequestValidator()
    {
        RuleFor(request => request.RunId)
            .NotEmpty();

        RuleFor(request => request.OwnerUserId)
            .NotEmpty();
    }
}
