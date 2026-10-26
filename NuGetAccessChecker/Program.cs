using NuGetAccessChecker.App;
using NuGetAccessChecker.Cli;
using NuGetAccessChecker.Infrastructure;

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.WriteLine(HelpText.Text);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(HelpText.Text);
    return 0;
}

var logger = new ConsoleNuGetLogger();
var feedClient = new NuGetPackageFeedClient(logger);
var sourceProvider = new NuGetPackageSourceProvider();
var app = new ApplicationRunner(options, sourceProvider, feedClient);
return await app.RunAsync();
