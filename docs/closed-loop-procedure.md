# Поиск недоступных NuGet-пакетов внутри закрытого контура

Пакеты передаются текстовыми файлами формата `PackageId => Version`.
Пустые строки и строки, начинающиеся с `#`, пропускаются.
Целевая платформа задаётся параметром `--framework` у `ExternalDependenciesFinder`.

Править файлы проектов не нужно. Итог процесса — заполненный CSV.

`NuGetAccessChecker` считает пакет доступным, если во внутреннем фиде есть скачиваемая версия той же major, не ниже требуемой.

```text
Внешний контур
    |
    | initial-packages.txt
    | ExternalDependenciesFinder
    v
packages_max.txt
    |
    | передать внутрь закрытого контура
    v
Закрытый контур
    |
    | NuGetAccessChecker
    v
unavailable-packages.txt
    |
    | NugetToCsv
    v
unavailable-packages.csv
```

## Внешний контур

Исходный список, например `initial-packages.txt`:

```text
Rebus.RabbitMq => 10.1.2
```

```bash
dotnet run --project ExternalDependenciesFinder -- --packages-file "path/to/initial-packages.txt" --framework net8.0 --packages-output "path/to/packages_max.txt"
```

Утилита сама создаёт служебный проект, выполняет `restore` и записывает прямые и транзитивные зависимости. Для каждого `PackageId` берётся максимальная версия. По умолчанию исключаются пакеты с префиксами `Rbp.` и `Psb.` Дополнительно: `--exclude-prefix <prefix>`.

Передать внутрь закрытого контура файл `packages_max.txt`. Папка `seed-restore` — служебная, её передавать не нужно.

## Закрытый контур

В окружении должен быть `NuGet.config` только с внутренними источниками: рядом с файлом списка, выше по дереву каталогов или в пользовательских настройках NuGet.

```bash
dotnet run --project NuGetAccessChecker -- --packages "path/to/packages_max.txt" --list "path/to/unavailable-packages.txt" --output "path/to/unavailable-packages-report.txt"
```

`unavailable-packages.txt` — список недоступных пакетов. Если пакетов нет, процесс закончен.

`unavailable-packages-report.txt` — подробный отчёт. Для совместимых замен указано `required -> found`.

Дальше на том же контуре запускается `NugetToCsv`. Рядом нужен CSV-шаблон из трёх строк: преамбула, заголовки и одна строка-шаблон. Заполняются столбцы «Наименование ПО», «Версия ПО», «Прямая ссылка на скачивание», «Список IP (где будет установлено ПО)» и «Российское ПО». Остальные ячейки копируются из строки-шаблона.

```bash
dotnet run --project NugetToCsv -- --packages "path/to/unavailable-packages.txt" --template "path/to/form.csv" --output "path/to/unavailable-packages.csv"
```

`unavailable-packages.csv` — итог процесса. Его передают наружу закрытого контура.
