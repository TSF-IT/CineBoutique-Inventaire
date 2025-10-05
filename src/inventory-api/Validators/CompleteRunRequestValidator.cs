using CineBoutique.Inventory.Api.Models;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Validators;

public sealed class CompleteRunRequestValidator : AbstractValidator<CompleteRunRequest>
{
    public CompleteRunRequestValidator(IValidator<CompleteRunItemRequest> itemValidator)
    {
        RuleFor(request => request.OwnerUserId)
            .NotEmpty();

        RuleFor(request => request.CountType)
            .Must(countType => countType is 1 or 2 or 3)
            .WithMessage("countType doit valoir 1, 2 ou 3.");

        RuleFor(request => request.Items)
            .NotNull()
            .WithMessage("Au moins une ligne doit être fournie.")
            .Must(items => items!.Count > 0)
            .WithMessage("Au moins une ligne doit être fournie.");

        RuleForEach(request => request.Items)
            .SetValidator(itemValidator);
    }
}

public sealed class CompleteRunItemRequestValidator : AbstractValidator<CompleteRunItemRequest>
{
    public CompleteRunItemRequestValidator()
    {
        RuleFor(item => item.Ean)
            .NotEmpty();

        RuleFor(item => item.Quantity)
            .GreaterThanOrEqualTo(0);
    }
}
