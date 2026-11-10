using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Extensions;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class CreateVideoBlockDtoValidator : AbstractValidator<CreateVideoBlockDto>
{
    public CreateVideoBlockDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");

        RuleFor(x => x.VideoUrl)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Video URL is required")
            .MaximumLength(2000).WithMessage("Video URL must not exceed 2000 characters")
            .Must(url => url.IsValidYouTubeUrl()).WithMessage(YouTubeUrlRules.InvalidUrlMessage);
    }
}
