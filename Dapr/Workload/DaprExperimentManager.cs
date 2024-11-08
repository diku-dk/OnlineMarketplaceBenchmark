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

    public async void TrimStreams()
    {
        await RedisUtils.TrimStreams(this.redisConfig, this.channelsToTrim);
    }

    public override async void PostExperiment()
    {
        await RedisUtils.TrimStreams(this.redisConfig, this.channelsToTrim);
        base.PostExperiment();
    }

    /**
     * 1. Trim streams
     * 2. Initialize all customer objects
     * 3. Initialize delivery as a single object, but multithreaded
     */
    protected override async void PreExperiment()
    {
        // cleanup microservice states
        var resps_ = new List<Task<HttpResponseMessage>>();
        foreach (var task in this.config.postExperimentTasks)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Patch, task.url);
            LOGGER.LogInformation("Pre experiment task to URL {0}", task.url);
            resps_.Add(HttpUtils.HTTP_CLIENT.SendAsync(message));
        }
        await Task.WhenAll(resps_);

        await RedisUtils.TrimStreams(this.redisConfig, this.channelsToTrim);

        base.PreExperiment();
    }

    protected override async void PostRunTasks(int runIdx)
    {
        // trim first to avoid receiving events after the post run task
        await RedisUtils.TrimStreams(redisConfig, channelsToTrim);

        // reset data in microservices - post run
        if (runIdx < this.config.runs.Count - 1)
        {
            LOGGER.LogInformation("Post run tasks started");
            var responses = new List<Task<HttpResponseMessage>>();
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
                LOGGER.LogInformation("Post run task to Microservice {0} URL {1}", task.name, task.url);
                responses.Add(HttpUtils.HTTP_CLIENT.SendAsync(message));
            }
            await Task.WhenAll(responses);
            LOGGER.LogInformation("Post run tasks finished");
        }

    }

}
