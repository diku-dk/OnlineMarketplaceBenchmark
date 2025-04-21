using Common.DataGeneration;
using Common.Entities;
using Common.Http;
using Common.Infra;
using Common.Metric;
using Common.Services;
using Common.Workers.Customer;
using Common.Workers.Seller;
using Common.Workload;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using static Common.Metric.MetricManager;
using static Common.Services.CustomerService;
using static Common.Services.DeliveryService;
using static Common.Services.SellerService;
using static Common.Workload.WorkloadManager;

namespace Common.Experiment;

public abstract class AbstractExperimentManager
{
    protected const string PostExpMessage = "Post experiment task {0} to URL {1}";
    protected const string PostExpErrorMessage = "Post experiment task {0} to URL {1} failed:\n{2}";
    protected const string PostRunMessage = "Post run task {0} to URL {1}";
    protected const string PostRunErrorMessage = "Post run task {0} to URL {1} failed:\n{2}";
    protected const string ConnOpenedMessage = "Perhaps connection is already opened? Message={0}";
    protected const string RunFinishedMessage = "Run #{0} finished at {1}";
    protected const string NumProdChangedFromLastRunMessage = "Run #{0} number of products changed from last run {1}";
    protected const string RunStartedMessage = "Run #{0} started at {1}";
    protected const string ConvergeWaitMessage = "Wait for microservices to converge (i.e., finish receiving events) for {0} seconds...";
    private const string InitGcMessage = "Memory used before collection:       {0:N0}";
    private const string AfterGcMessage = "Memory used after full collection:   {0:N0}";

    protected readonly IHttpClientFactory httpClientFactory;

    protected readonly ExperimentConfig config;

    protected readonly DuckDBConnection connection;

    protected static readonly byte ITEM = 0;

    protected static readonly ILogger LOGGER = LoggerProxy.GetInstance("ExperimentManager");

    // workers config
    protected readonly DeliveryService deliveryService;

    protected List<Customer> customers;
    protected readonly Interval customerRange;
    private readonly Dictionary<int, AbstractCustomerWorker> customerThreads;
    protected readonly CustomerService customerService;

    protected readonly SellerService sellerService;
    private readonly Dictionary<int, ISellerWorker> sellerThreads;
    protected int numSellers;
    protected readonly MetricManager metricManager;
    protected readonly WorkloadManager workloadManager;

    public AbstractExperimentManager(IHttpClientFactory httpClientFactory, BuildWorkloadManagerDelegate workloadManagerDelegate, BuildMetricManagerDelegate buildMetricManagerDelegate, BuildSellerWorkerDelegate sellerWorkerDelegate, BuildCustomerWorkerDelegate customerWorkerDelegate, BuildDeliveryWorkerDelegate deliveryWorkerDelegate, ExperimentConfig config, DuckDBConnection duckDBConnection)
    {
        this.httpClientFactory = httpClientFactory;
        this.config = config;
        this.connection = duckDBConnection;

        this.deliveryService = new DeliveryService(deliveryWorkerDelegate(httpClientFactory, config.deliveryWorkerConfig));

        this.sellerThreads = new Dictionary<int, ISellerWorker>();
        this.sellerService = new SellerService(this.sellerThreads, sellerWorkerDelegate);
        this.numSellers = 0;

        this.customerThreads = new Dictionary<int, AbstractCustomerWorker>();
        this.customerService = new CustomerService(this.customerThreads, customerWorkerDelegate);
        this.customerRange = new Interval(1, config.numCustomers);

        this.workloadManager = workloadManagerDelegate(
                                this.sellerService, this.customerService, this.deliveryService,
                                config.transactionDistribution,
                                this.customerRange,
                                config.concurrencyLevel,
                                config.concurrencyType,
                                config.executionTime,
                                config.delayBetweenRequests);

        this.metricManager = buildMetricManagerDelegate(sellerService, customerService, deliveryService);
    }

    /**
     * Initialize all customer objects
     */
    protected virtual void PreExperiment()
    {
        LOGGER.LogInformation("Initializing customer workers...");
        for (int i = this.customerRange.min; i <= this.customerRange.max; i++)
        {
            this.customerThreads.Add(i, this.customerService.BuildCustomerWorker(this.httpClientFactory, this.sellerService, this.config.numProdPerSeller, this.config.customerWorkerConfig, this.customers[i - 1]));
        }
    }

     /**
     * 1. Initialize all seller objects
     * 2. Set distributions on customer objects
     */
    protected virtual void PreWorkload(int runIdx)
    {
        LOGGER.LogInformation("Initializing seller workers...");
        this.numSellers = (int)DuckDbUtils.Count(this.connection, "sellers");
        for (int i = 1; i <= this.numSellers; i++)
        {
            List<Product> products = DuckDbUtils.SelectAllWithPredicate<Product>(connection, "products", "seller_id = " + i);
            if (!this.sellerThreads.ContainsKey(i))
            {
                this.sellerThreads[i] = this.sellerService.BuildSellerWorker(i, this.httpClientFactory, this.config.sellerWorkerConfig); 
            }
            this.sellerThreads[i].SetUp(products, this.config.runs[runIdx].keyDistribution,  this.config.runs[runIdx].productZipfian);
        }

        LOGGER.LogInformation("Setting up seller workload data in customer workers...");
        Interval sellerRange = new Interval(1, this.numSellers);
        for (int i = this.customerRange.min; i <= this.customerRange.max; i++)
        {
            this.customerThreads[i].SetUp(sellerRange, this.config.runs[runIdx].sellerDistribution, this.config.runs[runIdx].keyDistribution, this.config.runs[runIdx].sellerZipfian, this.config.runs[runIdx].productZipfian);
        }
    }

     /**
     * Reset microservice states
     */
    protected virtual void PostRunTasks(int runIdx)
    {
        foreach (var task in this.config.postRunTasks)
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
    }

    /**
     * Cleanup microservice states
     */
    public virtual void PostExperiment()
    {
        foreach (var task in this.config.postExperimentTasks)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Patch, task.url);
            LOGGER.LogInformation(PostExpMessage, task.name, task.url);
            try {
                HttpUtils.HTTP_CLIENT.Send(message);
            }
            catch(Exception e)
            {
                LOGGER.LogError(PostExpErrorMessage, task.name, task.url, e.Message);
            }
        }
    }

    protected virtual void Collect(int runIdx, DateTime startTime, DateTime finishTime)
    {
        this.metricManager.SetUp(this.numSellers, this.config.numCustomers);
        string ts = new DateTimeOffset(startTime).ToUnixTimeMilliseconds().ToString();
        this.metricManager.Collect(startTime, finishTime, this.config.epoch, string.Format("{0}#{1}_{2}_{3}_{4}_{5}_{6}", ts, runIdx, this.config.numCustomers, this.config.concurrencyLevel, this.config.runs[runIdx].numProducts, this.config.runs[runIdx].sellerDistribution, this.config.runs[runIdx].keyDistribution));
    }

    public virtual async Task Run()
    {
        try {
            this.connection.Open();
        } catch(InvalidOperationException e)
        {
            // ignore if exception is related to connection being opened already
            LOGGER.LogWarning(ConnOpenedMessage, e.Message);
        }
        SyntheticDataSourceConfig previousData = new SyntheticDataSourceConfig()
        {
            numCustomers = this.config.numCustomers,
            numProducts = 0 // to force product generation and ingestion in the upcoming loop
        };

        int runIdx = 0;

        var dataGen = new SyntheticDataGenerator(previousData);
        dataGen.CreateSchema(this.connection);
        // dont need to generate customers on every run. only once
        dataGen.GenerateCustomers(this.connection);
        // customers are fixed accross runs
        this.customers = DuckDbUtils.SelectAll<Customer>(this.connection, "customers");

        this.PreExperiment();

        foreach (var run in this.config.runs)
        {
            LOGGER.LogInformation(RunStartedMessage, runIdx, DateTime.UtcNow);

            if (run.numProducts != previousData.numProducts)
            {
                LOGGER.LogInformation(NumProdChangedFromLastRunMessage, runIdx, runIdx - 1);

                // update previous
                previousData = new SyntheticDataSourceConfig()
                {
                    numProdPerSeller = this.config.numProdPerSeller,
                    numCustomers = this.config.numCustomers,
                    numProducts = run.numProducts,
                    qtyPerProduct = this.config.qtyPerProduct
                };
                var syntheticDataGenerator = new SyntheticDataGenerator(previousData);

                // must truncate if not first run
                if (runIdx > 0)
                    syntheticDataGenerator.TruncateTables(connection);

                syntheticDataGenerator.Generate(connection);

                await DefaultIngestionOrchestrator.Run(connection, config.ingestionConfig);
    
                if (runIdx == 0)
                {
                    // remove customers from ingestion config from now on
                    this.config.ingestionConfig.mapTableToUrl.Remove("customers");
                }

            }

            this.PreWorkload(runIdx);

            this.workloadManager.SetUp(new Interval(1, this.numSellers), this.config.runs[runIdx].sellerDistribution, this.config.runs[runIdx].sellerZipfian);

            LOGGER.LogInformation(RunStartedMessage, runIdx, DateTime.UtcNow);

            (DateTime startTime, DateTime finishTime) = this.workloadManager.Run();

            LOGGER.LogInformation(ConvergeWaitMessage, this.config.delayBetweenRuns / 1000);
            Thread.Sleep(this.config.delayBetweenRuns);

            // set up data collection for metrics
            this.Collect(runIdx, startTime, finishTime);

            CollectGarbage();

            LOGGER.LogInformation(RunFinishedMessage, runIdx, DateTime.UtcNow);

            // increment run index
            runIdx++;

            this.PostRunTasks(runIdx);
        }

        LOGGER.LogInformation("Post experiment cleanup tasks starting...");

        this.PostExperiment();

        LOGGER.LogInformation("Experiment finished");
    }

    public void RunSimpleExperiment()
    {
        this.customers = DuckDbUtils.SelectAll<Customer>(this.connection, "customers");
        this.PreExperiment();
        this.PreWorkload(0);
        this.workloadManager.SetUp(new Interval(1, this.numSellers), this.config.runs[0].sellerDistribution, this.config.runs[0].sellerZipfian);
        (DateTime startTime, DateTime finishTime) = this.workloadManager.Run();
        this.Collect(0, startTime, finishTime);
        this.PostRunTasks(0);
        this.PostExperiment();
        CollectGarbage();
    }

    protected static void CollectGarbage()
    {
        LOGGER.LogInformation(InitGcMessage,GC.GetTotalMemory(false));
        // Collect all generations of memory.
        GC.Collect();
        LOGGER.LogInformation(AfterGcMessage,
        GC.GetTotalMemory(true));
    }

}
