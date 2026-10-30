namespace CodeLearning.Tests.Helpers;

public static class HttpResponseMessageExtensions
{
    public static string GetAccessToken(this HttpResponseMessage response) =>
        response.GetSetCookie("access_token").Value;

    public static (string Value, string Attributes) GetSetCookie(this HttpResponseMessage response, string cookieName)
    {
        var prefix = cookieName + "=";

        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(prefix, StringComparison.Ordinal))
            : null;

        if (setCookie is null)
        {
            throw new InvalidOperationException($"Response did not set the '{cookieName}' cookie");
        }

        var parts = setCookie[prefix.Length..].Split(';', 2);
        var attributes = parts.Length > 1 ? parts[1] : string.Empty;

        return (Uri.UnescapeDataString(parts[0]), attributes);
    }
}
