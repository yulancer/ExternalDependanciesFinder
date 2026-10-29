namespace NugetToCsv.Cli;

public static class HelpText
{
    public const string Text = @"NugetToCsv

Назначение:
  Заполняет CSV-шаблон строками по списку NuGet-пакетов.
  Для каждого пакета копируется строка-шаблон, затем заполняются столбцы
  «Наименование ПО», «Версия ПО», «Прямая ссылка на скачивание»,
  «Список IP (где будет установлено ПО)» и «Российское ПО».

Использование:
  NugetToCsv --packages <unavailable-packages.txt> --template <form.csv> [--output <filled.csv>]
  NugetToCsv --project <path-to.csproj> --template <form.csv> [--output <filled.csv>]
  NugetToCsv <path-to.csproj> <form.csv>

Параметры:
  --packages <path>   Текстовый список (PackageId => Version).
                      Пустые строки и строки, начинающиеся с #, пропускаются.
  --project <path>    .csproj (PackageReference) и packages.config рядом с ним.
  --template <path>   CSV из трёх строк: преамбула, заголовки, строка-шаблон.
  --output <path>     Заполненный CSV.
                      По умолчанию: <имя шаблона>_filled<расширение> рядом с шаблоном.
  --help, -h          Показать справку.

Столбцы, которых нет в заголовке, не создаются. Остальные ячейки берутся из строки-шаблона.
";
}
