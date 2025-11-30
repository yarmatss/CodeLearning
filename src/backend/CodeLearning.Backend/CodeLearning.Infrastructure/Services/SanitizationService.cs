using CodeLearning.Application.Services;
using Ganss.Xss;
using System.Net;

namespace CodeLearning.Infrastructure.Services;

public class SanitizationService : ISanitizationService
{
    private readonly HtmlSanitizer _sanitizer;

    public SanitizationService()
    {
        _sanitizer = new HtmlSanitizer();
        ConfigureAllowedTags();
    }

    public string SanitizeMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        return _sanitizer.Sanitize(content);
    }

    public string SanitizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return WebUtility.HtmlEncode(text);
    }

    private void ConfigureAllowedTags()
    {
        // Markdown-safe HTML tags
        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedTags.Add("p");
        _sanitizer.AllowedTags.Add("br");
        _sanitizer.AllowedTags.Add("strong");
        _sanitizer.AllowedTags.Add("em");
        _sanitizer.AllowedTags.Add("u");
        _sanitizer.AllowedTags.Add("h1");
        _sanitizer.AllowedTags.Add("h2");
        _sanitizer.AllowedTags.Add("h3");
        _sanitizer.AllowedTags.Add("h4");
        _sanitizer.AllowedTags.Add("h5");
        _sanitizer.AllowedTags.Add("h6");
        _sanitizer.AllowedTags.Add("ul");
        _sanitizer.AllowedTags.Add("ol");
        _sanitizer.AllowedTags.Add("li");
        _sanitizer.AllowedTags.Add("blockquote");
        _sanitizer.AllowedTags.Add("code");
        _sanitizer.AllowedTags.Add("pre");
        _sanitizer.AllowedTags.Add("a");
        _sanitizer.AllowedTags.Add("img");
        _sanitizer.AllowedTags.Add("table");
        _sanitizer.AllowedTags.Add("thead");
        _sanitizer.AllowedTags.Add("tbody");
        _sanitizer.AllowedTags.Add("tr");
        _sanitizer.AllowedTags.Add("th");
        _sanitizer.AllowedTags.Add("td");
        _sanitizer.AllowedTags.Add("hr");
        _sanitizer.AllowedTags.Add("del");
        _sanitizer.AllowedTags.Add("ins");

        // Allowed attributes
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedAttributes.Add("href"); // <a>
        _sanitizer.AllowedAttributes.Add("src"); // <img>
        _sanitizer.AllowedAttributes.Add("alt"); // <img>
        _sanitizer.AllowedAttributes.Add("title");
        _sanitizer.AllowedAttributes.Add("class");

        // Allowed URL schemes (prevent javascript:, data:, etc.)
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");

        // Disallow dangerous attributes
        _sanitizer.AllowedAttributes.Remove("onclick");
        _sanitizer.AllowedAttributes.Remove("onload");
        _sanitizer.AllowedAttributes.Remove("onerror");
        _sanitizer.AllowedAttributes.Remove("onmouseover");
        _sanitizer.AllowedAttributes.Remove("onfocus");
        _sanitizer.AllowedAttributes.Remove("onblur");

        // Disallow dangerous tags
        _sanitizer.AllowedTags.Remove("script");
        _sanitizer.AllowedTags.Remove("iframe");
        _sanitizer.AllowedTags.Remove("object");
        _sanitizer.AllowedTags.Remove("embed");
        _sanitizer.AllowedTags.Remove("applet");
        _sanitizer.AllowedTags.Remove("meta");
        _sanitizer.AllowedTags.Remove("link");
        _sanitizer.AllowedTags.Remove("style");
        _sanitizer.AllowedTags.Remove("form");
        _sanitizer.AllowedTags.Remove("input");
        _sanitizer.AllowedTags.Remove("button");
    }
}
