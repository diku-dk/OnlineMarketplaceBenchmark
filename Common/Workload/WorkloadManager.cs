using System.Collections.Concurrent;
using System.Diagnostics;
using Common.Distribution;
using MathNet.Numerics.Distributions;
using Common.Services;

namespace Common.Workload;

public class WorkloadManager
{

    public delegate WorkloadManager BuildWorkloadManagerDelegate(ISellerService sellerService,
                ICustomerService customerService,
                IDeliveryService deliveryService,
                IDictionary<TransactionType, int> transactionDistribution,
                Interval customerRange,
                int concurrencyLevel,
                ConcurrencyType concurrencyType,
                int executionTime,
                int delayBetweenRequests);

    protected readonly ISellerService sellerService;
    protected readonly ICustomerService customerService;
    protected readonly IDeliveryService deliveryService;

    private readonly IDictionary<TransactionType, int> transactionDistribution;
    private readonly Random random;

    protected readonly BlockingCollection<int> customerIdleQueue;

    protected readonly int executionTime;
    protected readonly int concurrencyLevel;
    private readonly ConcurrencyType concurrencyType;
    private readonly int delayBetweenRequests;

    protected readonly IDictionary<TransactionType, int> histogram;

    protected IDiscreteDistribution sellerIdGenerator;

    private readonly Interval customerRange;

    protected WorkloadManager(
                ISellerService sellerService,
                ICustomerService customerService,
                IDeliveryService deliveryService,
                IDictionary<TransactionType,int> transactionDistribution,
                Interval customerRange,
                int concurrencyLevel,
                ConcurrencyType concurrencyType,
                int executionTime,
                int delayBetweenRequests)
    {
        this.sellerService = sellerService;
        this.customerService = customerService;
        this.deliveryService = deliveryService;
        this.transactionDistribution = transactionDistribution;
        this.customerRange = customerRange;
        this.random = new Random();
        this.concurrencyLevel = concurrencyLevel;
        this.concurrencyType = concurrencyType;
        this.executionTime = executionTime;
        this.delayBetweenRequests = delayBetweenRequests;
        this.histogram = new Dictionary<TransactionType, int>();
        this.customerIdleQueue = new BlockingCollection<int>(new ConcurrentQueue<int>());
    }

    public static WorkloadManager BuildWorkloadManager(
                ISellerService sellerService,
                ICustomerService customerService,
                IDeliveryService deliveryService,
                IDictionary<TransactionType, int> transactionDistribution,
                Interval customerRange,
                int concurrencyLevel,
                ConcurrencyType concurrencyType,
                int executionTime,
                int delayBetweenRequests)
    {
        return new WorkloadManager(sellerService, customerService, deliveryService, transactionDistribution, customerRange, concurrencyLevel, concurrencyType, executionTime, delayBetweenRequests);
    }

    // can differ across runs
    public void SetUp(Interval sellerRange, DistributionType sellerDistribution, double sellerZipfian)
    {
        this.sellerIdGenerator =
                    sellerDistribution == DistributionType.UNIFORM ?
                    new DiscreteUniform(sellerRange.min, sellerRange.max, new Random()) :
                    new Zipf(sellerZipfian, sellerRange.max, new Random());

        foreach (TransactionType tx in Enum.GetValues(typeof(TransactionType)))
        {
            this.histogram[tx] = 0;
        }

        while(this.customerIdleQueue.TryTake(out _)){ }
        for (int i = this.customerRange.min; i <= this.customerRange.max; i++)
        {
            this.customerIdleQueue.Add(i);
        }
    }

    public virtual (DateTime startTime, DateTime finishTime) Run(CancellationTokenSource cancellationTokenSource = null)
    {
        if(this.concurrencyType == ConcurrencyType.CONTROL)
        {
            return this.RunControl();
        } else
        {
            if(cancellationTokenSource == null)
                return this.RunContinuous(new CancellationTokenSource());
            return this.RunContinuous(cancellationTokenSource);
        }
    }

    // signal when all threads have started
    private Barrier barrier;
    private CancellationTokenSource tokenSource;

    private (DateTime startTime, DateTime finishTime) RunContinuous(CancellationTokenSource cancellationTokenSource)
    {
        // pass token source along to align polling task and emitter threads
        this.tokenSource = cancellationTokenSource;
        this.barrier = new Barrier(this.concurrencyLevel+1);

        int i = 0;
        while(i < this.concurrencyLevel)
        {
            var thread = new Thread(Worker);
            thread.Start();
            i++;
        }

        // for all to start at the same time
        this.barrier.SignalAndWait();
        var startTime = DateTime.UtcNow;
        Console.WriteLine("Run started at {0}.", startTime);
        Thread.Sleep(this.executionTime);
        var finishTime = DateTime.UtcNow;
        cancellationTokenSource.Cancel();
        this.barrier.Dispose();
        Console.WriteLine("Run finished at {0}.", finishTime);
        return (startTime, finishTime);
    }

    private void Worker()
    {
        long threadId = Environment.CurrentManagedThreadId;
        Console.WriteLine("Thread {0} started", threadId); 
        int currentTid = 0;
        this.barrier.SignalAndWait();
        while(!this.tokenSource.IsCancellationRequested)
        {
            TransactionType tx = this.PickTransactionFromDistribution();
            currentTid++;
            var instanceId = threadId.ToString()+"-"+currentTid.ToString();
            this.SubmitTransaction(instanceId, tx);
        }
        Console.WriteLine("Thread {0} finished. Last TID submitted was {1}", threadId, currentTid);
    }

    // two classes of transactions:
    // a.eventual complete
    // b. complete on response received
    // for b it is easy, upon completion we know we can submit another transaction
    // for a is tricky, we never know when it completes
    public virtual (DateTime startTime, DateTime finishTime) RunControl()
	{
        Stopwatch s = new Stopwatch();
        var execTime = TimeSpan.FromMilliseconds(this.executionTime);
        int currentTid = 1;
        var startTime = DateTime.UtcNow;
        Console.WriteLine("Started sending batch of transactions with concurrency level {0} at {1}.", this.concurrencyLevel, startTime);
        s.Start();
        while (currentTid < this.concurrencyLevel)
        {
            TransactionType tx = this.PickTransactionFromDistribution();
            this.histogram[tx]++;
            var toPass = currentTid;
            _ = Task.Run(() => this.SubmitTransaction(toPass.ToString(), tx));
            currentTid++;

            // throttle
            if (this.delayBetweenRequests > 0)
            {
                Thread.Sleep(this.delayBetweenRequests);
            }
        }

        
        while (s.Elapsed < execTime)
        {
            TransactionType tx = this.PickTransactionFromDistribution();
            this.histogram[tx]++;
            var toPass = currentTid;
            // spawning in a different thread may lead to duplicate tids in actors
            _ = Task.Run(() => this.SubmitTransaction(toPass.ToString(), tx));
            currentTid++;

            // it is ok to waste cpu time for higher precision in experiment time
            // the load is in the target platform receiving the workload, not here
            while (!Shared.ResultQueue.Reader.TryRead(out _) && s.Elapsed < execTime) { }

            // throttle
            if (this.delayBetweenRequests > 0)
            {
                Thread.Sleep(this.delayBetweenRequests);
            }
        }

        var finishTime = DateTime.UtcNow;
        s.Stop();

        Console.WriteLine("Finished at {0}. Last TID submitted was {1}", finishTime, currentTid-1);
        Console.WriteLine("Histogram:");
        foreach(var entry in this.histogram)
        {
            Console.WriteLine("{0}: {1}", entry.Key, entry.Value);
        }

        return (startTime, finishTime);
    }

    protected TransactionType PickTransactionFromDistribution()
    {
        int x = this.random.Next(0, 101);
        foreach (var entry in this.transactionDistribution)
        {
            if (x <= entry.Value)
            {
                return entry.Key;
            }
        }
        return TransactionType.NONE;
    }

    protected virtual void SubmitTransaction(string tid, TransactionType type)
    {
        try
        {
            switch (type)
            {
                // customer worker
                case TransactionType.CUSTOMER_SESSION:
                {
                    int customerId = this.customerIdleQueue.Take();
                    this.customerService.Run(customerId, tid);
                    this.customerIdleQueue.Add(customerId);
                    break;
                }
                // delivery worker
                case TransactionType.UPDATE_DELIVERY:
                {
                    this.deliveryService.Run(tid);
                    break;
                }
                // seller worker
                case TransactionType.PRICE_UPDATE:
                case TransactionType.UPDATE_PRODUCT:
                case TransactionType.QUERY_DASHBOARD:
                {
                    int sellerId = this.sellerIdGenerator.Sample();
                    this.sellerService.Run(sellerId, tid, type);
                    break;
                }
                default:
                {
                    long threadId = Environment.CurrentManagedThreadId;
                    Console.WriteLine("Thread ID " + threadId + " Unknown transaction type defined!");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            if(e is HttpRequestException)
            {
                e = e.GetBaseException();
            }
            Console.WriteLine($"Thread ID {Environment.CurrentManagedThreadId} - Error caught in SubmitTransaction.\nTID: {tid} Type: {type} \nError Type: {e.GetType()}\nError Source: {e.Source}\nError Message: {e.Message}\n StackTrace: \n{e.StackTrace}");
           
        }
    }

}
