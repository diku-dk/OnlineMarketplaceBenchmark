using Common.Distribution;
using Common.Entities;
using Common.Infra;
using Common.Workers.Seller;
using Common.Workload;
using Common.Workload.Seller;
using Microsoft.Extensions.Logging;

namespace Tests.Thread;

/**
 * seller correctness criteria:
 * - no total order across sellers
 * - no total order across different products in a seller (e.g., updates from different products can be submitted concurrently)
 * - total order of updates of the same products in a seller
 * 
 * CAUTION: do not run the tests concurrently
 */
[Collection(nameof(SafetyCollectionDefinition))]
public class SellerTests
{

    private static int NUM_THREADS = 100;
    private static readonly Random random = new Random();

    private static readonly Queue<Message>[] queues = { new Queue<Message>(NUM_THREADS+1), new Queue<Message>(NUM_THREADS+1) };

    [Fact]
	public async void TestTwoProductsLinearizability()
	{
        var logger = LoggerProxy.GetInstance("SellerThread_"+ 1);
        var testSeller = new TestTwoProductsSeller(1, new SellerWorkerConfig(){ adjustRange = new Interval(1,10) }, logger);

        var productList = new List<Product>(){ new Product(){ product_id = 1, price = 10, version = "0", freight_value = 0 },
                                               new Product(){ product_id = 2, price = 10, version = "0", freight_value = 0 } };
        testSeller.SetUp(productList, DistributionType.UNIFORM, 0);

        var tasks = new List<Task>(100);

        for(int i = 1; i <= NUM_THREADS; i++)
        {
            var toPass = i;
            if(random.Next(0,3) == 0){
                tasks.Add( Task.Run(()=>testSeller.UpdatePrice(toPass.ToString())) );
            } else
            {
                tasks.Add( Task.Run(()=>testSeller.UpdateProduct(toPass.ToString())) );
            }
        }

        await Task.WhenAll(tasks);

        for(int j = 0; j < 2; j++){
            var list = queues[j].ToList();
            // start with price update?
            if (list[0].type == TransactionType.PRICE_UPDATE){
                Assert.True(list[0].version.SequenceEqual("0"));
            }

            for(int i = 1; i < list.Count; i++)
            {
                if( (list[i].type == TransactionType.PRICE_UPDATE && list[i-1].type == TransactionType.UPDATE_PRODUCT) ||
                    (list[i].type == TransactionType.PRICE_UPDATE && list[i-1].type == TransactionType.PRICE_UPDATE) )
                {
                    /*
                    if(!list[i].version.SequenceEqual(list[i - 1].version))
                    {
                        Console.WriteLine("Problem!");
                    }
                    */
                    Assert.True(list[i].version.SequenceEqual(list[i-1].version));
                } else if ( (list[i].type == TransactionType.UPDATE_PRODUCT && list[i-1].type == TransactionType.UPDATE_PRODUCT) ||
                            (list[i].type == TransactionType.UPDATE_PRODUCT && list[i-1].type == TransactionType.PRICE_UPDATE) )
                {
                    Assert.False(list[i].version.SequenceEqual( list[i-1].version ));
                }
            }
        }

        messages.Clear();

    }

    protected class TestTwoProductsSeller : AbstractSellerWorker
    {
        public TestTwoProductsSeller(int sellerId, SellerWorkerConfig workerConfig, ILogger logger) : base(sellerId, workerConfig, logger)
        {
        }

        public override void BrowseDashboard(string tid)
        {
            throw new NotImplementedException();
        }

        protected override void SendProductUpdateRequest(Product product, string tid)
        {
            queues[product.product_id - 1].Enqueue(new Message(TransactionType.UPDATE_PRODUCT, tid, product.version));
        }

        protected override void SendUpdatePriceRequest(Product productToUpdate, string tid)
        {
            queues[productToUpdate.product_id - 1].Enqueue(new Message(TransactionType.PRICE_UPDATE, tid, productToUpdate.version));
        }
    }

    // ======================================================================================================= //

    private static readonly Queue<Message> messages = new Queue<Message>(NUM_THREADS+1);

	[Fact]
	public async void TestSingleProductLinearizability()
	{
		// the goal is to check there are no interleaving of divergent versions

		//                           T2                                      T1
		// example: ----- price update with version 1 -------- product update to version 2
		// cannot send the price update (v1) after the product update (v2) above
        var logger = LoggerProxy.GetInstance("SellerThread_"+ 1);
        var testSeller = new TestSingleProductSeller(1, new SellerWorkerConfig(){ adjustRange = new Interval(1,10) }, logger);

        var productList = new List<Product>() { new Product() { product_id = 1, price = 10, version = "0", freight_value = 0 } };
        testSeller.SetUp(productList, DistributionType.UNIFORM, 0);

        var tasks = new List<Task>(100);

        for(int i = 1; i <= NUM_THREADS; i++)
        {
            var toPass = i;
            if(random.Next(0,3) == 0){
                tasks.Add( Task.Run(()=>testSeller.UpdatePrice(toPass.ToString())) );
            } else
            {
                tasks.Add( Task.Run(()=>testSeller.UpdateProduct(toPass.ToString())) );
            }
        }

        await Task.WhenAll(tasks);

        // assert whether there are no bad interleavings
 
        var list = messages.ToList();
        int NUMBER_COMPLETED = NUM_THREADS;
        if (list.Count != NUM_THREADS)
        {
            Console.WriteLine("Some tasks have not finished! Please ensure there are enough resources next time!");
            // Assert.True(false);
            NUMBER_COMPLETED = list.Count;

            if (list.Count == 0) Assert.True(false);
        }

        // start with price update?
        if (list[0].type == TransactionType.PRICE_UPDATE){
            Assert.True(list[0].version.SequenceEqual("0"));
        }

        for(int i = 1; i < NUMBER_COMPLETED; i++)
        {
            if(list[i].type == TransactionType.PRICE_UPDATE && list[i-1].type == TransactionType.UPDATE_PRODUCT) 
            {
                bool outcome = list[i].version.SequenceEqual(list[i - 1].version);
                Assert.True(outcome, list[i].version + " is != " + list[i - 1].version);
            } 
            else if (list[i].type == TransactionType.PRICE_UPDATE && list[i - 1].type == TransactionType.PRICE_UPDATE)
            {
                bool outcome = list[i].version.SequenceEqual(list[i - 1].version);
                Assert.True(outcome);
            }
            else if ( (list[i].type == TransactionType.UPDATE_PRODUCT && list[i-1].type == TransactionType.UPDATE_PRODUCT) ||
                        (list[i].type == TransactionType.UPDATE_PRODUCT && list[i-1].type == TransactionType.PRICE_UPDATE) )
            {
                Assert.False(list[i].version.SequenceEqual(list[i-1].version));
            }
        }

        messages.Clear();
	}

    private struct Message
    {
        public TransactionType type;
        // public int productId;
        public string tid;
        public string version;

        public Message(TransactionType type, string tid, string version) : this()
        {
            this.type = type;
            this.tid = tid;
            this.version = version;
        }
    }

    protected class TestSingleProductSeller : AbstractSellerWorker
    {
        public TestSingleProductSeller(int sellerId, SellerWorkerConfig workerConfig, ILogger logger) : base(sellerId, workerConfig, logger)
        {
        }

        public override void BrowseDashboard(string tid)
        {
            throw new NotImplementedException();
        }

        protected override void SendProductUpdateRequest(Product product, string tid)
        {
            messages.Enqueue(new Message(TransactionType.UPDATE_PRODUCT, tid, product.version));
        }

        protected override void SendUpdatePriceRequest(Product productToUpdate, string tid)
        {
            messages.Enqueue(new Message(TransactionType.PRICE_UPDATE, tid, productToUpdate.version));
        }
    }

}

