using Common.Experiment;
using Common.Http;
using Common.Infra;
using DriverBench.Experiment;
using DuckDB.NET.Data;

namespace DriverBench;

public sealed class Program
{

    public static void Main(string[] args)
    {
        Console.WriteLine("Initializing benchmark driver...");
        ExperimentConfig config = ConsoleUtility.BuildExperimentConfig(args);
        Console.WriteLine("Configuration parsed. Starting program...");
        DuckDBConnection? connection = null;

        try
        {
            while (true)
            {
                Console.WriteLine("\n Select an option: \n 1 - Generate Data \n 2 - Run Scalability Experiment \n q - Exit");
                string? op = Console.ReadLine();

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
                                Console.WriteLine("Assuming data exists in "+ config.connectionString);
                                connection = new DuckDBConnection(config.connectionString);
                                connection.Open();
                            }
                        }
                        var expManager = DriverBenchExperimentManager.BuildDriverBenchExperimentManager(CustomHttpClientFactory.GetInstance(), config, connection);
                        expManager.RunSimpleExperiment();
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

