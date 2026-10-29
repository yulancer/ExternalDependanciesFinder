using System.Text;

namespace NugetToCsv.Services;

public static class Utf8EncodingDetector
{
    public static Encoding Detect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length >= 3)
        {
            var bom = new byte[3];
            var read = stream.Read(bom, 0, 3);
            if (read == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
