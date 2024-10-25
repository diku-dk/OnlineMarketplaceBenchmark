using Common.Distribution;
using Common.Entities;
using Common.Infra;
using Common.Services;
using Common.Streaming;
using Common.Workers.Customer;
using Common.Workload;
using Common.Workload.CustomerWorker;
using Common.Workload.Metrics;
using Microsoft.Extensions.Logging;

namespace Tests.Thread;

public class CustomerTests
{
    // let A and B represent products of the same seller
    // let the order of updates on a product be represented as A1, A2, A3, ..., An (same for B)
    // considering cart added product B2
    // the cart cannot add B1 afterwards since it has already "seen" B2
    // adding a product to a cart has to indicate a version
    // cart with causal cut is only necessary if we had multi-object updates

    private static int numThreads = 100;

    [Fact]
    public async void SimpleCheckoutTest()
    {

        // two sellers
        // each seller has a product
        // customer threads get items from these sellers
        // max two items per cart

        // final submission must not contain duplicate products

        TestSellerService testSellerService = new TestSellerService();

        CustomerWorkerConfig config = new()
        {
            maxNumberKeysToAddToCart = 2,
            checkoutProbability = 100,
        };

        var tasks = new List<Task>(100);

        CustomerThreadTest[] threads = new CustomerThreadTest[numThreads + 1];

        for (int i = 1; i <= numThreads; i++)
        {
            var toPass = i;
            var logger = LoggerProxy.GetInstance("SellerThread_" + toPass);
            threads[toPass] = new CustomerThreadTest(testSellerService, 2, config, new Customer() { id = toPass }, logger);
            threads[toPass].SetUp(new Interval(1, 2), DistributionType.UNIFORM, DistributionType.UNIFORM, 0, 0);
            tasks.Add(Task.Run(() => threads[toPass].Run(toPass.ToString())));
        }

        await Task.WhenAll(tasks);

        for (int j = 1; j <= numThreads; j++)
        {
            Assert.True(threads[j].cartItems.Count > 0 && threads[j].checkoutSent);
        } 
    }

    private class CustomerThreadTest : AbstractCustomerWorker
    {
        public readonly ISet<(int, int)> cartItems = new HashSet<(int, int)>();

        public bool checkoutSent = false;

        public CustomerThreadTest(ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Customer customer, ILogger logger) : base(sellerService, numberOfProducts, config, customer, logger)
        {
        }

        protected override void AddItemsToCart()
        {
            int numberOfProducts = random.Next(1, this.config.maxNumberKeysToAddToCart + 1);
            while (this.cartItems.Count < numberOfProducts)
            {
                var sellerId = this.sellerIdGenerator.Sample();
                var product = this.sellerService.GetProduct(sellerId, this.productIdGenerator.Sample() - 1);
                cartItems.Add((sellerId, product.product_id));
            }
        }

        public override List<TransactionOutput> GetFinishedTransactions()
        {
            throw new NotImplementedException();
        }

        protected override void InformFailedCheckout()
        {
            throw new NotImplementedException();
        }

        protected override void SendCheckoutRequest(string tid)
        {
            this.checkoutSent = true;
        }
    }

    private class TestSellerService : ISellerService
    {

        Product[][] products = new Product[2][];

        public TestSellerService() {
            products[0] = new Product[2];
            products[1] = new Product[2];
            products[0][0] = new Product() { seller_id = 1, product_id = 1, price = 10, version = "0" };
            products[0][1] = new Product() { seller_id = 1, product_id = 2, price = 10, version = "0" };
            products[1][0] = new Product() { seller_id = 2, product_id = 1, price = 10, version = "0" };
            products[1][1] = new Product() { seller_id = 2, product_id = 2, price = 10, version = "0" };
        }

        public void AddFinishedTransaction(int sellerId, TransactionOutput transactionOutput)
        {
            throw new NotImplementedException();
        }

        public List<TransactionMark> GetAbortedTransactions()
        {
            throw new NotImplementedException();
        }

        public List<TransactionOutput> GetFinishedTransactions(int sellerId)
        {
            throw new NotImplementedException();
        }

        public Product GetProduct(int sellerId, int idx)
        {
            return this.products[sellerId-1][idx];
        }

        public List<TransactionIdentifier> GetSubmittedTransactions(int sellerId)
        {
            throw new NotImplementedException();
        }

        public IDictionary<int, List<Product>> GetTrackedProductUpdates()
        {
            throw new NotImplementedException();
        }

        public void Run(int sellerId, string tid, TransactionType type)
        {
            throw new NotImplementedException();
        }
    }

}

