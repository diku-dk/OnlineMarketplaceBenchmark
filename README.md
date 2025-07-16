# Online Marketplace: A Benchmark for Data Management in Microservices

Online Marketplace is a benchmark modeling an event-driven microservice system in the marketplace application domain. It is designed to reflect emerging data management requirements and challenges faced by microservice developers in practice. This project contains the source code for the driver for the Online Marketplace benchmark. The driver is responsible to manage the lifecycle of an experiment, including data generation, data population, workload submission, and metrics collection.

:exclamation: Looking for SIGMOD 2025 reproducibility? :exclamation: Jump straight to it by clicking on [Reproducibility](#reproducibility) :boom:

## Table of Contents
- [Online Marketplace Benchmark](#marketplace)
    * [Implementations](#implementations)
    * [Required APIs](#apis)
- [Benchmark Driver](#driver)
    * [Prerequisites](#prerequisites)
    * [Directory Structure](#structure)
    * [Data Generation](#data)
    * [Configuration](#config)
    * [Running an Experiment](#run)
- [Advanced Details](#advanced)
    * [Tracking Replication Anomalies](#replication)
    * [Driver Performance](#performance)
    * [Future Work](#future)
    * [Troubleshooting](#troubleshooting)
- [Reproducibility](#reproducibility)

## <a name="marketplace"></a>Online Marketplace Benchmark

For an in-depth discussion and explanation of the benchmark, please refer to our full paper at SIGMOD '25:

```bibtex
@article{10.1145/3709653,
author = {Laigner, Rodrigo and Zhang, Zhexiang and Liu, Yijian and Gomes, Leonardo Freitas and Zhou, Yongluan},
title = {Online Marketplace: A Benchmark for Data Management in Microservices},
year = {2025},
issue_date = {February 2025},
publisher = {Association for Computing Machinery},
address = {New York, NY, USA},
volume = {3},
number = {1},
url = {https://doi.org/10.1145/3709653},
doi = {10.1145/3709653},
journal = {Proc. ACM Manag. Data},
month = feb,
articleno = {3},
numpages = {26},
keywords = {benchmark, data management, microservices, online marketplace}
}
```

### <a name="implementations"></a>Implementations

There are three stable implementations of the application prescribed by the Online Marketplace benchmark available: [Orleans](https://github.com/diku-dk/MarketplaceOnOrleans), [Statefun](https://github.com/diku-dk/MarketplaceOnStatefun), and [Dapr](https://github.com/rnlaigner/MarketplaceOnDapr). In order to run experiments targeting one of the platforms, refer to their respective repositories since they contain specific instructions as to how to configure and deploy the platform.

### <a name="apis"></a>Required APIs

In case you are looking to implement the application prescribed by Online Marketplace benchmark in another platform,
 some HTTP APIs are required to be exposed by the platform prior to workload submission. The list of HTTP APIs is as follows.


API                  | HTTP Request Type  | Miroservice    |  Description |
-------------------- |------------------- |--------------- |--------------|
/cart/{customerId}/add | PUT | Cart | Add a product to a customer's cart |
/cart/{customerId}/checkout | POST | Cart | Checkout a cart |
/cart/{customerId}/seal | POST | Cart | Reset a cart |
/customer | POST | Customer | Register a new customer |
/product  | POST  | Product | Register a new product |
/product  | PATCH | Product | Update a product's price |
/product  | PUT   | Product | Replace a product |
/seller   | POST  | Seller | Register a new seller |
/seller/dashboard/{sellerId} | GET | Seller | Retrieve seller's dashboard for a given a seller |
/shipment/{tid} | PATCH | Shipment | Update packages to 'delivered' status | 
/stock | POST | Stock | Register a new stock item |

For the requests that modify microservices' state (POST/PATCH/PUT), refer to classes present in [Entities](Common/Entities) to understand the expected payload. We strongly recommend analyzing the subprojects [Orleans](Orleans) and [Statefun](Statefun) to understand how to extend the driver to run experiments in other platforms.

## <a name="driver"></a>Benchmark Driver

The benchmark driver is written in C# and takes advantage of the thread management facilities provided by the .NET framework.

### <a name="prerequisites"></a>Prerequisites

* [.NET Framework 7](https://dotnet.microsoft.com/en-us/download/dotnet/7.0)
* A multi-core machine with appropriate memory size in case generated data is kept in memory
* Linux- or MacOS-based operating system

In case you want to modify, extend, or debug the benchmark driver, we recommend an IDE, which can be [Visual Studio](https://visualstudio.microsoft.com/vs/community/) or [VSCode](https://code.visualstudio.com/).

## <a name="structure"></a>Project Structure

The directory [Common](Common) contains base classes for implementing a platform-specific subproject. Inside this directory, the following subdirectories are found:

* [DataGeneration](Common/DataGeneration) 
    The different types of data generator classes.
* [Entities](Common/Entities)
    The entities that form HTTP request payloads.
* [Experiment](Common/Experiment)
    The experiment manager and experiment configuration classes.
* [Http](Common/Http)
    Utilities for HTTP processing.
* [Infra](Common/Infra)
    Utilities for console, logging, DuckDB, and thread management.
* [Ingestion](Common/Ingestion)
    Data ingestion orchestrators to preload components' states
* [Metric](Common/Metric)
    The metric manager that process and calculate experiment metrics
* [Requests](Common/Requests)
    The request types that form HTTP payloads.
* [Services](Common/Services)
    The classes that abstract away and facilitate the registering of and access to metrics collected
* [Streaming](Common/Streaming)
    Configuration classes for events related to marking the end of a long-running business transaction.
* [Workers](Common/Workers)
    The classes that abstract away specific implementations of application clients (i.e., customers, sellers, delivery service)
* [Workload](Common/Workload)
    The workload manager that ensures the empriment workload adheres to the workload configuration provided by the user

The directories [Dapr](Dapr), [Statefun](Statefun), and [Orleans](Orleans) relies upon (and extends) many of the above classes to ensure the driver core functionalities remain functional in each respective platform. For example, due to the synchrnous RPC nature of Orleans, the use of events to mark the end of a long-running business transaction is not necessary, reason why the workload manager is overriden.

The directory [Tests](Tests) contain varied unit tests to ensure the correctness of the requests sent to a platform.

### <a name="data"></a>Data Generation

The driver uses [DuckDB](https://duckdb.org/why_duckdb) to store and query generated data during the workload submission. Besides storing data in DuckDB filesystem, it is worthy noting that users can also generate data in memory to use in experiments. More info about can be found in [Config](#config). The benefit of persisting data in DuckDB is that such data can be safely reused in other experiments, thus decreasing experiment runs' overall time.

The library [DuckDB.NET](https://github.com/Giorgi/DuckDB.NET) is used to bridge .NET with DuckDB. However, the library only supports Unix-based operating systems right now. As the driver depends on the data stored in DuckDB, unfortunately it is not possible to run the benchmark in Windows-based operating systems.

Furthermore, we use additional libraries to support the data generation process. [Dapper](https://github.com/DapperLib/Dapper) is used to map rows to objects. [Bogus](https://github.com/bchavez/Bogus) is used to generate realistic synthetic data.

### <a name="config"></a>Configuration

The driver requires a configuration file to be passed as input at startup. The configuration prescribes several important aspects of the experiment and the driver's behavior, including but not limited to the transaction ratio, the target microservice API addresses, the data set parameters, the degree of concurrency, and more. An example configuration, including comments included when the parameter name is not auto-explanable, is shown below.

```
{
    "connectionString": "Data Source=file.db", // it defines the data source. if in-memory, set "Data Source=:memory"
    "numCustomers": 100000,
    "numProdPerSeller": 10,
    "qtyPerProduct": 10000,
    "executionTime": 60000, // it defines the total time of each experiment run 
    "epoch": 10000, // it defines whether the output result will partition the metric results by epoch
    "delayBetweenRequests": 0,
    "delayBetweenRuns": 0,
    // transaction ratio
    "transactionDistribution": {
        "CUSTOMER_SESSION": 30,
        "QUERY_DASHBOARD": 35,
        "PRICE_UPDATE": 38,
        "UPDATE_PRODUCT": 40,
        "UPDATE_DELIVERY": 100
    },
    "concurrencyLevel": 48,
    "ingestionConfig": {
        "strategy": "WORKER_PER_CPU",
        "concurrencyLevel": 32,
        // mandatory to load initial data in microservices
        "mapTableToUrl": {
            "sellers": "http://orleans:8081/seller",
            "customers": "http://orleans:8081/customer",
            "stock_items": "http://orleans:8081/stock",
            "products": "http://orleans:8081/product"
        }
    },
    // it defines the possible multiple runs this experiment contains (an entry per run)
    "runs": [
        {
            "numProducts": 100000,
            "sellerDistribution": "UNIFORM",
            "keyDistribution": "UNIFORM"
        }
    ],
    // it defines the APIs that should be contact at the end of every run
    "postRunTasks": [
    ],
    // it defines the APIs that should be contact at the end of the experiment
    "postExperimentTasks": [
        {
            "name": "cleanup",
            "url": "http://orleans:8081/cleanup"
        }
    ],
    // it defines aspects related to a customer session
    "customerWorkerConfig": {
        "maxNumberKeysToAddToCart": 10,
        "minMaxQtyRange": {
            "min": 1,
            "max": 10
        },
        "checkoutProbability": 100,
        "voucherProbability": 5,
        "productUrl": "http://orleans:8081/product",
        "cartUrl": "http://orleans:8081/cart",
        // track which tids have been submitted
        "trackTids": true
    },
    // it defines aspects related to a seller transaction
    "sellerWorkerConfig": {
        // adjust price percentage range
        "adjustRange": {
            "min": 1,
            "max": 10
        },
        "sellerUrl": "http://orleans:8081/seller",
        "productUrl": "http://orleans:8081/product",
        // track product update history
        "trackUpdates": false
    },
    "deliveryWorkerConfig": {
        "shipmentUrl": "http://orleans:8081/shipment"
    }
}

```

Other example configuration files are found in [Configuration](Configuration).

### <a name="run"></a>Running an Experiment

Once the configuration is set and assuming the target data platform is up and running (i.e., ready to receive requests), we can initialize the benchmark driver process. In the project root folder, run the following commands for the respective platforms:

- Orleans
```
dotnet run --project Orleans <configuration file path>
```

- Statefun
```
dotnet run --project Statefun <configuration file path>
```

In both cases, the following menu will be shown to the user:

```
 Select an option:
 1 - Generate Data
 2 - Ingest Data
 3 - Run Experiment
 4 - Ingest and Run (2 and 3)
 5 - Parse New Configuration
 q - Exit
```

Through the menu, the user can select specific benchmark tasks, including data generation (1), data ingestion into the data platform (2), and workload submission (3). In case the configuration file has been modified, one can also request the driver to read the new configuration (5) without the need to restart the driver.

At the end of an experiment cycle, the results collected along the execution are shown in the screen and stored automatically in a text file. The text file indicates the execution time, as well as some of the parameters used for faster identification of a specific run.

## <a name="advanced"></a>Advanced Details

### <a name="replication"></a>Tracking Replication Correctness

The Online Marketplace implementation targeting [Microsoft Orleans](https://github.com/diku-dk/MarketplaceOnOrleans) supports  tracking the cart history (make sure that the options ```StreamReplication``` and ```TrackCartHistory``` are set to true). By tracking the cart history, we can match the items in the carts with the history of product updates. That enables the identification of possible causal anomalies related to updates in multiple objects.

To enable such anomaly detection in the driver, make sure the options "trackTids" in ```customerWorkerConfig``` and "trackUpdates" in ```sellerWorkerConfig``` in the configuration file are set to true. By tracking the history of TIDs for each customer cart, we can request customer actors in Orleans about the content of their respective carts submitted for checkout. With the cart history, we match historic cart items with the history of product updates (tracked by driver's seller workers) to identify anomalies. 

We understand these settings are sensible and prone to error. We expect to improve such settings in the near future.

### <a name="performance"></a>Driver Performance

The project DriverBench can run simulated workload to test the driver scalability. That is, the driver's ability to submit more requests as more computational resources are added.

There are three impediments that refrain the driver from scaling:

- (a) Insufficient computational resources
- (b) Contended workload
- (c) The target platform

(a) can be mitigated with more CPUs and memory (to hold data in memory if necessary).

(b) does not occur if uniform distribution is used. However, when using non-uniform distribution, the task is tricky because there could be some level of synchronization in the driver to make sure updates to a product are linearizable. Adjusting the zipfian constant can alleviate the problem in case non-uniform distribution is really necessary.

(c) can be mitigated by (i) tuning the target data platform, (ii) increasing computational resources in the target platform, (iii) co-locating the driver with the data platform (remove network latency).

### <a name="future"></a>Future Work

We intend to count the "add item to cart" operation as a measured query in the driver. In the current implementation, although the add item operation is not counted as part of the latency of a customer checkout, capturing the cost of an "add item" allows capturing the overall latency of the customer session as a whole and not only the checkout operation.

### <a name="troubleshooting"></a>Troubleshooting

The following links provide useful pointers for troubleshooting possible deployment and performance issues with experiments.

- [How to copy files to output directory](https://stackoverflow.com/questions/44374074/copy-files-to-output-directory-using-csproj-dotnetcore)
- [What process is listening to a given port?](https://stackoverflow.com/questions/4421633/who-is-listening-on-a-given-tcp-port-on-mac-os-x)
- [Orleans Docker deployment](http://sergeybykov.github.io/orleans/1.5/Documentation/Deployment-and-Operations/Docker-Deployment.html)
- [Interlocked](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.increment?view=net-7.0&redirectedfrom=MSDN#System_Threading_Interlocked_Increment_System_Int32__)
- [Locust](https://github.com/GoogleCloudPlatform/microservices-demo/blob/main/src/loadgenerator/locustfile.py)
- [.NET HTTP client optimization](https://www.stevejgordon.co.uk/using-httpcompletionoption-responseheadersread-to-improve-httpclient-performance-dotnet)
- [.NET HTTP client connection pooling](https://www.stevejgordon.co.uk/httpclient-connection-pooling-in-dotnet-core)
- [.NET HTTP client timeout handling](https://thomaslevesque.com/2018/02/25/better-timeout-handling-with-httpclient/)

## <a name="reproducibility"></a>SIGMOD Reproducibility

### Source Code (for Artifact Availability)

The source code of all the projects required for reproducibility steps can be downloaded as follows:

- [Benchmark driver](https://github.com/diku-dk/OnlineMarketplaceBenchmark/archive/refs/tags/v1.0.zip)
- [Orleans](https://github.com/diku-dk/MarketplaceOnOrleans/archive/refs/tags/v1.0.zip)
- [Statefun](https://github.com/diku-dk/MarketplaceOnStatefun/archive/refs/tags/v1.0.zip)

### Configuration Instructions (to ensure Artifacts are Functional)

The instructions provided in this section assume the usage of the Linux distribution [Ubuntu](https://ubuntu.com/) 22.10. 

To setup the benchmark driver, we must install .NET 7 using the following command:

```
sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-7.0
```

To setup Orleans, make sure .NET 7 is installed. Then, you can run the project with the following command:

```
dotnet run --urls "http://*:8080" --project ../Silo
```

Further instructions about Orleans deployment can be found in the following link: [MarketplaceOnOrleans](https://github.com/diku-dk/MarketplaceOnOrleans).

To setup Statefun, we must install Java 8 using the following command:

```
sudo apt update && \
  sudo apt install openjdk-8-jdk
```

Besides, it is necessary to install Docker and Docker compose to build the container used to run Statefun.

To install Docker, follow the instructions in the following link: [Install Docker Engine on Ubuntu](https://docs.docker.com/engine/install/ubuntu/). Make sure Docker compose is installed as part of a Docker installation.

Then, in the Statefun project's root folder, run the following commands in sequence:

```
docker-compose build
```

```
docker-compose up
```

Further instructions about Statefun deployment can be found in the following link: [MarketplaceOnStatefun](https://github.com/diku-dk/MarketplaceOnStatefun).

Finally, the following commands can be used to initialize the benchmark driver.
The driver interacts with the target platforms (i.e., submit workload), therefore now we can setup an experiment to benchmark one of the target platforms (Orleans or Statefun).


For Orleans:

```
dotnet run --project Orleans Configuration/orleans_local.json
```

For Statefun:

```
dotnet run --project Statefun Configuration/statefun_local.json
```

When the menu shows up, follow the order in the menu to 1 - Generate Data, 2 - Ingest Data, and 3 - Run Experiment.
Option 1 will generate synthetic data, option 2 will preload some of the application components with the generated data, and option 3 will start the experiment procedure.

Every experiment generates a result file in the driver's root folder with ".txt" extension, following the format below:

```
Run from 10/04/2023 13:59:18 to 10/04/2023 14:00:18
===========================================
Transaction: CUSTOMER_SESSION - #130077 - Average end-to-end latency: 7.36635946939107
Transaction: QUERY_DASHBOARD - #21028 - Average end-to-end latency: 4.402026616891806
Transaction: PRICE_UPDATE - #12715 - Average end-to-end latency: 1.1246382068423126
Transaction: UPDATE_PRODUCT - #8259 - Average end-to-end latency: 1.4979217096500828
Transaction: UPDATE_DELIVERY - #252373 - Average end-to-end latency: 4.102420991152072
Number of seconds: 60.0002047
Number of completed transactions: 424452
Transactions per second: 7074.175865270006
```

### Experimental Setup (to ensure Results are Reproducible)

The cloud platform [UCloud](https://cloud.sdu.dk) must be used to reproduce the experiments found in the SIGMOD paper. According to [UCloud docs](https://docs.cloud.sdu.dk/help/faq.html), an individual not affiliated with a Danish institution must request access through a PI. In this case, the reviewer must send an e-mail to [Rodrigo laigner](https://rnlaigner.github.io/) requesting the access.

Once the access is granted, log in [UCloud](https://cloud.sdu.dk/app). Access your home folder through the [link](https://cloud.sdu.dk/app/drives). Click "Upload files" and upload the files found in [Reproducibility](Reproducibility).

#### Setting up Orleans in UCloud

To set up the Orleans platform, access the [link](https://cloud.sdu.dk/app/jobs/create?app=ubuntu-xfce&version=Jul2023).

In the upper right corner, select the 'Big Data Systems' project.

In 'Job name', type 'orleans'

In 'Machine type', select u1-standard-64.

Inside 'Optional Parameters', in the `Initialization` line, click the button 'Use'. Select the file 'init_dotnet.sh' uploaded earlier in your Home folder. 

Finally, click 'Submit' to start running the Ubuntu instance.


