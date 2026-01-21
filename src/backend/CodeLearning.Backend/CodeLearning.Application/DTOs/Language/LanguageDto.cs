namespace CodeLearning.Application.DTOs.Language;

public record LanguageDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string FileExtension { get; init; }
    public bool IsEnabled { get; init; }
}
