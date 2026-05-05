# ExternalDependanciesFinder

`ExternalDependanciesFinder` — консольная утилита для поиска внешних NuGet-зависимостей в .NET-решении или наборе проектов. Утилита анализирует прямые и транзитивные зависимости, выбирает максимальную версию каждого пакета, сохраняет итоговый список и, при необходимости, создаёт отдельное служебное решение для проверки `restore` и `build`.

## Что делает программа

1. Принимает путь к папке для анализа.
2. Ищет `.sln` в указанной папке.
3. Если `.sln` найден, выполняет:

   ```bash
   dotnet list "<solution>.sln" package --include-transitive
   ```

4. Если `.sln` не найден, рекурсивно ищет все `*.csproj` и для каждого выполняет:

   ```bash
   dotnet list "<project>.csproj" package --include-transitive
   ```

5. Разбирает вывод команды `dotnet list package --include-transitive`.
6. Собирает словарь `PackageId -> максимальная версия`.
7. Исключает внутренние пакеты по префиксам.
8. Сохраняет результат в `packages_max.txt` или в файл, указанный через `--packages-output`.
9. Если не указан `--list-only`, создаёт служебное решение.
10. Добавляет в служебный проект все найденные внешние пакеты.
11. Выполняет `dotnet restore`.
12. Если не указан `--no-build`, выполняет `dotnet build -c Release`.

## Требования

На машине должны быть установлены:

- .NET SDK;
- SDK для target framework, который будет указан через `--framework`;
- доступ к NuGet-источникам, из которых восстанавливаются найденные пакеты;
- корректный `NuGet.config`, если используются приватные NuGet-источники;
- пакет `NuGet.Versioning` в проекте самой утилиты.

Проверить SDK можно командой:

```bash
dotnet --info
```

## Сборка

Из папки проекта утилиты:

```bash
dotnet restore
dotnet build -c Release
```

Если пакет `NuGet.Versioning` ещё не добавлен:

```bash
dotnet add package NuGet.Versioning
dotnet build -c Release
```

## Запуск

### Базовый запуск

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\MyRepository"
```

Или через `dotnet run`:

```bash
dotnet run --project ExternalDependanciesFinder -- "C:\Projects\MyRepository"
```

Если путь не передан, анализируется текущая рабочая директория:

```bash
dotnet ExternalDependanciesFinder.dll
```

## Аргументы командной строки

```text
ExternalDependanciesFinder [scanRoot] [options]
```

| Параметр | Описание | По умолчанию |
|---|---|---|
| `scanRoot` | Папка для анализа | Текущая директория |
| `--output-dir <path>` | Папка для служебного решения | `<AppContext.BaseDirectory>/NugetUsingSolution` |
| `--packages-output <path>` | Путь к файлу со списком пакетов | `<scanRoot>/packages_max.txt` |
| `--log-dir <path>` | Папка для логов | `<scanRoot>/ExternalDependanciesFinder_Logs` |
| `--exclude-prefix <prefix>` | Дополнительный исключаемый префикс пакета | Можно указывать многократно |
| `--clear-default-excludes` | Очистить стандартные исключения `Rbp.` и `Psb.` | Не применяется |
| `--framework <tfm>` или `-f <tfm>` | Target framework генерируемого проекта | `net8.0` |
| `--solution-name <name>` | Имя генерируемого решения | `NugetUsingSolution` |
| `--project-name <name>` | Имя генерируемого проекта | `NugetUsingProject` |
| `--list-only` | Только создать список пакетов, без служебного решения | `false` |
| `--no-build` | Выполнить `restore`, но пропустить `build` | `false` |
| `--help` или `-h` | Показать справку | — |

## Примеры запуска

### 1. Обычный анализ репозитория

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo"
```

Результат:

```text
C:\Projects\Repo\packages_max.txt
C:\Projects\Repo\ExternalDependanciesFinder_Logs\ExternalDependanciesFinder_Report.txt
C:\Projects\Repo\ExternalDependanciesFinder_Logs\ExternalDependanciesFinder_Errors.txt
C:\Projects\Repo\ExternalDependanciesFinder_Logs\dotnet-list-package.log
<AppContext.BaseDirectory>\NugetUsingSolution\NugetUsingSolution.sln
```

### 2. Только сформировать список пакетов

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --list-only
```

В этом режиме создаётся только файл со списком пакетов и логи. Служебное решение не создаётся.

### 3. Создать служебное решение в отдельной папке

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --output-dir "D:\Temp\ExternalDeps"
```

Внимание: папка `--output-dir` пересоздаётся. Если она уже существует, программа удалит её содержимое.

### 4. Использовать другой target framework

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --framework net6.0
```

Генерируемый проект будет создан командой:

```bash
dotnet new console -n NugetUsingProject --framework net6.0
```

### 5. Проверить только restore, без build

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --no-build
```

Программа создаст служебное решение, добавит пакеты и выполнит `dotnet restore`, но не будет запускать `dotnet build`.

### 6. Исключить дополнительные внутренние пакеты

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --exclude-prefix System.
```

По умолчанию уже исключаются:

```text
Rbp.
Psb.
```

Дополнительные префиксы можно передавать несколько раз:

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --exclude-prefix System. --exclude-prefix MyCompany.
```

Или одним параметром через запятую/точку с запятой:

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --exclude-prefix "System.,MyCompany.;Internal."
```

### 7. Отключить стандартные исключения

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --clear-default-excludes
```

В этом случае пакеты `Rbp.*` и `Psb.*` не будут исключаться автоматически.

Если нужно заменить стандартные исключения на свои:

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --clear-default-excludes --exclude-prefix System.
```

### 8. Задать свой файл результата

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --packages-output "D:\Reports\packages_max.txt"
```

## Логика поиска проектов

### Если найден `.sln`

Программа ищет `.sln` только в корневой папке `scanRoot`:

```csharp
Directory.EnumerateFiles(scanRoot, "*.sln", SearchOption.TopDirectoryOnly)
```

Если найдено несколько `.sln`, берётся первый по сортировке имени. Остальные записываются в отчёт как проигнорированные.

### Если `.sln` не найден

Программа рекурсивно ищет `*.csproj`:

```csharp
Directory.EnumerateFiles(scanRoot, "*.csproj", SearchOption.AllDirectories)
```

При этом из поиска исключаются проекты, находящиеся внутри `--output-dir`, чтобы повторный запуск не анализировал собственное служебное решение.

## Как разбирается вывод dotnet list package

Утилита читает строки вывода, начинающиеся с символа `>`.

Пример строки:

```text
> Newtonsoft.Json      13.0.3
```

Алгоритм:

1. первый токен после `>` считается идентификатором пакета;
2. последний токен, который успешно парсится через `NuGetVersion`, считается версией;
3. если пакет уже встречался, версии сравниваются через `NuGet.Versioning`;
4. в итоговый список попадает максимальная версия.

## Формат packages_max.txt

Файл сохраняется в UTF-8 без BOM.

Формат строки:

```text
PackageId => Version
```

Пример:

```text
Dapper => 2.1.35
FluentValidation => 11.9.2
Microsoft.Extensions.Logging.Abstractions => 8.0.2
Newtonsoft.Json => 13.0.3
Npgsql => 8.0.3
```

Пакеты сортируются по имени без учёта регистра.

## Генерируемое служебное решение

По умолчанию создаётся:

```text
<AppContext.BaseDirectory>/NugetUsingSolution/NugetUsingSolution.sln
<AppContext.BaseDirectory>/NugetUsingSolution/NugetUsingProject/NugetUsingProject.csproj
```

Имена можно изменить:

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --solution-name ExternalDepsCheck --project-name ExternalDepsCheckProject
```

Служебное решение используется только для проверки, что найденные внешние зависимости можно восстановить и собрать в отдельном минимальном проекте.

## Файлы логов

По умолчанию логи пишутся в:

```text
<scanRoot>/ExternalDependanciesFinder_Logs/
```

Создаются файлы:

| Файл | Назначение |
|---|---|
| `ExternalDependanciesFinder_Report.txt` | Общий отчёт: параметры запуска, команды, коды возврата, этапы работы |
| `ExternalDependanciesFinder_Errors.txt` | Ошибки `dotnet list`, `dotnet add package`, `restore`, `build` и ошибки парсинга |
| `dotnet-list-package.log` | Полный stdout/stderr команды `dotnet list package --include-transitive` по каждой цели |

Папку логов можно изменить:

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\Repo" --log-dir "D:\Reports\ExternalDepsLogs"
```

## Структура обновлённого кода

| Блок | Назначение |
|---|---|
| `Options` | Разбор аргументов командной строки и хранение настроек запуска |
| `CollectPackages` | Выбор режима анализа: `.sln` или рекурсивный поиск `*.csproj` |
| `ProcessOneTarget` | Запуск `dotnet list package --include-transitive` и разбор вывода |
| `NuGetVersion` | Парсинг и сравнение NuGet-версий |
| `WritePackagesFile` | Запись итогового файла `packages_max.txt` |
| `GenerateValidationSolution` | Создание служебного решения, добавление пакетов, `restore`, опциональный `build` |
| `ProcessRunner` | Безопасный запуск внешних процессов через `ProcessStartInfo.ArgumentList` |
| `ReportWriter` | Запись отчётов, ошибок и полного вывода `dotnet list package` |

## Ограничения

1. Утилита по-прежнему зависит от формата вывода `dotnet list package --include-transitive`.
2. Если `dotnet list package` не сможет восстановить проект или прочитать зависимости, ошибка будет записана в лог, а программа продолжит обработку остальных целей.
3. Если `dotnet add package` не сможет добавить отдельный пакет, ошибка будет записана в лог, но обработка остальных пакетов продолжится.
4. Команды `restore` и `build` считаются критичными: если они завершаются с ошибкой, программа завершится с кодом `1`.
5. `--output-dir` пересоздаётся полностью, поэтому не указывайте папку с важными файлами.

## Быстрая инструкция для разработчика

1. Добавить зависимость в проект утилиты:

   ```bash
   dotnet add package NuGet.Versioning
   ```

2. Собрать утилиту:

   ```bash
   dotnet build -c Release
   ```

3. Запустить анализ:

   ```bash
   dotnet ExternalDependanciesFinder.dll "C:\Projects\MyRepository"
   ```

4. Проверить результат:

   ```text
   C:\Projects\MyRepository\packages_max.txt
   ```

5. При ошибках посмотреть логи:

   ```text
   C:\Projects\MyRepository\ExternalDependanciesFinder_Logs\ExternalDependanciesFinder_Report.txt
   C:\Projects\MyRepository\ExternalDependanciesFinder_Logs\ExternalDependanciesFinder_Errors.txt
   C:\Projects\MyRepository\ExternalDependanciesFinder_Logs\dotnet-list-package.log
   ```

## Рекомендуемый запуск для быстрого отчёта

Если нужно только получить список внешних зависимостей без проверки служебного решения:

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\MyRepository" --list-only
```

## Рекомендуемый запуск для проверки restore без полной сборки

```bash
dotnet ExternalDependanciesFinder.dll "C:\Projects\MyRepository" --no-build --output-dir "D:\Temp\ExternalDepsCheck"
```
