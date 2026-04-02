using System.Net;

namespace Collector.Tests.Helpers;

/// <summary>
/// A testable HttpMessageHandler that returns pre-configured responses
/// based on the request URL.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Content, Dictionary<string, string>? Headers)> _responses = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HttpRequestMessage> _sentRequests = [];
    private HttpStatusCode _defaultStatusCode = HttpStatusCode.NotFound;
    private string _defaultContent = "";
    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? _customHandler;

    public IReadOnlyList<HttpRequestMessage> SentRequests => _sentRequests;

    public void SetDefaultResponse(HttpStatusCode statusCode, string content = "")
    {
        _defaultStatusCode = statusCode;
        _defaultContent = content;
    }

    public void AddResponse(string url, HttpStatusCode statusCode, string content, Dictionary<string, string>? headers = null)
    {
        _responses[url] = (statusCode, content, headers);
    }

    public void SetCustomHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _customHandler = handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _sentRequests.Add(request);

        if (_customHandler is not null)
        {
            return await _customHandler(request);
        }

        var url = request.RequestUri?.ToString() ?? "";

        if (_responses.TryGetValue(url, out var response))
        {
            var httpResponse = new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Content, System.Text.Encoding.UTF8, "application/json")
            };

            if (response.Headers is not null)
            {
                foreach (var header in response.Headers)
                {
                    httpResponse.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return httpResponse;
        }

        return new HttpResponseMessage(_defaultStatusCode)
        {
            Content = new StringContent(_defaultContent, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
