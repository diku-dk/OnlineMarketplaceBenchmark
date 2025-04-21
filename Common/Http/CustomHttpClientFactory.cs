namespace Common.Http;

public sealed class CustomHttpClientFactory : IHttpClientFactory
{
	private CustomHttpClientFactory()
	{
	}

    private static readonly CustomHttpClientFactory INSTANCE = new CustomHttpClientFactory();

    public static CustomHttpClientFactory GetInstance()
    {
        return INSTANCE;
    }

    private static readonly SocketsHttpHandler handler = new SocketsHttpHandler
    {
        UseProxy = false,
        Proxy = null,
        UseCookies = false,
        AllowAutoRedirect = false,
        PreAuthenticate = false,
    };

    private static readonly HttpClient SHARED_HTTP_CLIENT;

    static CustomHttpClientFactory() {
        SHARED_HTTP_CLIENT = new HttpClient(handler);
        SHARED_HTTP_CLIENT.Timeout = Timeout.InfiniteTimeSpan; // avoid throwing timeout exception
        SHARED_HTTP_CLIENT.DefaultRequestHeaders.ConnectionClose = false;
    }

    public HttpClient CreateClient(string name = null)
    {
        return SHARED_HTTP_CLIENT;
    }
}


