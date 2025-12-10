namespace CodeLearning.Application.DTOs.Problem;

public class CreateStarterCodeDto
{
    public required string Code { get; set; }
    public Guid LanguageId { get; set; }
}
