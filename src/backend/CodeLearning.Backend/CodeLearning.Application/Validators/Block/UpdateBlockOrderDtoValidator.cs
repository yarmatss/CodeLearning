using CodeLearning.Application.DTOs.Block;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class UpdateBlockOrderDtoValidator : AbstractValidator<UpdateBlockOrderDto>
{
    public UpdateBlockOrderDtoValidator()
    {
        RuleFor(x => x.NewOrderIndex)
            .GreaterThan(0).WithMessage("Order index must be greater than 0");
    }
}
