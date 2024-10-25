using Common.Distribution;
using Common.Entities;
using Common.Workload;
using Common.Workload.Metrics;

namespace Common.Workers.Customer;

public interface ICustomerWorker
{
    void SetUp(Interval sellerRange, DistributionType sellerDistribution, DistributionType keyDistribution,
        double sellerZipfian, double productZipfian);

    void Run(string tid);

    List<TransactionIdentifier> GetSubmittedTransactions();

    // only for synchronous-based APIs, like Orleans
    List<TransactionOutput> GetFinishedTransactions();

    // support only in Orleans implementation right now
    IDictionary<string, List<CartItem>> GetCartItemsPerTid(DateTime finishTime);

}
