using CodeLearning.Application.DTOs.Language;

namespace CodeLearning.Application.Services;

public interface ILanguageService
{
    Task<IEnumerable<LanguageDto>> GetAllLanguagesAsync();
}
