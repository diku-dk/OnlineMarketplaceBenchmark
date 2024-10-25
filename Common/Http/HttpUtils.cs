using System.Text;

namespace Common.Http;

public sealed class HttpUtils
{
    public static readonly HttpClient client; 

    static HttpUtils()
    {
        client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = false,
            Proxy = null,
            UseCookies = false,
        });
        client.Timeout = TimeSpan.FromMilliseconds(1000);
        client.DefaultRequestHeaders.ConnectionClose = false;
    }

    private static readonly string JsonContentType = "application/json";

    private static readonly Encoding encoding = Encoding.UTF8;

    public static StringContent BuildPayload(string item)
    {
        return new StringContent(item, encoding, JsonContentType);
    }

}
