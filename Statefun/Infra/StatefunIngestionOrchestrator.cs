using System.Collections.Concurrent;
using Common.Ingestion.Config;
using DuckDB.NET.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Common.Http;

namespace Statefun.Infra;

public sealed class StatefunIngestionOrchestrator
{

	public static async Task Run(DuckDBConnection connection, IngestionConfig config)
	{
        var startTime = DateTime.UtcNow;    
        var numThreads = config.concurrencyLevel <= 0 ? Environment.ProcessorCount : config.concurrencyLevel;
        Console.WriteLine("Ingestion process starting at {0} with strategy {1} and numWorkers {2}", startTime, config.strategy.ToString(),numThreads);

        var command = connection.CreateCommand();

        List<Task> tasksToWait = new();

        int total =  config.mapTableToUrl.Count;

        BlockingCollection<(JObject,string Url)> tuples = new BlockingCollection<(JObject,string Url)>();

        BlockingCollection<(string,string)> errors = new BlockingCollection<(string,string)>();

        foreach (var table in config.mapTableToUrl)
        {
            command.CommandText = "select * from "+table.Key+";";
            var queryResult = command.ExecuteReader();
            tasksToWait.Add(Task.Run(() => Produce(tuples, queryResult, table.Value)));
        }

        await Task.WhenAll(tasksToWait);
        Console.WriteLine("Finished loading all record in memory.");
        tasksToWait.Clear();

        // Statefun : get the current read record before starting ingestion        

        //TODO : put url in config
        string baseUrl = "http://statefunhost:8081/";
        long recordBeforeIngest = await getCurrentReadRecord(baseUrl);
        long recordNow = recordBeforeIngest;
        long totalTuples = tuples.Count;
        long recordExpectedAfterIngest = recordBeforeIngest + totalTuples;

        for (int i = 0; i < numThreads; i++) {
             tasksToWait.Add( Task.Run(() => ConsumeShared(tuples, errors)) );
        }    

        await Task.WhenAll(tasksToWait);

        Console.WriteLine();
        Console.WriteLine("Finished ingesting all tables.");

        if(errors.Count > 0)
        {
            Console.WriteLine("Errors found while ingesting. An example:");
            var errorEntry = errors.Take();
            Console.WriteLine(errorEntry.Item1+" "+errorEntry.Item2);
        }

        do
        {
            recordNow = await getCurrentReadRecord(baseUrl);
            Console.WriteLine("Waiting for read all data, {0}/{1}", recordNow, recordExpectedAfterIngest);
            await Task.Delay(500);
        } while (recordNow != recordExpectedAfterIngest);

        TimeSpan span = DateTime.UtcNow - startTime;
        Console.WriteLine("Ingestion process has terminated in {0} seconds", span.TotalSeconds);
    }

    private static void Produce(BlockingCollection<(JObject,string Url)> tuples, DuckDBDataReader queryResult, string Url)
    {
        while (queryResult.Read())
        {
            JObject obj = new JObject();
            for (int ordinal = 0; ordinal < queryResult.FieldCount; ordinal++)
            {
                var column = queryResult.GetName(ordinal);
                var val = queryResult.GetValue(ordinal);
                obj[column] = JToken.FromObject(val);
            }
            tuples.Add((obj,Url));
        }
    }

    private static void ConsumeShared(BlockingCollection<(JObject Tuple, string Url)> tuples, BlockingCollection<(string,string)> errors)
    {
        while (tuples.TryTake(out (JObject Tuple, string Url) item))
        {
            try
            {
                ConvertAndSend(item.Tuple, item.Url);
            }
            catch (Exception e)
            {
                errors.Add((item.Url,e.Message));
            }
        }
    }

    // CHANGE THIS FOR STATEFUN
    private static void ConvertAndSend(JObject obj, string url)
    {
        string partitionKey = "";
        string baseContentType = "application/vnd.marketplace/";
        string eventType = "";
        string lastSegment = url.Substring(url.LastIndexOf('/') + 1);
        if (lastSegment == "seller") 
        {
            partitionKey = obj["id"].ToString();
            eventType = "SetSeller";
        } 
        else if (lastSegment == "customer") 
        {
            partitionKey = obj["id"].ToString();
            eventType = "SetCustomer";
        } 
        else if (lastSegment == "product")
        {
            partitionKey = string.Concat(obj["seller_id"].ToString(), "-", obj["product_id"].ToString());
            eventType = "UpsertProduct";
        } 
        else if (lastSegment == "stock")
        {
            partitionKey = string.Concat(obj["seller_id"].ToString(), "-", obj["product_id"].ToString());
            eventType = "SetStockItem";
        }
        else 
        {
            throw new Exception("Unknown url: "+url);
        }
        
        string apiUrl = string.Concat(url, "/", partitionKey);
        string payLoad = JsonConvert.SerializeObject(obj);
        string contentType = string.Concat(baseContentType, eventType);
        StatefunUtils.SendHttpToStatefun(HttpUtils.HTTP_CLIENT, apiUrl, contentType, payLoad).Wait();
    }

    public static async Task<long> getCurrentReadRecord(string stateFunUrl)
    {
        using (HttpClient client = new HttpClient())
        {
            try
            {                                        
                HttpResponseMessage response = await client.GetAsync(stateFunUrl + "jobs");
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();                
                JObject responseJson = JObject.Parse(responseBody);                
                string jobId = (string)responseJson["jobs"][0]["id"];
                                    
                string newUrl = stateFunUrl + "jobs/" + jobId;
                response = await client.GetAsync(newUrl);
                response.EnsureSuccessStatusCode();
                responseBody = await response.Content.ReadAsStringAsync();
                responseJson = JObject.Parse(responseBody);                    

                JObject targetVertex = responseJson["vertices"].FirstOrDefault(v => (string)v["name"] == "feedback-union -> functions -> Sink: io.statefun.playground-egress-egress") as JObject;
                long readRecords = (long)targetVertex["metrics"]["read-records"];
                return readRecords;                    
            }
            catch (HttpRequestException ex)
            {                    
                Console.WriteLine($"Exception: {ex.Message}");
            }
            return 0;
        }
    }
}

