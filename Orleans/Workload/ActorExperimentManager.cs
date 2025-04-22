using Common.Experiment;
using Orleans.Workers;
using DuckDB.NET.Data;
using Common.Workers.Delivery;
using Common.Metric;
using static Common.Services.CustomerService;
using static Common.Services.DeliveryService;
using static Common.Services.SellerService;
using Common.Workload;

namespace Orleans.Workload;

public sealed class ActorExperimentManager : AbstractExperimentManager
{

    public static ActorExperimentManager BuildActorExperimentManager(IHttpClientFactory httpClientFactory, ExperimentConfig config, DuckDBConnection connection)
    {
        return new ActorExperimentManager(httpClientFactory, ActorSellerWorker.BuildSellerWorker, ActorCustomerWorker.BuildCustomerWorker, DefaultDeliveryWorker.BuildDeliveryWorker, config, connection);
    }

    private ActorExperimentManager(IHttpClientFactory httpClientFactory, BuildSellerWorkerDelegate sellerWorkerDelegate, BuildCustomerWorkerDelegate customerWorkerDelegate, BuildDeliveryWorkerDelegate deliveryWorkerDelegate, ExperimentConfig config, DuckDBConnection connection) :
        base(httpClientFactory, config.concurrencyType == ConcurrencyType.CONTROL ?  ActorWorkloadManager.BuildWorkloadManager : WorkloadManager.BuildWorkloadManager, MetricManager.BuildMetricManager, sellerWorkerDelegate, customerWorkerDelegate, deliveryWorkerDelegate, config, connection) { }

}
