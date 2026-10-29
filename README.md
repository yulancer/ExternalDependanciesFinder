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
| `TestProjectCreator` | Отдельная утилита: создаёт тестовое решение по списку пакетов. В процессе закрытого контура не используется |
| `FullListOfUnavailablePackages` | Старый проект-контейнер списка. В текущем процессе вместо него используется текстовый файл |
| `InitialProjectForUpdate` | Старый проект-контейнер списка. В текущем процессе вместо него используется текстовый файл |
| `CpmMigrator` | Переводит решение на Central Package Management (`Directory.Packages.props`) |
| `PropsNormalizer` | Нормализует и сортирует `Directory.Build.props` / `Directory.Packages.props` |
| `Net8Fixer` | Правит целевые платформы и типовые несовместимости при переходе на .NET 8 |
| `NugetToCsv` | Заполняет CSV-шаблон по текстовому списку недоступных пакетов. Это итог процесса закрытого контура |
| `GetSha1` | Считает SHA-1 файлов — для сверки артефактов между контурами |

## Быстрый старт

```bash
dotnet run --project ExternalDependenciesFinder -- "C:\Projects\MyRepository" --list-only
```

На выходе — `packages_max.txt` со строками вида `PackageId => Version` и папка логов
с полным отчётом.

Подробное описание параметров: [ExternalDependenciesFinder/README.md](ExternalDependenciesFinder/README.md).

## Полный процесс для закрытого контура

Между контурами передаётся текстовый список `PackageId => Version`. Итог — заполненный CSV. Проекты вручную не правятся.

```bash
dotnet run --project ExternalDependenciesFinder -- --packages-file initial-packages.txt --framework net8.0 --packages-output packages_max.txt
dotnet run --project NuGetAccessChecker -- --packages packages_max.txt --list unavailable-packages.txt --output unavailable-packages-report.txt
dotnet run --project NugetToCsv -- --packages unavailable-packages.txt --template form.csv --output unavailable-packages.csv
```

Пошаговый регламент: [docs/closed-loop-procedure.md](docs/closed-loop-procedure.md).

## Исключение внутренних пакетов

Внутренние пакеты организации из отчёта нужно убирать, иначе они попадут в список
внешних зависимостей. Список префиксов задаётся под конкретный контур:

```bash
dotnet run --project ExternalDependenciesFinder -- "C:\Projects\Repo" \
  --exclude-prefix "MyCompany.,Internal."
```

По умолчанию исключений нет.

## Требования

.NET SDK 8.0+, доступ к используемым NuGet-источникам, корректный `NuGet.config`
для приватных источников.

## Лицензия

MIT — см. [LICENSE](LICENSE).
