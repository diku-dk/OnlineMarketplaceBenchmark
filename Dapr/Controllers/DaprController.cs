using System.Net;
using Common.Experiment;
using Common.Http;
using Common.Infra;
using Common.Ingestion;
using Daprr.Workload;
using DuckDB.NET.Data;
using Microsoft.AspNetCore.Mvc;

namespace Daprr.Controllers;

[ApiController]
public class DaprController : ControllerBase
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<DaprController> logger;

    // requests are served by different threads
    // to ensure visibility after updates, need interlocked
    private static ExperimentConfig CONFIG;
    private static DuckDBConnection CONNECTION;

    public DaprController(IHttpClientFactory httpClientFactory, ILogger<DaprController> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    [Route("/1")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    public ActionResult GenerateData()
    {
        if(CONFIG is null)
        {
            return BadRequest("Please register a configuration first.");
        }
        Interlocked.Exchange(ref CONNECTION, ConsoleUtility.GenerateData(CONFIG));
        return Ok("Data generated");
    }

    private static bool ConnectionIsSet()
    {
        if(CONNECTION is null){
            if(CONFIG.connectionString.SequenceEqual("DataSource=:memory:"))
            {
                return false;
            }
            else
            {
                Interlocked.Exchange(ref CONNECTION, new DuckDBConnection(CONFIG.connectionString));
                CONNECTION.Open();
                return true;
            }
        }
        return true;
    }

    [Route("/2")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    public async Task<ActionResult> IngestData()
    {
        if(CONFIG is null)
        {
            return BadRequest("Please register a configuration first.");
        }
        if (!ConnectionIsSet())
        {
            return BadRequest("Please generate some data first by selecting option 1.");
        }
        await IngestionOrchestratorV1.Run(CONNECTION, CONFIG.ingestionConfig);
        return Ok("Data ingested");
    }

    [Route("/3")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    public ActionResult RunExperiment()
    {
        if(CONFIG is null)
        {
            return BadRequest("Please register a configuration first.");
        }
        if (!ConnectionIsSet())
        {
            return BadRequest("Please generate some data first by selecting option 1.");
        }
        var experimentManager = DaprExperimentManager
                                    .BuildDaprExperimentManager(this.httpClientFactory, CONFIG, CONNECTION);
        experimentManager.RunSimpleExperiment();
        return Ok("Experiment finished");
    }

    [Route("/4")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    public async Task<ActionResult> IngestDataAndRunExperiment()
    {
        if(CONFIG is null)
        {
            return BadRequest("Please register a configuration first.");
        }
        if (!ConnectionIsSet())
        {
            return BadRequest("Please generate some data first by selecting option 1.");
        }
        await CustomIngestionOrchestrator.Run(CONNECTION, CONFIG.ingestionConfig);
        var expManager = DaprExperimentManager
                            .BuildDaprExperimentManager(CustomHttpClientFactory.GetInstance(), CONFIG, CONNECTION);
        expManager.RunSimpleExperiment();
        return Ok("Experiment finished");
    }

    [Route("/5")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    public ActionResult ParseNewConfiguration([FromBody] ExperimentConfig newConfig)
    {
        // Console.WriteLine("Parse new configuration from body requested. Is null? "+config is null);
        Interlocked.Exchange(ref CONFIG, newConfig);
        return Ok("New configuration parsed.");
    }

    [Route("/6/{path}")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    public ActionResult ParseNewConfigurationFromPath(string path)
    {
        // Console.WriteLine("Parse new configuration from path requested. Is null? "+config is null);
        path = path.Replace("%2F","/");
        ExperimentConfig newConfig = ConsoleUtility.BuildExperimentConfig_(path);
        Interlocked.Exchange(ref CONFIG, newConfig);
        return Ok("New configuration parsed.");
    }

    private static readonly string STREAMS_TRIMMED_OK = "New trimmed successfully.";

    [Route("/7")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    public ActionResult TrimStreams()
    {
        if(CONFIG is null)
        {
            return BadRequest("Please register a configuration first.");
        }
        var expManager = DaprExperimentManager
                            .BuildDaprExperimentManager(CustomHttpClientFactory.GetInstance(), CONFIG, CONNECTION);
        expManager.TrimStreams();
        Console.WriteLine(STREAMS_TRIMMED_OK);
        return Ok(STREAMS_TRIMMED_OK);
    }

}
