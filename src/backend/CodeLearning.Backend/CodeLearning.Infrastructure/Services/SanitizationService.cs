using CodeLearning.Application.Services;
using Ganss.Xss;
using System.Net;

namespace CodeLearning.Infrastructure.Services;

public class SanitizationService : ISanitizationService
{
    private readonly HtmlSanitizer _sanitizer = new();

    public SanitizationService()
    {
        ConfigureAllowedTags();
    }

    public string SanitizeMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return _sanitizer.Sanitize(content);
    }

    public string SanitizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return WebUtility.HtmlEncode(text);
    }

    private void ConfigureAllowedTags()
    {
        _sanitizer.AllowedTags.Clear();

        var allowedTags = new[]
        {
            "p", "br", "strong", "em", "u",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li", "blockquote",
            "code", "pre", "a", "img",
            "table", "thead", "tbody", "tr", "th", "td",
            "hr", "del", "ins"
        };

        foreach (var tag in allowedTags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Clear();

        var allowedAttributes = new[] { "href", "src", "alt", "title", "class" };
        foreach (var attr in allowedAttributes)
        {
            _sanitizer.AllowedAttributes.Add(attr);
        }

        _sanitizer.AllowedSchemes.Clear();

        var allowedSchemes = new[] { "http", "https", "mailto" };
        foreach (var scheme in allowedSchemes)
        {
            _sanitizer.AllowedSchemes.Add(scheme);
        }
    }
}
