using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LanguagesController(ILanguageService languageService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetLanguages()
    {
        var languages = await languageService.GetAllLanguagesAsync();
        return Ok(languages);
    }
}
