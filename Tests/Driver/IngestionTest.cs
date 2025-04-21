using Common.DataGeneration;
using Common.Ingestion;
using Common.Ingestion.Config;
using DuckDB.NET.Data;

namespace Tests.Driver;

public class IngestionTest
{
    [Fact]
	public async void TestDiskIngestion()
	{
        var connection = new DuckDBConnection("Data Source=file.db");
        connection.Open();

		SyntheticDataSourceConfig dataSourceConfig = new()
        {
            numCustomers = 10000,
            numProducts = 10000,
            numProdPerSeller = 10
        };

        var dataGen = new SyntheticDataGenerator(dataSourceConfig);
        dataGen.CreateSchema(connection);
        dataGen.Generate(connection, true);

        IngestionConfig ingestionConfig = new()
        {
            connectionString = "Data Source=file.db",
            strategy = IngestionStrategy.WORKER_PER_CPU,
            concurrencyLevel = 4,
            mapTableToUrl = new Dictionary<string, string>()
            {
                { "sellers", "http://localhost:5006" },
                { "customers", "http://localhost:5007" },
                { "stock_items", "http://localhost:8080/stock" },
                { "products", "http://localhost:8080/product"}
            }
        };

        await StrategyIngestionOrchestrator.Run(connection, ingestionConfig, true);

    }

	[Fact]
	public async void TestInMemoryIngestion()
	{

        if(OperatingSystem.IsWindows())
        {
            Console.WriteLine("DuckDB library does not work in Windows platform. The test will not run.");
            Assert.True(true);
            return;
        }

		var connection = new DuckDBConnection("DataSource=:memory:");

        connection.Open();

		SyntheticDataSourceConfig dataSourceConfig = new()
        {
            numCustomers = 10,
            numProducts = 10,
            numProdPerSeller = 2
        };

        var dataGen = new SyntheticDataGenerator(dataSourceConfig);
        dataGen.CreateSchema(connection);
        // dont need to generate customers on every run. only once
        // dataGen.GenerateCustomers(connection);
        dataGen.Generate(connection, true);

        IngestionConfig ingestionConfig = new()
        {
            connectionString = "DataSource=:memory:",
            strategy = IngestionStrategy.WORKER_PER_CPU,
            concurrencyLevel = 4,
            mapTableToUrl = new Dictionary<string, string>()
            {
                //{ "sellers" , "http://localhost:8080/seller" },
                //{ "customers" , "http://localhost:8080/customer" },
                //{ "stock_items", "http://localhost:8080/stock" },
                { "products", "http://localhost:8080/product"}
            }
        };

        await StrategyIngestionOrchestrator.Run(connection, ingestionConfig, true);

        // retrieve some random and see if they match

	}
}

