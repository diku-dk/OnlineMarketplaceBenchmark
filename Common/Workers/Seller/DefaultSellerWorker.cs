using Common.Entities;
using Common.Http;
using Common.Infra;
using Common.Requests;
using Common.Streaming;
using Common.Workload;
using Common.Workload.Metrics;
using Common.Workload.Seller;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Common.Workers.Seller;

/**
 * The default seller thread assumes that updates to product are completed eventually but seller dashboard are completed synchronously
 */
public class DefaultSellerWorker : AbstractSellerWorker
{
    protected readonly IHttpClientFactory httpClientFactory;

    // many threads can access the same seller. to avoid contention in the network channel, it is better to use a connection per thread
    private readonly IDictionary<long, HttpClient> httpClientPerThread;

	protected DefaultSellerWorker(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig, ILogger logger) : base(sellerId, workerConfig, logger)
	{
        this.httpClientFactory = httpClientFactory;
        this.httpClientPerThread = new Dictionary<long, HttpClient>();
	}

	public static DefaultSellerWorker BuildSellerWorker(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig)
    {
        var logger = LoggerProxy.GetInstance("SellerThread_"+ sellerId);
        return new DefaultSellerWorker(sellerId, httpClientFactory, workerConfig, logger);
    }

    protected override void SendUpdatePriceRequest(Product product, string tid)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, this.config.productUrl);
        string serializedObject = JsonConvert.SerializeObject(
            new PriceUpdate(this.sellerId, product.product_id, product.price, product.version, tid));
        // Console.WriteLine(serializedObject);
        request.Content = HttpUtils.BuildPayload(serializedObject);

        var initTime = DateTime.UtcNow;
        var resp = this.GetHttpClient().Send(request, HttpCompletionOption.ResponseHeadersRead);
        if (resp.IsSuccessStatusCode)
        {
            this.DoAfterSuccessUpdate(tid, TransactionType.PRICE_UPDATE);
            this.submittedTransactions.Add(new TransactionIdentifier(tid, TransactionType.PRICE_UPDATE, initTime));
        }
        else
        {
            this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.PRICE_UPDATE, this.sellerId, MarkStatus.ABORT, "product"));
            this.logger.LogWarning("Seller {0} failed to update product {1} price: {2}", this.sellerId, product.product_id, resp.ReasonPhrase);
        }
    }

    protected override void SendProductUpdateRequest(Product product, string tid)
    {
        string productJson = JsonConvert.SerializeObject(product);
        // Console.WriteLine(productJson);
        HttpRequestMessage message = new(HttpMethod.Put, this.config.productUrl)
        {
            Content = HttpUtils.BuildPayload(productJson)
        };

        var sentTs = DateTime.UtcNow;
        var resp = this.GetHttpClient().Send(message, HttpCompletionOption.ResponseHeadersRead);
        if (resp.IsSuccessStatusCode)
        {
            this.DoAfterSuccessUpdate(tid, TransactionType.UPDATE_PRODUCT);
            this.submittedTransactions.Add(new TransactionIdentifier(tid, TransactionType.UPDATE_PRODUCT, sentTs));
        }
        else
        {
            this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.UPDATE_PRODUCT, this.sellerId, MarkStatus.ABORT, "product"));
            this.logger.LogWarning("Seller {0} failed to update product {1} version: {2}", this.sellerId, product.product_id, resp.ReasonPhrase);
        }
    }

    protected virtual void DoAfterSuccessUpdate(string tid, TransactionType transactionType)
    {
        // do nothing by default
    }

    public override void BrowseDashboard(string tid)
    {
        try
        {
            HttpRequestMessage message = new(HttpMethod.Get, config.sellerUrl + "/dashboard/" + this.sellerId);
            message.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            var startTs = DateTime.UtcNow;
            var response = this.GetHttpClient().Send(message);
            var endTs = DateTime.UtcNow;
            if (response.IsSuccessStatusCode)
            {
                this.finishedTransactions.Add(new TransactionOutput(tid, endTs));
                this.submittedTransactions.Add(new TransactionIdentifier(tid, TransactionType.QUERY_DASHBOARD, startTs));
                // this.logger.LogInformation("Seller {0} - {1} - Dashboard retrieval succeeded: {2}", this.sellerId, endTs, response.ReasonPhrase);
            }
            else
            {
                this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.QUERY_DASHBOARD, this.sellerId, MarkStatus.ABORT, "seller"));
                this.logger.LogWarning("Seller {0} - {1} - Dashboard retrieval failed: {2}", this.sellerId, endTs, response.ReasonPhrase);
            }
        }
        catch (Exception e)
        {
            this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.QUERY_DASHBOARD, this.sellerId, MarkStatus.ABORT, "seller"));
            this.logger.LogError("Seller {0}: Dashboard could not be retrieved: {1}", this.sellerId, e.Message);
        }
    }

    private HttpClient GetHttpClient()
    {
        if( this.httpClientPerThread.ContainsKey(Environment.CurrentManagedThreadId))
        {
            return this.httpClientPerThread[Environment.CurrentManagedThreadId];
        }
        var httpClient = this.httpClientFactory.CreateClient();
        this.httpClientPerThread.Add(Environment.CurrentManagedThreadId, httpClient);
        return httpClient;
    }

}

