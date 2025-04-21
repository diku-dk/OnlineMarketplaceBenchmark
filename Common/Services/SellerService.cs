using Common.Entities;
using Common.Workload;
using Common.Workload.Metrics;
using Common.Workers.Seller;
using Common.Streaming;
using Common.Workload.Seller;

namespace Common.Services;

public sealed class SellerService : ISellerService
{
    public delegate ISellerWorker BuildSellerWorkerDelegate(int sellerId, IHttpClientFactory httpClientFactory, SellerWorkerConfig workerConfig);

    // callback
    public readonly BuildSellerWorkerDelegate BuildSellerWorker;

    private readonly Dictionary<int, ISellerWorker> sellers;

    public SellerService(Dictionary<int, ISellerWorker> sellers, BuildSellerWorkerDelegate buildSellerWorker)
    {
        this.sellers = sellers;
        this.BuildSellerWorker = buildSellerWorker;
    }

    public Product GetProduct(int sellerId, int idx) => this.sellers[sellerId].GetProduct(idx);

    public List<TransactionOutput> GetFinishedTransactions(int sellerId)
    {
        return this.sellers[sellerId].GetFinishedTransactions();
    }

    public List<TransactionIdentifier> GetSubmittedTransactions(int sellerId)
    {
        return this.sellers[sellerId].GetSubmittedTransactions();
    }

    public void Run(int sellerId, string tid, TransactionType type) => this.sellers[sellerId].Run(tid, type);

    public List<TransactionMark> GetAbortedTransactions()
    {
        List<TransactionMark> merged = new();
        foreach(var seller in this.sellers.Values)
        {
            merged.AddRange(seller.GetAbortedTransactions());
        }
        return merged;
    }

    public void AddFinishedTransaction(int sellerId, TransactionOutput transactionOutput)
    {
        this.sellers[sellerId].AddFinishedTransaction(transactionOutput);
    }

    public IDictionary<int,List<Product>> GetTrackedProductUpdates()
    {
        Dictionary<int,List<Product>> dict = new(this.sellers.Count);
        foreach(int sellerId in this.sellers.Keys)
        {
            dict.Add(sellerId, this.sellers[sellerId].GetTrackedProductUpdates());
        }
        return dict;
    }

}

