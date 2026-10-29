using System.Text;
using NugetToCsv.App;
using NugetToCsv.Cli;

namespace NugetToCsv.Tests.App;

public sealed class ApplicationRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nuget-csv-app-" + Guid.NewGuid().ToString("N"));

    public ApplicationRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_WritesFilledCsvAndKeepsBom()
    {
        var packages = Path.Combine(_root, "unavailable-packages.txt");
        var template = Path.Combine(_root, "form.csv");
        var output = Path.Combine(_root, "out", "filled.csv");
        File.WriteAllText(packages, "# PackageId => Version\nDapper => 2.1.35\n");
        File.WriteAllText(
            template,
            "Отчёт\nНаименование ПО,Версия ПО,Прямая ссылка на скачивание,Список IP (где будет установлено ПО),Российское ПО\n,,,,\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var options = Options.Parse(new[] { "--packages", packages, "--template", template, "--output", output });
        var exitCode = new ApplicationRunner(options).Run();

        Assert.Equal(0, exitCode);
        var bytes = File.ReadAllBytes(output);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

        var lines = File.ReadAllLines(output);
        Assert.Equal("Отчёт", lines[0]);
        Assert.Contains("Dapper,2.1.35,https://www.nuget.org/api/v2/package/Dapper/2.1.35,*,Нет", lines[2]);
    }

    [Fact]
    public void Run_ReturnsTwo_WhenPackagesFileIsMissing()
    {
        var template = Path.Combine(_root, "form.csv");
        File.WriteAllText(template, "a\nb\nc\n");
        var options = Options.Parse(new[]
        {
            "--packages", Path.Combine(_root, "missing.txt"),
            "--template", template
        });

        Assert.Equal(2, new ApplicationRunner(options).Run());
    }

    [Fact]
    public void Run_ReturnsOne_WhenTemplateIsTooShort()
    {
        var packages = Path.Combine(_root, "packages.txt");
        var template = Path.Combine(_root, "short.csv");
        File.WriteAllText(packages, "Dapper => 2.1.35\n");
        File.WriteAllText(template, "only-one-line\n");

        var options = Options.Parse(new[] { "--packages", packages, "--template", template });

        Assert.Equal(1, new ApplicationRunner(options).Run());
    }
}
