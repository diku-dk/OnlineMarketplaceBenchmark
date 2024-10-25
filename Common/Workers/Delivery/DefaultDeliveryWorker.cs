using System.Collections.Concurrent;
using Common.Infra;
using Common.Streaming;
using Common.Workload;
using Common.Workload.Delivery;
using Common.Workload.Metrics;
using Microsoft.Extensions.Logging;

namespace Common.Workers.Delivery;

/**
 * Default delivery worker. It considers a synchronous API for requesting an UPDATE DELIVERY transaction
 */
public class DefaultDeliveryWorker : IDeliveryWorker
{
    protected readonly HttpClient httpClient;

    protected readonly DeliveryWorkerConfig config;

    protected readonly ILogger logger;

    protected readonly ConcurrentBag<TransactionMark> abortedTransactions;

    protected readonly ConcurrentBag<TransactionIdentifier> submittedTransactions;

    protected readonly ConcurrentBag<TransactionOutput> finishedTransactions;

    public static DefaultDeliveryWorker BuildDeliveryWorker(IHttpClientFactory httpClientFactory, DeliveryWorkerConfig config)
    {
        var logger = LoggerProxy.GetInstance("DeliveryWorker");
        return new DefaultDeliveryWorker(config, httpClientFactory, logger);
    }

    protected DefaultDeliveryWorker(DeliveryWorkerConfig config, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        this.config = config;
        this.httpClient = httpClientFactory.CreateClient();
        this.logger = logger;
        this.abortedTransactions = new();
        this.submittedTransactions = new();
        this.finishedTransactions = new();
    }

    public virtual void Run(string tid)
    {
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Patch, this.config.shipmentUrl + "/" + tid);
        var initTime = DateTime.UtcNow;
        try {
            var resp = this.httpClient.Send(message);
            if (resp.IsSuccessStatusCode)
            {
                var endTime = DateTime.UtcNow;
                var init = new TransactionIdentifier(tid, TransactionType.UPDATE_DELIVERY, initTime);
                this.submittedTransactions.Add(init);
                DoAfterSuccessSubmission(new TransactionOutput(tid, endTime));
            }
            else
            {
                this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.UPDATE_DELIVERY, 1, MarkStatus.ABORT, "shipment"));
                this.logger.LogDebug("Delivery worker failed to update delivery for TID {0}: {1}", tid, resp.ReasonPhrase);
            }
        } catch(Exception e)
        {
             this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.UPDATE_DELIVERY, 1, MarkStatus.ABORT, "shipment"));
                this.logger.LogDebug("Delivery worker failed to update delivery for TID {0}: {1}", tid, e);
        }
    }

    protected virtual void DoAfterSuccessSubmission(TransactionOutput transactionOutput)
    {
        AddFinishedTransaction(transactionOutput);
    }

    public List<TransactionMark> GetAbortedTransactions()
    {
        var list = new List<TransactionMark>();
        while (this.abortedTransactions.TryTake(out var item))
        {
            list.Add(item);
        }
        return list;
    }

    public virtual void AddFinishedTransaction(TransactionOutput transactionOutput)
    {
        this.finishedTransactions.Add(transactionOutput);
    }

    public List<TransactionOutput> GetFinishedTransactions()
    {
        var list = new List<TransactionOutput>();
        while (this.finishedTransactions.TryTake(out var item))
        {
            list.Add(item);
        }
        return list;
    }

    public List<TransactionIdentifier> GetSubmittedTransactions()
    {
        var list = new List<TransactionIdentifier>();
        while (this.submittedTransactions.TryTake(out var item))
        {
            list.Add(item);
        }
        return list;
    }

}

