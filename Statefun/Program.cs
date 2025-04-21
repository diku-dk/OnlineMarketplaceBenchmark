using Common.Experiment;
using Common.Http;
using Common.Infra;
using DuckDB.NET.Data;
using Statefun.Infra;
using Statefun.Workload;

namespace Statefun;

public class Program
{

    public static async Task Main(string[] args)
    {
        Console.WriteLine("Initializing benchmark driver...");
        ExperimentConfig config = ConsoleUtility.BuildExperimentConfig(args);
        Console.WriteLine("Configuration parsed. Starting program...");
        DuckDBConnection connection = null;

        try
        {
            while (true)
            {

                Console.WriteLine("\n Select an option: \n 1 - Generate Data \n 2 - Ingest Data \n 3 - Run Experiment \n 4 - Ingest and Run (2 and 3) \n 5 - Parse New Configuration \n q - Exit");
                string op = Console.ReadLine();

                switch (op)
                {
                    case "1":
                    {
                        connection = ConsoleUtility.GenerateData(config);
                        break;
                    }
                    case "2":
                    {
                        if (connection is null)
                        {
                            if (config.connectionString.SequenceEqual("DataSource=:memory:"))
                            {
                                Console.WriteLine("Please generate some data first by selecting option 1.");
                                break;
                            }
                            else
                            {
                                connection = new DuckDBConnection(config.connectionString);
                                connection.Open();
                            }
                        }
                        await StatefunIngestionOrchestrator.Run(connection, config.ingestionConfig);
                        GC.Collect();
                        break;
                        }
                    case "3":
                    {
                        if (connection is null)
                        {
                            if (config.connectionString.SequenceEqual("DataSource=:memory:"))
                            {
                                Console.WriteLine("Please generate some data first by selecting option 1.");
                                break;
                            }
                            else
                            {
                                connection = new DuckDBConnection(config.connectionString);
                                connection.Open();
                            }
                        }
                        var expManager = StatefunExperimentManager.BuildStatefunExperimentManager(CustomHttpClientFactory.GetInstance(), config, connection);
                        await expManager.Run();
                        Console.WriteLine("Experiment finished.");
                        break;
                    }
                    case "4":
                    {
                        if (connection is null)
                        {
                            if (config.connectionString.SequenceEqual("DataSource=:memory:"))
                            {
                                Console.WriteLine("Please generate some data first by selecting option 1.");
                                break;
                            }
                            else
                            {
                                connection = new DuckDBConnection(config.connectionString);
                                connection.Open();
                            }
                        }
                        // ingest data
                        await DefaultIngestionOrchestrator.Run(connection, config.ingestionConfig);
                        Console.WriteLine("Delay after ingest...");
                        await Task.Delay(10000);
                        var expManager = StatefunExperimentManager.BuildStatefunExperimentManager(CustomHttpClientFactory.GetInstance(), config,connection);
                        await expManager.Run();
                        Console.WriteLine("Experiment finished.");
                        break;
                    }
                    case "5":
                    {
                        config = ConsoleUtility.BuildExperimentConfig(args);
                        Console.WriteLine("Configuration parsed.");
                        break;
                    }
                    case "q":
                    {
                        return;
                    }
                    default:
                    {
                        Console.WriteLine("Input invalid");
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception catched. Source: {0}; StackTrace: \n {1}", e.Source, e.StackTrace);
        }
    }

}


