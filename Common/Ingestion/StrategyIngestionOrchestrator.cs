using System.Collections.Concurrent;
using System.Reflection;
using Common.Ingestion.Config;
using Common.Http;
using DuckDB.NET.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Common.Ingestion;

/**
 * Only used by Dapr and tests at the moment.
 * The default ingestion orchestrator serves in most cases
 */
public sealed class StrategyIngestionOrchestrator
{

	public static async Task Run(DuckDBConnection connection, IngestionConfig config, bool debug = false)
	{
        var startTime = DateTime.UtcNow;
        Console.WriteLine("Ingestion process starting at {0} with strategy {1}", startTime, config.strategy.ToString());

        var command = connection.CreateCommand();

        List<Task> tasksToWait = new();

        int idx = 0;
        int total =  config.mapTableToUrl.Count;
        
        Console.WriteLine();

        foreach (var table in config.mapTableToUrl)
        {
           Console.WriteLine("Spawning table {0} load task at {1}", table.Key, DateTime.UtcNow);

            command.CommandText = "select * from "+table.Key+";";
            var queryResult = command.ExecuteReader();

            BlockingCollection<JObject> tuples = new BlockingCollection<JObject>();

            Task t1 = Task.Run(() => Produce(tuples, queryResult));

            long rowCount = GetRowCount(queryResult);

            if(rowCount == 0)
            {
                Console.WriteLine("Table {0} is empty!", table);
                continue;
            }

            if (config.strategy == IngestionStrategy.TABLE_PER_WORKER)
            {
                TaskCompletionSource tcs = new TaskCompletionSource();
                Task t = Task.Run(() => Consume(tuples, table.Value, rowCount, tcs, debug));
                tasksToWait.Add(tcs.Task);
            }
            else if (config.strategy == IngestionStrategy.WORKER_PER_CPU)
            {
                var numThreads = config.concurrencyLevel <= 0 ? 1 : config.concurrencyLevel;
                for (int i = 0; i < numThreads; i++) {
                    TaskCompletionSource tcs = new TaskCompletionSource();
                    Task t = Task.Run(() => ConsumeShared(tuples, table.Value, rowCount, tcs, debug));
                    tasksToWait.Add(tcs.Task);
                }
                await Task.WhenAll(tasksToWait);

                idx++;

                totalCount = 0;
                tasksToWait.Clear();

                Console.WriteLine();
                Console.WriteLine("Finished loading table {0} at {1}", table, DateTime.UtcNow);

            }
            else // default to single worker
            {
                TaskCompletionSource tcs = new TaskCompletionSource();
                Task t = Task.Run(() => Consume(tuples, table.Value, rowCount, tcs, debug));
                await tcs.Task;
                Console.WriteLine("Finished loading table {0}", table);
            }
        }

        if(tasksToWait.Count > 0)
        {
            await Task.WhenAll(tasksToWait);
            Console.WriteLine("Finished loading all tables");
        }

        TimeSpan span = DateTime.UtcNow - startTime;
        Console.WriteLine("Ingestion process has terminated in {0} seconds", span.TotalSeconds);
    }

    private static void Produce(BlockingCollection<JObject> tuples, DuckDBDataReader queryResult)
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
            tuples.Add(obj);
        }
    }

    private static readonly BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static long GetRowCount(DuckDBDataReader queryResult)
    {
        var field = queryResult.GetType().GetField("rowCount", bindingFlags);
        return (long)field?.GetValue(queryResult);
    }

    private static int totalCount = 0;

    private static void ConsumeShared(BlockingCollection<JObject> tuples, string url, long rowCount, TaskCompletionSource tcs, bool debug)
    {
        JObject jobject;
        do
        {
            bool taken = tuples.TryTake(out jobject);
            if (taken)
            {
                Interlocked.Increment(ref totalCount);
                ConvertAndSend(jobject, url, debug);
            }
        } while (Volatile.Read(ref totalCount) < rowCount);
        tcs.SetResult();
    }

    private static void Consume(BlockingCollection<JObject> tuples, string url, long rowCount, TaskCompletionSource tcs, bool debug)
    {
        int currRow = 1;
        do
        {
            JObject obj = tuples.Take();
            ConvertAndSend(obj, url, debug);
            currRow++;
        } while (currRow <= rowCount);
        tcs.SetResult();
    }

    private static void ConvertAndSend(JObject obj, string url, bool debug)
    {
        string strObj = JsonConvert.SerializeObject(obj);
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = HttpUtils.BuildPayload(strObj)
        };
        if (debug)
        {
            Console.WriteLine(strObj);
            return;
        }
        try
        {
            using HttpResponseMessage response = HttpUtils.HTTP_CLIENT.Send(message);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception message: {0}", e.Message);
        }
    }

}

