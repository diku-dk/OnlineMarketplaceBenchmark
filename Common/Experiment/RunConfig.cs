using Common.Distribution;

namespace Common.Experiment;

public sealed class RunConfig
{
    public int numProducts { get; set; }

    public DistributionType sellerDistribution { get; set; }

    public DistributionType keyDistribution { get; set; }

    public double sellerZipfian { get; set; } = 1;

    public double productZipfian { get; set; } = 1;

}


