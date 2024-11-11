using System.Text;

namespace Common.Http;

public sealed class HttpUtils
{
    public static readonly HttpClient HTTP_CLIENT; 

    static HttpUtils()
    {
        HTTP_CLIENT = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = false,
            Proxy = null,
            UseCookies = false,
            AllowAutoRedirect = false,
            PreAuthenticate = false,
        });
        HTTP_CLIENT.Timeout = TimeSpan.FromMilliseconds(2000);
        HTTP_CLIENT.DefaultRequestHeaders.ConnectionClose = false;
    }

    private static readonly string JsonContentType = "application/json";

    private static readonly Encoding encoding = Encoding.UTF8;

    public static StringContent BuildPayload(string item)
    {
        return new StringContent(item, encoding, JsonContentType);
    }

}
