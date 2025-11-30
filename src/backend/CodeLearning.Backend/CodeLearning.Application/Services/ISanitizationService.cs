namespace CodeLearning.Application.Services;

public interface ISanitizationService
{
    string SanitizeMarkdown(string content);
    string SanitizeText(string text);
}
