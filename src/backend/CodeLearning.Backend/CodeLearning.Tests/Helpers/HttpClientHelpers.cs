using Microsoft.AspNetCore.Mvc.Testing;

namespace CodeLearning.Tests.Helpers;

public static class HttpClientHelpers
{
    private static readonly Uri HttpsBaseAddress = new("https://localhost");

    /// <summary>
    /// Creates a client that never stores or sends cookies, so a request is anonymous unless the test
    /// adds credentials itself. Use this for "WithoutAuth" tests instead of relying on the Secure
    /// auth cookies not being sent over plain http.
    /// </summary>
    public static HttpClient CreateAnonymousClient<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = HttpsBaseAddress,
            HandleCookies = false
        });

    /// <summary>
    /// Creates a client with a cookie container and an https base address, so the Secure auth cookies
    /// set by register/login/refresh are stored and sent back automatically, as a browser would.
    /// </summary>
    public static HttpClient CreateCookieAuthClient<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = HttpsBaseAddress,
            HandleCookies = true
        });

    /// <summary>
    /// Adds a Cookie header to a single request. Use with a client created by <see cref="CreateAnonymousClient{TEntryPoint}"/>
    /// to send exactly the cookies a test wants.
    /// </summary>
    public static HttpRequestMessage WithCookie(this HttpRequestMessage request, string name, string value)
    {
        var pair = $"{name}={Uri.EscapeDataString(value)}";

        if (request.Headers.TryGetValues("Cookie", out var existing))
        {
            request.Headers.Remove("Cookie");
            pair = string.Join("; ", existing.Append(pair));
        }

        request.Headers.Add("Cookie", pair);
        return request;
    }
}
