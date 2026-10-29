namespace NuGetAccessChecker.Cli;

public static class HelpText
{
    public const string Text = @"NuGetAccessChecker

Назначение:
  Проверяет доступность NuGet-пакетов во включённых источниках.
  Пакет считается доступным, если во внутреннем фиде есть скачиваемая версия
  той же major-линии, не ниже требуемой (включая более новые патчи/минорные).

Использование:
  NuGetAccessChecker --packages <packages.txt> [--list <unavailable.txt>] [--output <report.txt>]
  NuGetAccessChecker --project <path-to-csproj-or-folder> [--output <output-file>] [--list <unavailable.txt>]

Параметры:
  --packages <path>  Текстовый список пакетов (PackageId => Version). Источники NuGet
                     берутся из каталога этого файла и родительских NuGet.config.
  --project <path>   Путь к .csproj или папке с .csproj
  --list <path>      Текстовый список недоступных пакетов для передачи наружу.
                     По умолчанию для --packages: <папка списка>/unavailable-packages.txt
  --output <path>    Подробный отчёт.
                     По умолчанию для --packages: <папка списка>/unavailable-packages-report.txt
                     По умолчанию для --project: <projectDir>/unavailable-packages.txt
  --help, -h         Показать справку

Формат текстового списка:
  PackageId => Version
  Пустые строки и строки, начинающиеся с #, пропускаются.
";
}
