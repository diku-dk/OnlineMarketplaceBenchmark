using System.Collections.Concurrent;
using Common.Ingestion.Config;
using Common.Http;
using DuckDB.NET.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Common.Infra;

public sealed class CustomIngestionOrchestrator
{

	public static async Task Run(DuckDBConnection connection, IngestionConfig config)
	{
        var startTime = DateTime.UtcNow;
        var numThreads = config.concurrencyLevel <= 0 ? Environment.ProcessorCount : config.concurrencyLevel;
        Console.WriteLine("Ingestion process starting at {0} with strategy {1} and numWorkers {2}", startTime, config.strategy.ToString(), numThreads);

        var command = connection.CreateCommand();

        List<Task> tasksToWait = new();

        int total =  config.mapTableToUrl.Count;

        BlockingCollection<(JObject,string Url)> tuples = new BlockingCollection<(JObject,string Url)>();

        BlockingCollection<(string,string)> errors = new BlockingCollection<(string,string)>();

        // Console.WriteLine($"Setting up {config.mapTableToUrl.Count} utility workers to read tuples from internal database and parse them into JSON...");

        foreach (var entry in config.mapTableToUrl)
        {
            Console.WriteLine("Spawning table {0} load task at {1}", entry.Key, DateTime.UtcNow);
            command.CommandText = "select * from "+entry.Key+";";
            var queryResult = command.ExecuteReader();
            string table = entry.Value;
            if(!queryResult.HasRows) {
                Console.WriteLine($"No rows found in table {entry.Key}!");
                continue;
            }
            tasksToWait.Add(Task.Run(() => Produce(tuples, queryResult, table)));
        }

        Console.WriteLine($"All {config.mapTableToUrl.Count} utility workers submitted.");
        await Task.WhenAll(tasksToWait);
        Console.WriteLine($"All {config.mapTableToUrl.Count} utility workers finished.");

        Console.WriteLine($"Setting up {numThreads} worker threads to send parsed records to target microservices...");

        for (int i = 0; i < numThreads; i++) {
             tasksToWait.Add( Task.Run(() => ConsumeShared(tuples, errors)) );
        }
        
        await Task.WhenAll(tasksToWait);
        Console.WriteLine();
        Console.WriteLine("Finished loading all tables.");

        if(errors.Count > 0)
        {
            Console.WriteLine("Errors found while ingesting. An example:");
            var errorEntry = errors.Take();
            Console.WriteLine(errorEntry.Item1+" "+errorEntry.Item2);
        }

        TimeSpan span = DateTime.UtcNow - startTime;
        Console.WriteLine("Ingestion process has terminated in {0} seconds", span.TotalSeconds);
        Console.WriteLine("Collecting garbage from ingestion process...");
        GC.Collect();
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
                ConvertAndSend(item.Tuple, item.Url, errors);
            }
            catch (Exception e)
            {
                errors.Add((item.Url,e.Message));
            }
        }
    }

    private static void ConvertAndSend(JObject obj, string url, BlockingCollection<(string,string)> errors)
    {
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = HttpUtils.BuildPayload(JsonConvert.SerializeObject(obj))
        };
        var response = HttpUtils.HTTP_CLIENT.Send(message, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            errors.Add((url,response.StatusCode+" : "+response.ReasonPhrase));
        }
    }

}

