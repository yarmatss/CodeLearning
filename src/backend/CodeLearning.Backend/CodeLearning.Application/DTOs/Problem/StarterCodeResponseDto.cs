namespace CodeLearning.Application.DTOs.Problem;

public class StarterCodeResponseDto
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public Guid LanguageId { get; set; }
    public required string LanguageName { get; set; }
}
