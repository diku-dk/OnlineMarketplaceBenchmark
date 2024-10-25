using Common.Entities;
using Common.Experiment;
using Common.Infra;
using Orleans.Workers;
using DuckDB.NET.Data;
using Common.Workers.Delivery;
using Common.Metric;
using Common.Workload;
using static Common.Services.CustomerService;
using static Common.Services.DeliveryService;
using static Common.Services.SellerService;

namespace Orleans.Workload;

public sealed class ActorExperimentManager : AbstractExperimentManager
{
    private readonly ActorWorkloadManager myWorkloadManager;

    public static ActorExperimentManager BuildActorExperimentManager(IHttpClientFactory httpClientFactory, ExperimentConfig config, DuckDBConnection connection)
    {
        return new ActorExperimentManager(httpClientFactory, ActorSellerWorker.BuildSellerWorker, ActorCustomerWorker.BuildCustomerWorker, DefaultDeliveryWorker.BuildDeliveryWorker, config, connection);
    }

    private ActorExperimentManager(IHttpClientFactory httpClientFactory, BuildSellerWorkerDelegate sellerWorkerDelegate, BuildCustomerWorkerDelegate customerWorkerDelegate, BuildDeliveryWorkerDelegate deliveryWorkerDelegate, ExperimentConfig config, DuckDBConnection connection) :
        base(httpClientFactory, ActorWorkloadManager.BuildWorkloadManager, MetricManager.BuildMetricManager, sellerWorkerDelegate, customerWorkerDelegate, deliveryWorkerDelegate, config, connection)
    {
        this.myWorkloadManager = (ActorWorkloadManager)this.workloadManager;
    }

    public async Task RunSimpleExperiment(int type)
    {
        this.customers = DuckDbUtils.SelectAll<Customer>(this.connection, "customers");
        this.PreExperiment();
        this.PreWorkload(0);
        this.myWorkloadManager.SetUp(new Interval(1, this.numSellers), this.config.runs[0].sellerDistribution, this.config.runs[0].sellerZipfian);
        (DateTime startTime, DateTime finishTime) res;
        if(type == 0){
            Console.WriteLine("Thread mode selected.");
            res = this.myWorkloadManager.RunThreads();
        }
        else if(type == 1) {
            Console.WriteLine("Task mode selected.");
            res = this.myWorkloadManager.RunTasks();
        }
        else {
            Console.WriteLine("Task per Tx mode selected.");
            res = await this.myWorkloadManager.RunTaskPerTx();
        }
        DateTime startTime = res.startTime;
        DateTime finishTime = res.finishTime;
        this.Collect(0, startTime, finishTime);
        this.PostExperiment();
        CollectGarbage();
    }

}
