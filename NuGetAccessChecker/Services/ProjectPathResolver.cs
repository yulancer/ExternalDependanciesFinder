namespace NuGetAccessChecker.Services;

public static class ProjectPathResolver
{
    public static string ResolveProjectFile(string projectPathOrFolder)
    {
        if (Directory.Exists(projectPathOrFolder))
        {
            var csproj = Directory.GetFiles(projectPathOrFolder, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (csproj is null)
                throw new FileNotFoundException($"В папке '{projectPathOrFolder}' не найден .csproj");

            return Path.GetFullPath(csproj);
        }

        if (!File.Exists(projectPathOrFolder))
            throw new FileNotFoundException($"Файл проекта '{projectPathOrFolder}' не найден");

        return Path.GetFullPath(projectPathOrFolder);
    }
}
