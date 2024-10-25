using Common.Infra;
using Common.Workers.Delivery;
using Common.Workload;
using Common.Workload.Delivery;
using Common.Workload.Metrics;
using Microsoft.Extensions.Logging;

namespace DriverBench.Workers;

public sealed class DriverBenchDeliveryWorker : DefaultDeliveryWorker
{
    private DriverBenchDeliveryWorker(DeliveryWorkerConfig config, IHttpClientFactory httpClientFactory, ILogger logger) : base(config, httpClientFactory, logger)
    {
    }

    public static new DriverBenchDeliveryWorker BuildDeliveryWorker(IHttpClientFactory httpClientFactory, DeliveryWorkerConfig config)
    {
        var logger = LoggerProxy.GetInstance("DriverBenchDeliveryWorker");
        return new DriverBenchDeliveryWorker(config, httpClientFactory, logger);
    }

    public override void Run(string tid)
    {
        var init = new TransactionIdentifier(tid, TransactionType.CUSTOMER_SESSION, DateTime.UtcNow);
        // fixed delay
        Thread.Sleep(100);
        var end = new TransactionOutput(tid, DateTime.UtcNow);
        this.submittedTransactions.Add(init);
        this.finishedTransactions.Add(end);
        while (!Shared.ResultQueue.Writer.TryWrite(Shared.ITEM));
    }

    public override void AddFinishedTransaction(TransactionOutput transactionOutput)
    {
        throw new NotImplementedException("Should not call this method for DriverBench implementation");
    }

}
