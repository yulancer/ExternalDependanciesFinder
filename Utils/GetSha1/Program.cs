using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        string[] packages =
        {
            "HarfBuzzSharp/7.3.0.3",
            "HarfBuzzSharp.NativeAssets.macOS/7.3.0.3",
            "HarfBuzzSharp.NativeAssets.Win32/7.3.0.3",
            "SkiaSharp/2.88.9",
            "SkiaSharp.HarfBuzz/2.88.9",
            "SkiaSharp.NativeAssets.macOS/2.88.9",
            "SkiaSharp.NativeAssets.Win32/2.88.9"
        };

        string baseUrl = "https://www.nuget.org/api/v2/package/";
        string downloadDir = Path.Combine(Path.GetTempPath(), "NugetSha1");
        Directory.CreateDirectory(downloadDir);

        string outputFile = Path.Combine(AppContext.BaseDirectory, "sha1.txt");
        using var writer = new StreamWriter(outputFile, false);

        using var http = new HttpClient();

        foreach (var pkg in packages)
        {
            var url = baseUrl + pkg;
            var fileName = pkg.Replace('/', '.') + ".nupkg";
            var filePath = Path.Combine(downloadDir, fileName);

            Console.WriteLine($"Скачиваю {url} ...");
            await writer.WriteLineAsync($"Скачиваю {url} ...");

            try
            {
                var bytes = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);

                string sha1 = GetSha1(filePath);
                string line = $"{pkg} => {sha1}";

                Console.WriteLine(line);
                await writer.WriteLineAsync(line);
            }
            catch (Exception ex)
            {
                string err = $"{pkg} => Ошибка: {ex.Message}";
                Console.WriteLine(err);
                await writer.WriteLineAsync(err);
            }
        }

        Console.WriteLine($"\nГотово! Результаты сохранены в {outputFile}");
    }

    static string GetSha1(string filePath)
    {
        using var sha1 = SHA1.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha1.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
