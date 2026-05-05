# Поиск недоступных NuGet-пакетов внутри закрытого контура

## Назначение

Этот документ описывает полный алгоритм подготовки списка NuGet-пакетов, проверки их доступности внутри закрытого контура и создания тестового решения для недоступных пакетов.

Инструкция предназначена для размещения в корне решения и используется как пошаговый регламент для разработчика, который выполняет обновление или проверку NuGet-зависимостей.

Основная цель процесса:

1. Получить полный список NuGet-пакетов, включая транзитивные зависимости.
2. Проверить, какие из этих пакетов недоступны внутри закрытого контура.
3. Создать тестовое решение для недоступных пакетов.
4. Запустить тесты и измерение покрытия.
5. Передать готовое тестовое решение для дальнейшей обработки.

## Используемые утилиты

В процессе используются три утилиты:

| Утилита | Назначение |
|---|---|
| `ExternalDependanciesFinder` | Формирует проект со всеми пакетами, включая транзитивные зависимости исходного списка. |
| `ExternalDependanciesFinder.NugetAccessChecker` | Проверяет доступность пакетов внутри закрытого контура и формирует список недоступных пакетов. |
| `TestProjectCreator` | Создаёт тестовое решение для указанного списка пакетов и запускает тесты с измерением покрытия. |

## Общая схема процесса

Процесс выполняется в двух средах:

| Среда | Что выполняется |
|---|---|
| Внешний контур | Подготовка исходного списка пакетов, получение полного списка зависимостей, создание тестового решения. |
| Закрытый контур | Проверка доступности полного списка пакетов во внутренних NuGet-источниках. |

Файлы передаются между контурами вручную.

```text
Внешний контур
    |
    | 1. InitialProjectForUpdate.csproj
    | 2. ExternalDependanciesFinder
    v
NugetUsungProject.csproj
    |
    | передать внутрь закрытого контура
    v
Закрытый контур
    |
    | 3. ExternalDependanciesFinder.NugetAccessChecker
    v
unavailable-packages.txt
    |
    | передать наружу закрытого контура
    v
Внешний контур
    |
    | 4. FullListOfUnavailablePackages.csproj
    | 5. TestProjectCreator
    v
TestSolution.zip
```

## Шаг 1. Подготовить исходный проект со списком пакетов

Создайте или обновите файл:

```text
InitialProjectForUpdate.csproj
```

В этот файл нужно добавить первоначальный список NuGet-пакетов, которые требуется проверить.

Пример структуры:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MyCompany.SomePackage" Version="1.2.3" />
    <PackageReference Include="MyCompany.AnotherPackage" Version="4.5.6" />
  </ItemGroup>
</Project>
```

Важно: целевая платформа в `TargetFramework` должна соответствовать той платформе, для которой проверяется совместимость пакетов.

## Шаг 2. Выполнить restore исходного проекта

Перед дальнейшей обработкой нужно убедиться, что указанные пакеты совместимы с целевой платформой.

Выполните команду:

```bash
dotnet restore InitialProjectForUpdate.csproj
```

Если `restore` завершился с ошибкой, сначала исправьте список пакетов или целевую платформу.

Дальнейшие шаги имеют смысл только после успешного восстановления исходного проекта.

## Шаг 3. Получить полный список пакетов с транзитивными зависимостями

Запустите `ExternalDependanciesFinder`, передав путь к исходному проекту:

```bash
dotnet run --project path/to/ExternalDependanciesFinder.csproj -- "path/to/InitialProjectForUpdate.csproj"
```

Или, если утилита уже собрана:

```bash
ExternalDependanciesFinder.exe "path/to/InitialProjectForUpdate.csproj"
```

Утилита создаёт отдельное решение с проектом, в котором перечислен полный список пакетов, включая транзитивные зависимости.

Ожидаемый результат находится по пути вида:

```text
ExternalDependanciesFinder/ExternalDependanciesFinder/bin/Debug/net8.0/NugetUsungSolution/NugetUsungSolution.sln
```

Внутри этого решения находится проект:

```text
NugetUsungProject.csproj
```

Именно этот проект содержит полный список пакетов, которые нужно проверить внутри закрытого контура.

> Примечание: в старой версии утилиты в названии используется `NugetUsungSolution` / `NugetUsungProject`. Если в новой версии имя исправлено на `NugetUsingSolution` / `NugetUsingProject`, используйте актуальное имя файла, созданного утилитой.

## Шаг 4. Передать полный список пакетов внутрь закрытого контура

Передайте внутрь закрытого контура файл:

```text
NugetUsungProject.csproj
```

Или его актуальный аналог, если имя было исправлено в новой версии утилиты.

Передавать всё решение обычно не требуется. Для проверки доступности достаточно проекта, содержащего полный список `PackageReference`.

## Шаг 5. Проверить доступность пакетов внутри закрытого контура

В закрытом контуре запустите утилиту:

```bash
dotnet run --project path/to/ExternalDependanciesFinder.NugetAccessChecker.csproj -- "path/to/NugetUsungProject.csproj"
```

Или, если утилита уже собрана:

```bash
ExternalDependanciesFinder.NugetAccessChecker.exe "path/to/NugetUsungProject.csproj"
```

Утилита проверяет, какие пакеты из переданного проекта доступны во внутренних NuGet-источниках закрытого контура.

Для корректной проверки внутри контура должен быть настроен `NuGet.config` с внутренними источниками пакетов.

Пример:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="MyCompanyInternalNuGet" value="https://nuget.mycompany.local/v3/index.json" />
  </packageSources>
</configuration>
```

## Шаг 6. Получить отчёт о недоступных пакетах

Результатом работы `ExternalDependanciesFinder.NugetAccessChecker` является файл:

```text
unavailable-packages.txt
```

Этот файл содержит список пакетов, которые не удалось найти или восстановить из NuGet-источников закрытого контура.

Файл нужно передать наружу закрытого контура.

## Шаг 7. Подготовить проект со списком недоступных пакетов

Во внешнем контуре создайте файл:

```text
FullListOfUnavailablePackages.csproj
```

В него нужно записать список пакетов из `unavailable-packages.txt` в виде `PackageReference`.

Пример:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MyCompany.MissingPackage" Version="1.0.0" />
    <PackageReference Include="MyCompany.AnotherMissingPackage" Version="2.0.0" />
  </ItemGroup>
</Project>
```

Важно сохранить версии пакетов, указанные в отчёте.

## Шаг 8. Создать тестовое решение для недоступных пакетов

Запустите `TestProjectCreator`, передав путь к проекту со списком недоступных пакетов:

```bash
dotnet run --project path/to/TestProjectCreator.csproj -- --ProjectPath="path/to/FullListOfUnavailablePackages.csproj"
```

Или, если утилита уже собрана:

```bash
TestProjectCreator.exe --ProjectPath="path/to/FullListOfUnavailablePackages.csproj"
```

В процессе работы утилита создаёт тестовое решение и запускает его с измерением тестового покрытия.

Ожидаемый результат:

```text
TestSolution
```

## Шаг 9. Проверить результат запуска тестов

После запуска `TestProjectCreator` возможны два варианта.

### Вариант 1. Тесты успешно прошли

Если созданное решение успешно запустилось и измерение покрытия выполнилось без ошибок, дополнительных действий не требуется.

Можно переходить к упаковке результата.

### Вариант 2. Тесты упали

Если часть тестов упала, нужно открыть созданное тестовое решение:

```text
TestSolution
```

Найдите упавшие тесты и раскомментируйте `return` в строке 273 теста.

После этого повторно запустите тесты.

Пример команды:

```bash
dotnet test TestSolution.sln
```

Если тесты после правки проходят, можно переходить к упаковке результата.

## Шаг 10. Передать результат

Готовое решение:

```text
TestSolution
```

нужно упаковать в архив и выложить на файлообменник.

Пример упаковки в PowerShell:

```powershell
Compress-Archive -Path .\TestSolution -DestinationPath .\TestSolution.zip -Force
```

Результат:

```text
TestSolution.zip
```

## Итоговые входные и выходные файлы

| Файл | Где создаётся | Назначение |
|---|---|---|
| `InitialProjectForUpdate.csproj` | Внешний контур | Исходный список пакетов для проверки. |
| `NugetUsungProject.csproj` | Внешний контур | Полный список пакетов, включая транзитивные зависимости. |
| `unavailable-packages.txt` | Закрытый контур | Список пакетов, недоступных внутри закрытого контура. |
| `FullListOfUnavailablePackages.csproj` | Внешний контур | Проект со списком недоступных пакетов для генерации тестов. |
| `TestSolution` | Внешний контур | Сгенерированное тестовое решение. |
| `TestSolution.zip` | Внешний контур | Архив для передачи результата. |

## Рекомендуемая структура файлов

Для удобства можно использовать такую структуру:

```text
repo-root/
  InitialProjectForUpdate.csproj
  FullListOfUnavailablePackages.csproj
  unavailable-packages.txt
  tools/
    ExternalDependanciesFinder/
    ExternalDependanciesFinder.NugetAccessChecker/
    TestProjectCreator/
  output/
    NugetUsungSolution/
    TestSolution/
    TestSolution.zip
```

## Краткая последовательность команд

Ниже приведён укрупнённый пример запуска.

### Во внешнем контуре

```bash
dotnet restore InitialProjectForUpdate.csproj

dotnet run --project tools/ExternalDependanciesFinder/ExternalDependanciesFinder.csproj -- "InitialProjectForUpdate.csproj"
```

После этого передать внутрь закрытого контура:

```text
NugetUsungProject.csproj
```

### В закрытом контуре

```bash
dotnet run --project tools/ExternalDependanciesFinder.NugetAccessChecker/ExternalDependanciesFinder.NugetAccessChecker.csproj -- "NugetUsungProject.csproj"
```

После этого передать наружу закрытого контура:

```text
unavailable-packages.txt
```

### Во внешнем контуре

Создать `FullListOfUnavailablePackages.csproj`, затем выполнить:

```bash
dotnet run --project tools/TestProjectCreator/TestProjectCreator.csproj -- --ProjectPath="FullListOfUnavailablePackages.csproj"
```

После успешного запуска упаковать результат:

```powershell
Compress-Archive -Path .\TestSolution -DestinationPath .\TestSolution.zip -Force
```

## Контрольный чек-лист

Перед передачей результата убедитесь, что:

- [ ] `InitialProjectForUpdate.csproj` содержит исходный список пакетов.
- [ ] `dotnet restore InitialProjectForUpdate.csproj` успешно выполнен.
- [ ] `ExternalDependanciesFinder` сформировал проект с полным списком зависимостей.
- [ ] `NugetUsungProject.csproj` передан внутрь закрытого контура.
- [ ] В закрытом контуре запущен `ExternalDependanciesFinder.NugetAccessChecker`.
- [ ] Получен файл `unavailable-packages.txt`.
- [ ] На основе `unavailable-packages.txt` создан `FullListOfUnavailablePackages.csproj`.
- [ ] `TestProjectCreator` создал `TestSolution`.
- [ ] Тесты либо успешно прошли, либо проблемные тесты отключены согласно инструкции.
- [ ] `TestSolution` упакован в архив.
- [ ] Архив `TestSolution.zip` выложен на файлообменник.
