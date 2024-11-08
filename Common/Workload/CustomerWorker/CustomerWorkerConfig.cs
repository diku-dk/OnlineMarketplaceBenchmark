namespace Common.Workload.CustomerWorker;

/**
* The necessary data required by a customer worker to work properly
*/
public sealed class CustomerWorkerConfig
{
    public Interval minMaxNumItemsRange { get; set; }

    // probability of a customer to checkout the cart
    public int checkoutProbability { get; set; }

    public string cartUrl { get; set; }

    public string checkoutUrl { get; set; }

    public Interval minMaxQtyRange { get; set; }

    public Interval delayBetweenRequestsRange { get; set; }

    public int voucherProbability { get; set; }

    // flag that defines whether submitted TIDs are tracked
    public bool trackTids { get; set; }

    // flag that defines whether all items are from the same seller
    public bool uniqueSeller { get; set; }

    public CustomerWorkerConfig(){}

    public CustomerWorkerConfig(Interval minMaxNumItemsRange, int checkoutProbability, string cartUrl, Interval minMaxQtyRange, Interval delayBetweenRequestsRange, int voucherProbability, bool trackTids, bool uniqueSeller)
    {
        this.minMaxNumItemsRange = minMaxNumItemsRange;
        this.checkoutProbability = checkoutProbability;
        this.cartUrl = cartUrl;
        this.minMaxQtyRange = minMaxQtyRange;
        this.delayBetweenRequestsRange = delayBetweenRequestsRange;
        this.voucherProbability = voucherProbability;
        this.trackTids = trackTids;
        this.uniqueSeller = uniqueSeller;
    }

}
