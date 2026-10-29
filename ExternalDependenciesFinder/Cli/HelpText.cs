namespace ExternalDependenciesFinder.Cli;

public static class HelpText
{
    public const string Text = @"ExternalDependenciesFinder

Назначение:
  Ищет прямые и транзитивные NuGet-зависимости в .NET-решении, наборе *.csproj
  или в текстовом списке пакетов. Выбирает максимальную версию каждого внешнего
  пакета и записывает её в текстовый файл. При анализе папки может создать
  служебное решение для проверки restore/build.

Использование:
  ExternalDependenciesFinder [scanRoot] [options]
  ExternalDependenciesFinder --packages-file <packages.txt> [--framework <tfm>]

Аргументы:
  scanRoot                         Папка для анализа. Если не указана, используется текущая папка.
                                   Не используется вместе с --packages-file.

Параметры:
  --packages-file <path>           Текстовый список прямых пакетов (PackageId => Version).
                                   Утилита сама создаёт служебный проект, выполняет restore
                                   и записывает полный список, включая транзитивные зависимости.
                                   Служебное решение для передачи не создаётся.

  --output-dir <path>              Папка служебного проекта или решения.
                                   По умолчанию для папки: <AppContext.BaseDirectory>/NugetUsingSolution
                                   По умолчанию для --packages-file: <папка файла>/seed-restore

  --packages-output <path>         Путь к итоговому файлу со списком пакетов.
                                   По умолчанию: <scanRoot>/packages_max.txt

  --log-dir <path>                 Папка для логов.
                                   По умолчанию: <scanRoot>/ExternalDependеnciesFinder_Logs

  --exclude-prefix <prefix>        Дополнительный префикс пакетов для исключения.
                                   Можно указывать несколько раз или через запятую/точку с запятой.
                                   По умолчанию уже исключаются Rbp. и Psb.

  --clear-default-excludes         Очистить стандартные исключения Rbp. и Psb.

  --framework, -f <tfm>            Target framework для генерируемого проекта.
                                   По умолчанию: net8.0

  --solution-name <name>           Имя генерируемого служебного решения.
                                   По умолчанию: NugetUsingSolution

  --project-name <name>            Имя генерируемого служебного проекта.
                                   По умолчанию: NugetUsingProject

  --list-only                      Только сформировать packages_max.txt, без создания служебного решения.

  --no-build                       Создать служебное решение и выполнить restore, но пропустить build.

  --help, -h                       Показать справку.

Примеры:
  ExternalDependenciesFinder --packages-file ""C:\work\initial-packages.txt"" --framework net8.0
  ExternalDependenciesFinder ""C:\Projects\Repo""
  ExternalDependenciesFinder ""C:\Projects\Repo"" --list-only
  ExternalDependenciesFinder ""C:\Projects\Repo"" --framework net6.0 --no-build
  ExternalDependenciesFinder ""C:\Projects\Repo"" --exclude-prefix Abdt. --output-dir ""D:\Temp\ExternalDeps""

Формат текстового списка:
  PackageId => Version
  Пустые строки и строки, начинающиеся с #, пропускаются.
";
}
