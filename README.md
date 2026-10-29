# ExternalDependenciesFinder

Набор консольных утилит для .NET, которые нужны при обновлении и модернизации
зависимостей больших решений — особенно там, где сборка идёт в закрытом контуре
с внутренними NuGet-источниками.

Задача, из которой всё выросло: перед тем как обновлять или переносить крупное
решение, нужно точно знать, какие внешние пакеты оно тянет (включая транзитивные),
какие из них недоступны внутри контура и какие из них вообще живые. Вручную это
не делается — `dotnet list package` даёт сырой текст по одному проекту.

## Утилиты

| Утилита | Что делает |
|---|---|
| `ExternalDependenciesFinder` | Собирает полный список внешних зависимостей, включая транзитивные, выбирает максимальную версию каждого пакета и записывает её в текстовый файл. Источник — папка с проектами или текстовый список |
| `NuGetAccessChecker` | Проверяет, какие пакеты из текстового списка или проекта доступны во внутренних NuGet-источниках, и записывает недоступные в текстовый файл |
| `NuGetMaxAccessibleVersion` | Находит максимальную версию пакета, доступную в заданных источниках |
| `CpmMigrator` | Переводит решение на Central Package Management (`Directory.Packages.props`) |
| `PropsNormalizer` | Нормализует и сортирует `Directory.Build.props` / `Directory.Packages.props` |
| `Net8Fixer` | Правит целевые платформы и типовые несовместимости при переходе на .NET 8 |
| `NugetToCsv` | Заполняет CSV-шаблон по текстовому списку недоступных пакетов. Это итог процесса закрытого контура |
| `GetSha1` | Считает SHA-1 файлов — для сверки артефактов между контурами |

`CpmMigrator`, `PropsNormalizer`, `Net8Fixer`, `GetSha1` и `NuGetMaxAccessibleVersion` лежат в решении [Utils/Utils.sln](Utils/Utils.sln). Тесты — в папке `_Tests`.

## Быстрый старт

```bash
dotnet run --project ExternalDependenciesFinder -- "C:\Projects\MyRepository" --list-only
```

На выходе — `packages_max.txt` со строками вида `PackageId => Version` и папка логов
с полным отчётом.

Подробное описание параметров: [ExternalDependenciesFinder/README.md](ExternalDependenciesFinder/README.md).

## Полный процесс для закрытого контура

Между контурами передаётся текстовый список `PackageId => Version`. Итог — заполненный CSV. Проекты вручную не правятся.

Во внешнем контуре из `initial-packages.txt` собирается полный список, включая транзитивные зависимости. Папка `seed-restore` служебная, её не передают.

```bash
dotnet run --project ExternalDependenciesFinder -- --packages-file initial-packages.txt --framework net8.0 --packages-output packages_max.txt
```

`packages_max.txt` передаётся внутрь. Там `NuGet.config` должен содержать только внутренние источники.

```bash
dotnet run --project NuGetAccessChecker -- --packages packages_max.txt --list unavailable-packages.txt --output unavailable-packages-report.txt
```

Если в `unavailable-packages.txt` нет пакетов, процесс закончен. Иначе на том же контуре список вписывается в CSV-шаблон `form.csv` (преамбула, заголовки, одна строка-образец):

```bash
dotnet run --project NugetToCsv -- --packages unavailable-packages.txt --template form.csv --output unavailable-packages.csv
```

`unavailable-packages.csv` передаётся наружу. Пошаговый регламент: [docs/closed-loop-procedure.md](docs/closed-loop-procedure.md).

## Исключение внутренних пакетов

По умолчанию уже исключаются пакеты с префиксами `Rbp.` и `Psb.` Дополнительные префиксы задаются так:

```bash
dotnet run --project ExternalDependenciesFinder -- "C:\Projects\Repo" \
  --exclude-prefix "MyCompany.,Internal."
```

## Требования

.NET SDK 8.0+, доступ к используемым NuGet-источникам, корректный `NuGet.config`
для приватных источников.

## Лицензия

MIT — см. [LICENSE](LICENSE).
