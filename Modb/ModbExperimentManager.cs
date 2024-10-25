using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Common.Entities;
using Common.Experiment;
using Common.Infra;
using Common.Metric;
using Common.Workers.Delivery;
using Common.Workload;
using Common.Workload.Metrics;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using static Common.Services.CustomerService;
using static Common.Services.DeliveryService;
using static Common.Services.SellerService;

namespace Modb;

public sealed class ModbExperimentManager : AbstractExperimentManager
{
    private readonly ModbPollingTask modbPollingTask;

    public static ModbExperimentManager BuildModbExperimentManager(IHttpClientFactory httpClientFactory, ExperimentConfig config, DuckDBConnection duckDBConnection)
    {
        return new ModbExperimentManager(httpClientFactory, ModbSellerWorker.BuildSellerWorker, ModbCustomerWorker.BuildCustomerWorker, DefaultDeliveryWorker.BuildDeliveryWorker, config, duckDBConnection);
    }

    public ModbExperimentManager(IHttpClientFactory httpClientFactory, BuildSellerWorkerDelegate sellerWorkerDelegate, BuildCustomerWorkerDelegate customerWorkerDelegate, BuildDeliveryWorkerDelegate deliveryWorkerDelegate, ExperimentConfig config, DuckDBConnection duckDBConnection) : base(httpClientFactory, WorkloadManager.BuildWorkloadManager, MetricManager.BuildMetricManager, sellerWorkerDelegate, customerWorkerDelegate, deliveryWorkerDelegate, config, duckDBConnection)
    {
        // must be at least same as batch window in modb
        this.modbPollingTask = new ModbPollingTask(this.config.pollingUrl, this.config.pollingRate);
    }

    public new void RunSimpleExperiment()
    {
        this.customers = DuckDbUtils.SelectAll<Customer>(this.connection, "customers");
        this.PreExperiment();
        this.PreWorkload(0);
        this.workloadManager.SetUp(new Interval(1, this.numSellers), this.config.runs[0].sellerDistribution, this.config.runs[0].sellerZipfian);
        this.metricManager.SetUp(this.numSellers, this.config.numCustomers);

        var tokenSource = new CancellationTokenSource();
        Task<long> pollingTask = Task.Run(() => this.modbPollingTask.Run(tokenSource.Token));

        // let first TID be polled
        Thread.Sleep(1);
        
        (DateTime startTime, DateTime finishTime) = this.workloadManager.Run(tokenSource);
       
        // wait for completion
        while(!pollingTask.IsCompleted){ }

        if(pollingTask.IsCompletedSuccessfully)
        {
            // fill missing tx output entries
            foreach(var entry in BatchTrackingUtils.tidToBatchMap)
            {
                if (!BatchTrackingUtils.batchToFinishedTsMap.ContainsKey(entry.Value.batchId))
                {
                    continue;
                }
                var finishedTs = BatchTrackingUtils.batchToFinishedTsMap[entry.Value.batchId];
                TransactionOutput transactionOutput = new TransactionOutput(entry.Key, finishedTs);
                if(entry.Value.transactionType == TransactionType.CUSTOMER_SESSION){
                    customerService.AddFinishedTransaction(entry.Value.workerId, transactionOutput);
                } else
                {
                    sellerService.AddFinishedTransaction(entry.Value.workerId, transactionOutput);
                }
            }

            // this.metricManager.SimpleCollect(startTime, finishTime, pollingTask.Result);
            this.metricManager.Collect(startTime, finishTime, this.config.epoch);
            if (this.WaitCompletion())
            {
                this.PostExperiment();
            }
        }
        else
        {
            LOGGER.LogWarning("Polling task has not finished correctly!");
            this.metricManager.SimpleCollect(startTime, finishTime, 0);
        }

        CollectGarbage();
    }

    private bool WaitCompletion()
    {
        int maxAttempts = 10;
        long lastCommittedTid;
        long lastSubmittedTid;
        try {
            do
            {
                Thread.Sleep(1000);
                lastSubmittedTid = this.modbPollingTask.PollLastSubmittedTid();
                LOGGER.LogInformation($"Last submitted TID retrieved: {lastSubmittedTid}");
                lastCommittedTid = this.modbPollingTask.PollLastCommittedTid();
                LOGGER.LogInformation($"Last committed TID retrieved: {lastCommittedTid}");
                maxAttempts--;
            } while (lastCommittedTid != lastSubmittedTid && maxAttempts > 0);
            Thread.Sleep(2000);
            if(lastCommittedTid == lastSubmittedTid) return true;
            return false;
        } catch(Exception e)
        {
            LOGGER.LogError($"Error caught: {e}");
            return false;
        }
    }

}

