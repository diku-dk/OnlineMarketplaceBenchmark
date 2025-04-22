using Common.Services;
using Common.Workload;

namespace Orleans.Workload;

public sealed class ActorWorkloadManager : WorkloadManager
{

    private ActorWorkloadManager(
        ISellerService sellerService,
        ICustomerService customerService,
        IDeliveryService deliveryService,
        IDictionary<TransactionType, int> transactionDistribution,
        Interval customerRange,
        int concurrencyLevel, ConcurrencyType concurrencyType,
        int executionTime, int delayBetweenRequests) :
        base(sellerService, customerService, deliveryService, transactionDistribution, customerRange,
            concurrencyLevel, concurrencyType, executionTime, delayBetweenRequests)
    { }

    public static new ActorWorkloadManager BuildWorkloadManager(
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
        return new ActorWorkloadManager(sellerService, customerService, deliveryService, transactionDistribution, customerRange, concurrencyLevel, concurrencyType, executionTime, delayBetweenRequests);
    }

    protected override void SubmitTransaction(string tid, TransactionType txType)
    {
        Task.Run(() => this.RunTransaction(tid, txType), CancellationToken.None)
            .ContinueWith(_=> Shared.ResultQueue.Writer.WriteAsync(Shared.ITEM), CancellationToken.None);
    }

}