using Common.Entities;
using Common.Http;
using Common.Infra;
using Common.Requests;
using Common.Services;
using Common.Streaming;
using Common.Workload;
using Common.Workload.CustomerWorker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Common.Workers.Customer;

/*
 * Contains default customer worker functionality
 */
public class DefaultCustomerWorker : AbstractCustomerWorker
{
    protected readonly HttpClient httpClient;

    protected readonly IDictionary<(int sellerId, int productId), Product> cartItems;

    protected readonly ISet<string> tids;

    protected DefaultCustomerWorker(ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Entities.Customer customer, HttpClient httpClient, ILogger logger) : base(sellerService, numberOfProducts, config, customer, logger)
    {
        this.httpClient = httpClient;
        this.cartItems = new Dictionary<(int, int), Product>(config.minMaxNumItemsRange.max);
        this.tids = new HashSet<string>();
    }

    public static DefaultCustomerWorker BuildCustomerWorker(IHttpClientFactory httpClientFactory, ISellerService sellerService, int numberOfProducts, CustomerWorkerConfig config, Entities.Customer customer)
    {
        var logger = LoggerProxy.GetInstance("Customer" + customer.id.ToString());
        return new DefaultCustomerWorker(sellerService, numberOfProducts, config, customer, httpClientFactory.CreateClient(), logger);
    }

    protected override void AddItemsToCart()
    {
        int numberKeysToAddToCart = this.random.Next(this.config.minMaxNumItemsRange.min, this.config.minMaxNumItemsRange.max + 1);
        if (this.config.uniqueSeller)
        {
            int sellerId = this.sellerIdGenerator.Sample();
            // prevent cases where number of products per seller is lower than number of items in the cart
            numberKeysToAddToCart = Math.Min(this.numberOfProducts, numberKeysToAddToCart);
            while (this.cartItems.Count < numberKeysToAddToCart)
            {
                this.AddItem(sellerId);
            }
        } else {
            while (this.cartItems.Count < numberKeysToAddToCart)
            {
                int sellerId = this.sellerIdGenerator.Sample();
                this.AddItem(sellerId);
            }
        }
    }

    private void AddItem(int sellerId)
    {
        Product product = this.sellerService.GetProduct(sellerId, this.productIdGenerator.Sample() - 1);
        if (this.cartItems.TryAdd((sellerId, product.product_id), product))
        {
            int quantity = this.random.Next(this.config.minMaxQtyRange.min, this.config.minMaxQtyRange.max + 1);
            try
            {
                string objStr = this.BuildCartItem(product, quantity);
                this.BuildAddCartPayloadAndSend(objStr);
            }
            catch (Exception e)
            {
                this.logger.LogError("Customer {0} Url {1} Seller {2} Key {3}: Exception Message: {5} ", customer.id, this.config.cartUrl, product.seller_id, product.product_id, e.Message);
            }
        }
    }

    protected virtual void BuildAddCartPayloadAndSend(string objStr)
    {
        StringContent payload = HttpUtils.BuildPayload(objStr);
        HttpRequestMessage message = new(HttpMethod.Patch, string.Format(this.BaseAddCartUrl, this.customer.id))
        {
            Content = payload
        };
        this.httpClient.Send(message, HttpCompletionOption.ResponseHeadersRead);
    }

    protected override void InformFailedCheckout()
    {
        // just cleaning cart state for next browsing
        HttpRequestMessage message = new(HttpMethod.Patch, string.Format(this.BaseSealCartUrl, this.customer.id));
        try { this.httpClient.Send(message); } catch(Exception){ }
    }

    // the idea is to reuse the cart state to resubmit an aborted customer checkout
    // and thus avoid having to restart a customer session, i.e., having to add cart items again from scratch
    private static readonly int MAX_CHECKOUT_ATTEMPTS = 3;

    protected virtual int GetMaxCheckoutAttempts()
    {
        return MAX_CHECKOUT_ATTEMPTS;
    }

    protected virtual string BuildCheckoutUrl()
    {
        return string.Format(BaseCheckoutUrl, this.customer.id);
    }

    protected override void SendCheckoutRequest(string tid)
    {
        string objStr = this.BuildCheckoutPayload(tid);
        StringContent payload = HttpUtils.BuildPayload(objStr);
        string url = this.BuildCheckoutUrl();
        int maxAttempts = this.GetMaxCheckoutAttempts();
        DateTime sentTs;
        int attempt = 0;
        try
        {
            bool success = false;
            HttpResponseMessage resp;
            do {
                sentTs = DateTime.UtcNow;
                resp = this.httpClient.Send(new(HttpMethod.Post, url)
                {
                    Content = payload
                });
                attempt++;
                success = resp.IsSuccessStatusCode;
            } while(!success && attempt < maxAttempts);

            if(success)
            {
                this.DoAfterSuccessSubmission(tid);
                this.submittedTransactions.Add(new(tid, TransactionType.CUSTOMER_SESSION, sentTs));
            } else
            {
                this.abortedTransactions.Add(new TransactionMark(tid, TransactionType.CUSTOMER_SESSION, this.customer.id, MarkStatus.ABORT, "cart"));
            }
        }
        catch (Exception e)
        {
            this.logger.LogError("Customer {0} Url {1}: Exception Message: {5} ", this.customer.id, url, e.Message);
            this.InformFailedCheckout();
        }
    }

    protected override void DoAfterCustomerSession()
    {
        // clean it for next customer session. besides, allow garbage collector to collect the items
        this.cartItems.Clear();
    }

    protected virtual void DoAfterSuccessSubmission(string tid)
    {
        if (this.config.trackTids)
        {
            // store
            this.tids.Add(tid);
        }
    }

    public override IDictionary<string, List<CartItem>> GetCartItemsPerTid(DateTime finishTime)
    {
        Dictionary<string,List<CartItem>> dict = new();
        foreach(var tid in tids){
            string url = this.config.cartUrl + "/" + this.customer.id + "/history/" + tid;
            var resp = this.httpClient.Send(new(HttpMethod.Get, url));
            if(resp.IsSuccessStatusCode){
                // var str = resp.Content.ReadAsStringAsync();
                using var reader = new StreamReader(resp.Content.ReadAsStream());
                var str = reader.ReadToEnd();
                dict.Add(tid, JsonConvert.DeserializeObject<List<CartItem>>(str));
            }
        }
        return dict;
    }

    protected string BuildCheckoutPayload(string tid)
    {
        // define payment type randomly
        var typeIdx = this.random.Next(1, 4);
        PaymentType type = typeIdx > 2 ? PaymentType.CREDIT_CARD : typeIdx > 1 ? PaymentType.DEBIT_CARD : PaymentType.BOLETO;
        int installments = type == PaymentType.CREDIT_CARD ? this.random.Next(1, 11) : 0;

        // build
        CustomerCheckout customerCheckout = new CustomerCheckout(
            customer.id,
            customer.first_name,
            customer.last_name,
            customer.city,
            customer.address,
            customer.complement,
            customer.state,
            customer.zip_code,
            type.ToString(),
            customer.card_number,
            customer.card_holder_name,
            customer.card_expiration,
            customer.card_security_number,
            customer.card_type,
            installments,
            tid
        );

        return JsonConvert.SerializeObject(customerCheckout);
    }

    private string BuildCartItem(Product product, int quantity)
    {
        // define voucher from distribution
        float voucher = 0;
        int probVoucher = this.random.Next(0, 101);
        if (probVoucher <= this.config.voucherProbability)
        {
            voucher = product.price * 0.10f;
        }

        // build a cart item
        CartItem cartItem = new CartItem(
                product.seller_id,
                product.product_id,
                product.name,
                product.price,
                product.freight_value,
                quantity,
                voucher,
                product.version
        );

        return JsonConvert.SerializeObject(cartItem); 
    }

}

