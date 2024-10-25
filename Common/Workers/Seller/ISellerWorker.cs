using Common.Distribution;
using Common.Entities;
using Common.Streaming;
using Common.Workload;
using Common.Workload.Metrics;

namespace Common.Workers.Seller;

public interface ISellerWorker
{
	void Run(string tid, TransactionType type)
    {
        switch (type)
        {
            case TransactionType.QUERY_DASHBOARD:
            {
                BrowseDashboard(tid);
                break;
            }
            case TransactionType.UPDATE_PRODUCT:
            {
                UpdateProduct(tid);
                break;
            }
            case TransactionType.PRICE_UPDATE:
            {   
                UpdatePrice(tid);
                break;
            }
        }
    }

    void UpdatePrice(string tid);
    void UpdateProduct(string tid);
    void BrowseDashboard(string tid);

    List<TransactionIdentifier> GetSubmittedTransactions();

    List<TransactionOutput> GetFinishedTransactions();

    List<TransactionMark> GetAbortedTransactions();

    Product GetProduct(int idx);

    void SetUp(List<Product> products, DistributionType keyDistribution, double productZipfian);

    void AddFinishedTransaction(TransactionOutput transactionOutput);

    List<Product> GetTrackedProductUpdates();

}

