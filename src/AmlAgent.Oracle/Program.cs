namespace AmlAgent.Oracle;

public static class Program
{
    public static int Main(string[] args)
    {
        string? input = null, output = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input"  when i + 1 < args.Length: input = args[++i]; break;
                case "--output" when i + 1 < args.Length: output = args[++i]; break;
                case "-h" or "--help":
                    Console.WriteLine("aml-oracle --input <transfers.csv> --output <aml_clusters.csv>");
                    return 0;
            }
        }
        if (input is null || output is null)
        {
            Console.Error.WriteLine("Usage: aml-oracle --input <transfers.csv> --output <aml_clusters.csv>");
            return 64;
        }

        var result = OracleRunner.Run(input, output);
        Console.WriteLine($"Wrote {result.ClustersWritten} AML clusters to {result.OutputPath}");
        return 0;
    }
}
