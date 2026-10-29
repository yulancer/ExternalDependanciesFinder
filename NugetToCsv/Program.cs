using NugetToCsv.App;
using NugetToCsv.Cli;

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

return new ApplicationRunner(options).Run();
