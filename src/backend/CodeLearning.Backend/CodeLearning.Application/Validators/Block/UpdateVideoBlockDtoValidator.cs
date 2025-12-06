using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Extensions;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class UpdateVideoBlockDtoValidator : AbstractValidator<UpdateVideoBlockDto>
{
    public UpdateVideoBlockDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");

        RuleFor(x => x.VideoUrl)
            .NotEmpty().WithMessage("Video URL is required")
            .Must(url => url.IsValidYouTubeUrl())
            .WithMessage("Invalid YouTube URL format. Supported: youtube.com/watch?v=... or youtu.be/...");
    }
}
