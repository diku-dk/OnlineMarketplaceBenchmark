using Common.Distribution;
using Common.Entities;
using Common.Infra;
using Common.Services;
using Common.Workers.Customer;
using Common.Workload;
using Common.Workload.CustomerWorker;
using Common.Workload.Metrics;
using Microsoft.Extensions.Logging;

namespace Orleans.Workers;

/**
 * Implements the functionality of a synchronous customer API.
 * As a result, this class must add a finished transaction mark in the DoAfterSubmission method
 */
public sealed class ActorCustomerWorker : DefaultCustomerWorker
{

    private ActorCustomerWorker(ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Customer customer, HttpClient httpClient, ILogger logger) : base(sellerService, numberOfProducts, config, customer, httpClient, logger)
    { }

    public static new ActorCustomerWorker BuildCustomerWorker(IHttpClientFactory httpClientFactory, ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Customer customer)
    {
        var logger = LoggerProxy.GetInstance("Customer_" + customer.id.ToString());
        return new ActorCustomerWorker(sellerService, numberOfProducts, config, customer, httpClientFactory.CreateClient(), logger);
    }

    /**
     * This method expects the content to contain items belonging to the cart
     */
    protected override void DoAfterSuccessSubmission(string tid)
    {
        this.finishedTransactions.Add(new TransactionOutput(tid, DateTime.UtcNow));
        base.DoAfterSuccessSubmission(tid);
    }

}

