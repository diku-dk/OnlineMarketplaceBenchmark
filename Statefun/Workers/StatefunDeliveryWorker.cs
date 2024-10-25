using Common.Infra;
using Common.Streaming;
using Common.Workload;
using Common.Workload.Delivery;
using Common.Workload.Metrics;
using Common.Workers.Delivery;
using Microsoft.Extensions.Logging;
using Statefun.Infra;

namespace Statefun.Workers;

public sealed class StatefunDeliveryWorker : DefaultDeliveryWorker
{

    private StatefunDeliveryWorker(DeliveryWorkerConfig config, IHttpClientFactory httpClientFactory, ILogger logger) : base(config, httpClientFactory, logger)
    {       
    }

    public static new StatefunDeliveryWorker BuildDeliveryWorker(IHttpClientFactory httpClientFactory, DeliveryWorkerConfig config)
    {
        var logger = LoggerProxy.GetInstance("Delivery");
        return new StatefunDeliveryWorker(config, httpClientFactory, logger);
    }

    public override void Run(string tid)
	{
        string payLoad = "{ \"tid\" : " + tid + " }";
        string partitionID = tid;
        string apiUrl = string.Concat(this.config.shipmentUrl, "/", partitionID);        
        string eventType = "UpdateShipment";
        string contentType = string.Concat(StatefunUtils.BASE_CONTENT_TYPE, eventType);

        var initTime = DateTime.UtcNow; 
        HttpResponseMessage resp = StatefunUtils.SendHttpToStatefun(this.httpClient, apiUrl, contentType, payLoad).Result;      

        if (resp.IsSuccessStatusCode)
        {
            TransactionIdentifier txId = new TransactionIdentifier(tid, TransactionType.UPDATE_DELIVERY, initTime);
            this.submittedTransactions.Add(txId);
        } else
        {
            this.abortedTransactions.Add( new TransactionMark(tid, TransactionType.UPDATE_DELIVERY, 1, MarkStatus.ABORT, "shipment")  );
            this.logger.LogDebug("Delivery worker failed to update delivery for TID {0}: {1}", tid, resp.ReasonPhrase);
        }
    }

}
