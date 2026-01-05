using CodeLearning.Application.DTOs.Block;
using FluentValidation;
using System.Text.RegularExpressions;

namespace CodeLearning.Application.Validators.Block;

public partial class CreateVideoBlockDtoValidator : AbstractValidator<CreateVideoBlockDto>
{
    [GeneratedRegex(@"^(https?:\/\/)?(www\.)?(youtube\.com\/watch\?v=|youtu\.be\/)([a-zA-Z0-9_-]{11}).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrlRegex();

    public CreateVideoBlockDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");

        RuleFor(x => x.VideoUrl)
            .NotEmpty().WithMessage("Video URL is required")
            .Must(BeValidYouTubeUrl).WithMessage("Invalid YouTube URL format. Supported: youtube.com/watch?v=... or youtu.be/...");
    }

    private bool BeValidYouTubeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return YouTubeUrlRegex().IsMatch(url);
    }
}
