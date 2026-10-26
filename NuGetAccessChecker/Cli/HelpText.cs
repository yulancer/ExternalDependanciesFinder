namespace NuGetAccessChecker.Cli;

public static class HelpText
{
    public const string Text = @"NuGetAccessChecker

Назначение:
  Проверяет доступность NuGet-пакетов из .csproj во включённых источниках.
  Пакет считается доступным, если во внутреннем фиде есть скачиваемая версия
  той же major-линии, не ниже требуемой (включая более новые патчи/минорные).

Использование:
  NuGetAccessChecker --project <path-to-csproj-or-folder> [--output <output-file>]

Параметры:
  --project <path>   Путь к .csproj или папке с .csproj
  --output <path>    Путь к отчёту (по умолчанию: <projectDir>/unavailable-packages.txt)
  --help, -h         Показать справку
";
}
