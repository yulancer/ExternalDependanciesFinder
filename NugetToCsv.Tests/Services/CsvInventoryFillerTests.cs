using NugetToCsv.Models;
using NugetToCsv.Services;

namespace NugetToCsv.Tests.Services;

public sealed class CsvInventoryFillerTests
{
    private static readonly string[] Template =
    {
        "Преамбула,не трогать",
        "Наименование ПО,Версия ПО,Прямая ссылка на скачивание,Список IP (где будет установлено ПО),Российское ПО,Примечание",
        ",,,,,\"как в шаблоне, без изменений\""
    };

    [Fact]
    public void Fill_CopiesTemplateAndWritesPackageColumns()
    {
        var lines = CsvInventoryFiller.Fill(Template, new[]
        {
            new PackageIdVersion("Newtonsoft.Json", "13.0.3"),
            new PackageIdVersion("Dapper", "2.1.35")
        });

        Assert.Equal("Преамбула,не трогать", lines[0]);
        Assert.Equal(4, lines.Count);

        var dapper = CsvLine.Parse(lines[2]);
        Assert.Equal("Dapper", dapper[0]);
        Assert.Equal("2.1.35", dapper[1]);
        Assert.Equal("https://www.nuget.org/api/v2/package/Dapper/2.1.35", dapper[2]);
        Assert.Equal("*", dapper[3]);
        Assert.Equal("Нет", dapper[4]);
        Assert.Equal("как в шаблоне, без изменений", dapper[5]);

        var json = CsvLine.Parse(lines[3]);
        Assert.Equal("Newtonsoft.Json", json[0]);
        Assert.Equal("13.0.3", json[1]);
    }

    [Fact]
    public void Fill_SortsByPackageId()
    {
        var lines = CsvInventoryFiller.Fill(Template, new[]
        {
            new PackageIdVersion("Zed", "1.0.0"),
            new PackageIdVersion("alpha", "2.0.0")
        });

        Assert.Equal("alpha", CsvLine.Parse(lines[2])[0]);
        Assert.Equal("Zed", CsvLine.Parse(lines[3])[0]);
    }

    [Fact]
    public void Fill_KeepsOnlyPrefaceAndHeaders_WhenPackageListIsEmpty()
    {
        var lines = CsvInventoryFiller.Fill(Template, Array.Empty<PackageIdVersion>());

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("Наименование ПО,", lines[1]);
    }

    [Fact]
    public void Fill_SkipsMissingColumns()
    {
        var template = new[]
        {
            "preface",
            "Наименование ПО,Примечание",
            ","
        };

        var lines = CsvInventoryFiller.Fill(template, new[] { new PackageIdVersion("Dapper", "2.1.35") });
        var row = CsvLine.Parse(lines[2]);

        Assert.Equal(new[] { "Dapper", "" }, row);
    }

    [Fact]
    public void Fill_PadsShortTemplateRowAndDropsExtraCells()
    {
        var template = new[]
        {
            "preface",
            "Наименование ПО,Версия ПО,Лишнее",
            "only-name,extra-a,extra-b,extra-c"
        };

        var row = CsvLine.Parse(CsvInventoryFiller.Fill(
            template,
            new[] { new PackageIdVersion("Dapper", "2.0.0") })[2]);

        Assert.Equal(new[] { "Dapper", "2.0.0", "extra-b" }, row);
    }

    [Fact]
    public void Fill_Throws_WhenTemplateHasFewerThanThreeLines()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvInventoryFiller.Fill(new[] { "a", "b" }, Array.Empty<PackageIdVersion>()));

        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void DefaultOutputPath_AppendsFilledBeforeExtension()
    {
        var path = CsvInventoryFiller.DefaultOutputPath(@"C:\forms\inventory.csv");

        Assert.Equal(@"C:\forms\inventory_filled.csv", path);
    }
}
