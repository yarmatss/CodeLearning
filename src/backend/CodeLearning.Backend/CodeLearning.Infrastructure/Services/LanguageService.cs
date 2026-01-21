using CodeLearning.Application.DTOs.Language;
using CodeLearning.Application.Services;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class LanguageService(ApplicationDbContext context) : ILanguageService
{
    public async Task<IEnumerable<LanguageDto>> GetAllLanguagesAsync()
    {
        return await context.Languages
            .Where(l => l.IsEnabled)
            .OrderBy(l => l.Name)
            .Select(l => new LanguageDto
            {
                Id = l.Id,
                Name = l.Name,
                Version = l.Version,
                FileExtension = l.FileExtension,
                IsEnabled = l.IsEnabled
            })
            .ToListAsync();
    }
}
