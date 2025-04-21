using Common.Services;
using Common.Workload;

namespace Orleans.Workload;

public sealed class ActorWorkloadManager : WorkloadManager
{

    private delegate void SubmitActorTransaction(string tid, TransactionType txType);

    private readonly SubmitActorTransaction callback;

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
    {
        if(concurrencyType == ConcurrencyType.CONTROL)
        {
            this.callback = SubmitControl;
        } else
        {
            this.callback = base.SubmitTransaction;
        }
    }

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
        this.callback(tid, txType);
    }

    private void SubmitControl(string tid, TransactionType txType)
    {
        Task.Run(() => this.RunTransaction(tid, txType), CancellationToken.None)
            .ContinueWith(_=> Shared.ResultQueue.Writer.WriteAsync(Shared.ITEM), CancellationToken.None);
    }


}