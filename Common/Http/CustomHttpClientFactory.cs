namespace Common.Http;

public sealed class CustomHttpClientFactory : IHttpClientFactory
{
	public CustomHttpClientFactory()
	{
	}

    static readonly SocketsHttpHandler handler = new SocketsHttpHandler
    {
        UseProxy = false,
        Proxy = null,
        UseCookies = false, 
    };

    static readonly HttpClient sharedClient;

    static CustomHttpClientFactory() {
        sharedClient = new HttpClient(handler);
        sharedClient.Timeout = TimeSpan.FromMilliseconds(250);
        sharedClient.DefaultRequestHeaders.ConnectionClose = false;
    }

    public HttpClient CreateClient(string name = null)
    {
        return sharedClient;
    }
}


