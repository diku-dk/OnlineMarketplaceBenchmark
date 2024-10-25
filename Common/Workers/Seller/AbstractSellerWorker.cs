using System.Collections.Concurrent;
using Common.Distribution;
using Common.Entities;
using Common.Streaming;
using Common.Workload;
using Common.Workload.Metrics;
using Common.Workload.Seller;
using MathNet.Numerics.Distributions;
using Microsoft.Extensions.Logging;

namespace Common.Workers.Seller;

/**
 * Contains core functionality related ensuring safety in product selection
 * Thus, there is no communication protocol here
 */
public abstract class AbstractSellerWorker : ISellerWorker
{

    private readonly Random random;

    protected readonly SellerWorkerConfig config;

    protected int sellerId;

    private IDiscreteDistribution productIdGenerator;

    protected readonly ILogger logger;

    private Product[] products;

    private object[] productLocks;

    // concurrent bag because of concurrent writes of different products
    protected readonly ConcurrentBag<TransactionIdentifier> submittedTransactions;
    protected readonly ConcurrentBag<TransactionOutput> finishedTransactions;
    protected readonly ConcurrentBag<TransactionMark> abortedTransactions;

    // track sequence of updates to items so it can be matched against the historical carts
    private readonly List<Product> trackedUpdates;

    protected AbstractSellerWorker(int sellerId, SellerWorkerConfig workerConfig, ILogger logger)
	{
        this.random = Random.Shared;
        this.logger = logger;
        this.submittedTransactions = new ConcurrentBag<TransactionIdentifier>();
        this.finishedTransactions = new ConcurrentBag<TransactionOutput>();
        this.abortedTransactions = new ConcurrentBag<TransactionMark>();
        this.sellerId = sellerId;
        this.config = workerConfig;
        this.trackedUpdates = new List<Product>();
    }

    public void SetUp(List<Product> sellerProducts, DistributionType productDistribution, double productZipfian)
    {
        this.products = sellerProducts.ToArray();
        this.productLocks = new object[this.products.Length];
        for(int i = 0; i < this.products.Length; i++)
        {
            this.productLocks[i] = new object();
        }

        this.productIdGenerator = productDistribution == DistributionType.UNIFORM ?
                                 new DiscreteUniform(1, sellerProducts.Count, Random.Shared) :
                                 new Zipf(productZipfian, sellerProducts.Count, Random.Shared);
        this.submittedTransactions.Clear();
        this.finishedTransactions.Clear();
        this.abortedTransactions.Clear();
        this.trackedUpdates.Clear();
    }

    /**
     * The method is only called if there are available products, so the while loop always finishes at some point
     */
    public void UpdatePrice(string tid)
    {
        int idx = this.productIdGenerator.Sample() - 1;
        object locked = this.productLocks[idx];
        while(!Monitor.TryEnter(locked))
        {
            idx = this.productIdGenerator.Sample() - 1;
            locked = this.productLocks[idx];
        }

        int percToAdjust = this.random.Next(this.config.adjustRange.min, this.config.adjustRange.max);
        var currPrice = this.products[idx].price;
        var newPrice = currPrice + ((currPrice * percToAdjust) / 100);

        try {
            Product product = new(this.products[idx], newPrice);
            this.SendUpdatePriceRequest(product, tid);
            // update instance after successful request
            this.products[idx] = product;
            if(this.config.trackUpdates) this.trackedUpdates.Add(product);
        }
        finally
        {
            Monitor.Exit(locked);
        }
    }

    protected abstract void SendUpdatePriceRequest(Product productToUpdate, string tid);

    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
    public void UpdateProduct(string tid)
    {
        int idx = this.productIdGenerator.Sample() - 1;
        object locked = this.productLocks[idx];
        // only one update of a given version is allowed
        while(!Monitor.TryEnter(locked))
        {
            idx = this.productIdGenerator.Sample() - 1;
            locked = this.productLocks[idx];
        }
        try
        {
            Product product = new(this.products[idx], tid);
            this.SendProductUpdateRequest(product, tid);
            // trick so customer do not need to synchronize to get a product
            // (concurrent thread may refer to an older reference, i.e., version, though)
            this.products[idx] = product;
            if(this.config.trackUpdates) this.trackedUpdates.Add(product);
        }
        finally
        {
            Monitor.Exit(locked);
        } 
    }

    protected abstract void SendProductUpdateRequest(Product product, string tid);

    // we may retrieve a product that is being concurrently deleted
    // at first, I was thinking to always get available product..
    // because concurrently, a seller can delete a product and the time spent on finding a available product is lost
    public Product GetProduct(int idx)
    {
        return this.products[idx];
    }

    public abstract void BrowseDashboard(string tid);

    public List<TransactionOutput> GetFinishedTransactions()
    {
        var list = new List<TransactionOutput>();
        while (this.finishedTransactions.TryTake(out var item))
        {
            list.Add(item);
        }
        return list;
    }

    public List<TransactionIdentifier> GetSubmittedTransactions()
    {
        var list = new List<TransactionIdentifier>();
        while (this.submittedTransactions.TryTake(out var item))
        {
            list.Add(item);
        }
        return list;
    }

    public virtual void AddFinishedTransaction(TransactionOutput transactionOutput)
    {
        this.finishedTransactions.Add(transactionOutput);
    }

    public List<TransactionMark> GetAbortedTransactions()
    {
        var list = new List<TransactionMark>();
        while (this.abortedTransactions.TryTake(out var item))
        {
            list.Add(item);
        }
        return list;
    }

    public List<Product> GetTrackedProductUpdates()
    {
        return this.trackedUpdates;
    }

}

