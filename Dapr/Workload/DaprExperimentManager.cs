using System.Text;
using Daprr.Metric;
using Daprr.Streaming.Redis;
using Common.Experiment;
using Common.Streaming;
using Common.Http;
using Common.Workers.Seller;
using Common.Workload;
using Common.Workers.Customer;
using Common.Workers.Delivery;
using DuckDB.NET.Data;
using static Common.Services.CustomerService;
using static Common.Services.DeliveryService;
using static Common.Services.SellerService;
using StackExchange.Redis;

namespace Daprr.Workload;

public sealed class DaprExperimentManager : AbstractExperimentManager
{

    private readonly ConfigurationOptions redisConfig;
    private readonly List<string> channelsToTrim;

    static readonly List<TransactionType> TX_SET = new() { TransactionType.CUSTOMER_SESSION, TransactionType.PRICE_UPDATE, TransactionType.UPDATE_PRODUCT };

    public static DaprExperimentManager BuildDaprExperimentManager(IHttpClientFactory httpClientFactory, ExperimentConfig config, DuckDBConnection connection)
    {
        return new DaprExperimentManager(httpClientFactory, DefaultSellerWorker.BuildSellerWorker, DefaultCustomerWorker.BuildCustomerWorker, DefaultDeliveryWorker.BuildDeliveryWorker, config, connection);
    }

    private DaprExperimentManager(IHttpClientFactory httpClientFactory, BuildSellerWorkerDelegate sellerWorkerDelegate, BuildCustomerWorkerDelegate customerWorkerDelegate, BuildDeliveryWorkerDelegate deliveryWorkerDelegate, ExperimentConfig config, DuckDBConnection connection) :
        base(httpClientFactory, WorkloadManager.BuildWorkloadManager, DaprMetricManager.BuildDaprMetricManager, sellerWorkerDelegate, customerWorkerDelegate, deliveryWorkerDelegate, config, connection)
    {
        this.redisConfig = new ConfigurationOptions()
        {
            SyncTimeout = 500000,
            EndPoints =
            {
                {config.streamingConfig.host, config.streamingConfig.port }
            },
            AbortOnConnectFail = false
        };
        this.channelsToTrim = new();
        this.channelsToTrim.AddRange(this.config.streamingConfig.streams);

        // should also iterate over all transaction mark streams and trim them
        foreach (var type in TX_SET)
        {
            var channel = new StringBuilder(nameof(TransactionMark)).Append('_').Append(type.ToString()).ToString();
            this.channelsToTrim.Add(channel);
        }
    }

    public async Task TrimStreams()
    {
        LOGGER.LogInformation("Triggering trimming streams");
        await RedisUtils.TrimStreams(this.redisConfig, this.channelsToTrim);
        LOGGER.LogInformation("Trimming streams completed");
    }

    public override async void PostExperiment()
    {
        await TrimStreams();
        base.PostExperiment();
    }

    /**
     * 1. Cleanup microservice states
     * 2. Trim streams
     * 3. Call default pre experiment procedure
     */
    protected override async void PreExperiment()
    {
        // must execute first to make pre workload procedure does not find customer objects
        base.PreExperiment();
        base.PostExperiment();
        await TrimStreams();
    }

    protected override async void PostRunTasks(int runIdx)
    {
        // trim first to avoid receiving events after the post run task
        await RedisUtils.TrimStreams(redisConfig, channelsToTrim);

        // reset data in microservices - post run
        if (runIdx < this.config.runs.Count - 1)
        {
            LOGGER.LogInformation("Post run tasks started");
            List<PostRunTask> postRunTasks;
            // must call the cleanup if next run changes number of products
            if (config.runs[runIdx + 1].numProducts != config.runs[runIdx].numProducts)
            {
                LOGGER.LogInformation("Next run changes the number of products.");
                postRunTasks = config.postExperimentTasks;
            }
            else
            {
                LOGGER.LogInformation("Next run does not change the number of products.");
                postRunTasks = config.postRunTasks;
            }
            foreach (var task in postRunTasks)
            {
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Patch, task.url);
                LOGGER.LogInformation(PostRunMessage, task.name, task.url);
                try {
                    HttpUtils.HTTP_CLIENT.Send(message);
                }
                catch(Exception e)
                {
                    LOGGER.LogError(PostRunErrorMessage, task.name, task.url, e.Message);
                }
            }
            
            LOGGER.LogInformation("Post run tasks finished");
        }

    }

}
