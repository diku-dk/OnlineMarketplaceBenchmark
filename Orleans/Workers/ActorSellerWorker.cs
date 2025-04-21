using Common.Infra;
using Common.Workers.Seller;
using Common.Workload;
using Common.Workload.Metrics;
using Common.Workload.Seller;
using Microsoft.Extensions.Logging;

namespace Orleans.Workers;

public sealed class ActorSellerWorker : DefaultSellerWorker
{

	private ActorSellerWorker(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig, ILogger logger) : base(sellerId, httpClientFactory, workerConfig, logger)
	{ }

	public static new ActorSellerWorker BuildSellerWorker(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig)
    {
        var logger = LoggerProxy.GetInstance("Seller_"+ sellerId);
        return new ActorSellerWorker(sellerId, httpClientFactory, workerConfig, logger);
    }

    protected override void DoAfterSuccessUpdate(string tid, TransactionType transactionType)
    {
        this.finishedTransactions.Add(new TransactionOutput(tid, DateTime.UtcNow));
    }

}

