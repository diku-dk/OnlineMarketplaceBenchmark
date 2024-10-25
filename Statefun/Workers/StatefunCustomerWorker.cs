using Common.Distribution;
using Common.Entities;
using Common.Infra;
using Common.Services;
using Common.Streaming;
using Common.Workers.Customer;
using Common.Workload;
using Common.Workload.CustomerWorker;
using Common.Workload.Metrics;
using Microsoft.Extensions.Logging;
using Statefun.Infra;

namespace Statefun.Workers;

public sealed class StatefunCustomerWorker : DefaultCustomerWorker
{
    private readonly string partitionID;

    private StatefunCustomerWorker(ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Customer customer, HttpClient httpClient, ILogger logger) : base(sellerService, numberOfProducts, config, customer, httpClient, logger)
    {
        this.partitionID = this.customer.id.ToString();
    }

    public static new StatefunCustomerWorker BuildCustomerWorker(IHttpClientFactory httpClientFactory, ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Customer customer)
    {
        var logger = LoggerProxy.GetInstance("Customer" + customer.id.ToString());
        return new StatefunCustomerWorker(sellerService, numberOfProducts, config, customer, httpClientFactory.CreateClient(), logger);
    }

    public override void SetUp(Interval sellerRange, DistributionType sellerDistribution,  DistributionType keyDistribution,
                                double sellerZipfian, double productZipfian)
    {
        base.SetUp(sellerRange, sellerDistribution, keyDistribution, sellerZipfian, productZipfian);
    }

    protected override void BuildAddCartPayloadAndSend(string payLoad)
    {        
        string apiUrl = string.Concat(this.config.cartUrl, "/", partitionID);        
        string eventType = "AddCartItem";
        string contentType = string.Concat(StatefunUtils.BASE_CONTENT_TYPE, eventType);
        StatefunUtils.SendHttpToStatefun(this.httpClient, apiUrl, contentType, payLoad).Wait();                    
    }

    protected override void SendCheckoutRequest(string tid)
    {
        var payload = this.BuildCheckoutPayload(tid);
        try
        {
            DateTime sentTs = DateTime.UtcNow;

            string apiUrl = string.Concat(this.config.cartUrl, "/", partitionID);        
            string eventType = "CustomerCheckout";
            string contentType = string.Concat(StatefunUtils.BASE_CONTENT_TYPE, eventType);
            
            HttpResponseMessage resp = StatefunUtils.SendHttpToStatefun(this.httpClient, apiUrl, contentType, payload).Result;  
                    
            if (resp.IsSuccessStatusCode)
            {
                TransactionIdentifier txId = new(tid, TransactionType.CUSTOMER_SESSION, sentTs);
                this.submittedTransactions.Add(txId);
            }
            else
            {
                this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.CUSTOMER_SESSION, this.customer.id, MarkStatus.ABORT, "cart"));
            }
        }
        catch (Exception e)
        {
            this.logger.LogError("Customer {0} Url {1}: Exception Message: {5} ", customer.id, this.config.cartUrl + "/" + this.customer.id, e.Message);
            this.InformFailedCheckout();
        }
    }

    protected override void InformFailedCheckout()
    {
        string apiUrl = string.Concat(this.config.cartUrl, "/", partitionID);        
        string eventType = "Seal";
        string contentType = string.Concat(StatefunUtils.BASE_CONTENT_TYPE, eventType);
        string payLoad = "{}";
        StatefunUtils.SendHttpToStatefun(this.httpClient, apiUrl, contentType, payLoad).Wait();  
    }

}