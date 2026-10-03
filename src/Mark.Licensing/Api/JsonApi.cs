using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mark.Licensing.Api;

/// <summary>
/// A request to the licence server failed. <see cref="IsConnectionFailure"/>: the server could not be reached (no
/// internet, wrong address, server not running); otherwise the server answered with <see cref="Code"/>.
/// </summary>
public sealed class LicenceServerException : Exception
{
    public LicenceServerException(string code, string message, HttpStatusCode? status = null, bool isConnectionFailure = false,
        Exception? inner = null) : base(message, inner)
    {
        Code = code;
        Status = status;
        IsConnectionFailure = isConnectionFailure;
    }

    public string Code { get; }

    public HttpStatusCode? Status { get; }

    public bool IsConnectionFailure { get; }
}

/// <summary>JSON over HTTP to the licence server, with its errors turned into <see cref="LicenceServerException"/>.</summary>
public sealed class JsonApi : IDisposable
{
    private readonly HttpClient _http;

    public JsonApi(string serverUrl, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        BaseUrl = NormaliseUrl(serverUrl);
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(BaseUrl);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    /// <summary>The server address with a scheme and a trailing slash, e.g. "http://localhost:5180/".</summary>
    public string BaseUrl { get; }

    /// <summary>Sent as "Authorization: Bearer …" (MARK Owner's admin session).</summary>
    public string? BearerToken { get; set; }

    /// <summary>"localhost:5180" → "http://localhost:5180/". Throws <see cref="ArgumentException"/> for an unusable address.</summary>
    public static string NormaliseUrl(string? url)
    {
        string text = (url ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("Enter the licence server address.");
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"\"{url}\" is not a valid server address.");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken cancel = default)
    {
        using var response = await SendRawAsync(method, path, body, cancel).ConfigureAwait(false);
        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(LicenceJson.Options, cancel).ConfigureAwait(false);
            return result ?? throw new LicenceServerException(ErrorCodes.Invalid, "The licence server sent an empty answer.", response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new LicenceServerException(ErrorCodes.Invalid, "The licence server sent an answer MARK cannot read.", response.StatusCode, inner: ex);
        }
    }

    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancel = default)
    {
        using var response = await SendRawAsync(method, path, body, cancel).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: LicenceJson.Options);
        if (BearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            throw new LicenceServerException("connection", $"The licence server at {BaseUrl} could not be reached. " +
                                                           "Check that the licence server is running, the internet connection and the server address.",
                isConnectionFailure: true, inner: ex);
        }

        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            ApiError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiError>(LicenceJson.Options, cancel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                // Not one of our errors (e.g. a proxy page): reported below by status.
            }

            throw error is { Code.Length: > 0 }
                ? new LicenceServerException(error.Code, error.Message, response.StatusCode)
                : new LicenceServerException("http", $"The licence server answered {(int)response.StatusCode} {response.ReasonPhrase}. " +
                                                     "Check the server address.", response.StatusCode);
        }
    }

    public void Dispose() => _http.Dispose();
}
