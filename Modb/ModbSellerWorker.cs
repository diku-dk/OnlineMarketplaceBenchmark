using Common.Infra;
using Common.Workers.Seller;
using Common.Workload;
using Common.Workload.Seller;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Modb;

public sealed class ModbSellerWorker : DefaultSellerWorker
{

	private ModbSellerWorker(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig, ILogger logger) : base(sellerId, httpClientFactory, workerConfig, logger)
	{
	}

	public static new ModbSellerWorker BuildSellerWorker(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig)
    {
        var logger = LoggerProxy.GetInstance("ModbSellerWorker_"+ sellerId);
        return new ModbSellerWorker(sellerId, httpClientFactory, workerConfig, logger);
    }

    protected override void DoAfterSuccessUpdate(string tid, TransactionType transactionType)
    {
        // map this tid to a batch
        BatchTrackingUtils.MapTidToBatch(tid, this.sellerId, transactionType);
    }

}


