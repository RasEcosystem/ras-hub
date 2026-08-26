[English](README.md) | [Русский](README.ru.md)

# RasHub

RasHub — центральный сервис управления для
[RasStudio Mono](https://github.com/RasEcosystem/ras-studio-mono). Он
предоставляет версионированный API управления, хранит теневое состояние
инфраструктуры и координирует один или несколько экземпляров
[RasGate](https://github.com/RasEcosystem/ras-gate), выполняющих операции RAC
через RAS. Общие модели API находятся в сабмодуле
[`RasHub.Contracts`](src/RasHub.Contracts).

## Требования

- .NET SDK 10;
- Git и Make;
- Docker с Compose для локальных сервисов и развёртывания.

## Сборка и тесты

```bash
git submodule update --init --recursive
make build
make test
```

Полезные команды:

```bash
make release            # проверить релиз и собрать deployment bundle
make submodules-update  # обновить ревизии сабмодулей
make help               # показать все корневые команды
```

## Разработка

Запустите PostgreSQL и Seq, затем стартуйте `RasHub.Web` с Development-профилем:

```bash
make dev-up
```

Команда `make dev-stack-up` запускает весь стек в контейнерах, а
`make dev-down` останавливает его. Production-настройки и работа с миграциями
описаны в [deploy/README.md](deploy/README.md).

В окружении `Development` документация API доступна авторизованному пользователю
по адресу `/swagger`.

## Релизы

RasHub выпускается как Linux AMD64 контейнер. Релизный тег, совпадающий с
версией в `version.json`, запускает проверку форматирования, Release-сборку без
предупреждений, все тесты, упаковку deployment bundle и сборку Docker-образа.
Workflow публикует версионный образ в `ghcr.io/rasecosystem/ras-hub` и создаёт
GitHub Release с deployment bundle и файлом `SHA256SUMS`.

Перед созданием тега запустите локально те же проверки и упаковку:

```bash
make release
```

Релизный тег содержит точную семантическую версию с префиксом `v`, например
`v0.1.0-beta.1`, и должен указывать на коммит из `main`. Предварительные версии
не обновляют тег `latest`. В deployment bundle зафиксирована точная версия
образа; архив содержит production Compose, шаблон переменных окружения,
инструкцию по развёртыванию и лицензию, но не содержит секретов.

Веб-интерфейс показывает package version, сформированную из `version.json`.
Защищённый метод `GET /api/v1/info` возвращает полную informational version,
включая идентификатор сборки, для диагностики.

## Архитектура

RasHub хранит локальную теневую модель удалённой инфраструктуры
1С:Предприятия. Методы чтения shadow state обращаются к сохранённым данным и не
вызывают RasGate. Live-чтение, явное обновление shadow state, фоновый мониторинг
состояния и удалённые изменения выполняются через внутренний движок фоновых
задач.

```text
RasStudio Mono / Blazor / API
          |
       RasHub.Web
       /       \
shadow query  live / refresh / mutation / monitoring
     |                         |
query service         BackgroundTasks engine
     |                         |
  EF Core            Application task handler
     |                     /             \
PostgreSQL          resource gateway   shadow publisher
                           |                 |
                     RasGate session      EF Core
                           |                 |
                   RasGate -> RAC -> RAS  PostgreSQL
```

- `RasHub.Domain` содержит сохраняемые сущности, которыми владеет Hub.
- `RasHub.Application` содержит нормализованные удалённые модели, обработчики
  фоновых задач и контракты gateway для статуса, кластеров и информационных баз.
- `RasHub.Infrastructure` реализует gateway, persistence на EF Core и
  версионные RAC-адаптеры. Сессия одного Gate отвечает за общий HTTP envelope,
  endpoint, аутентификацию, обработку версии RAC и семантику ошибок.
- `RasHub.BackgroundTasks` — универсальный внутрипроцессный механизм выполнения;
  его очереди, расписания, дедупликация и ключи конкурентности не являются
  долговечными или распределёнными.
- `RasHub.Contracts` содержит версионированные wire-модели для клиентов API и не
  зависит от проектов серверной реализации.
- `RasHub.Web` отвечает за HTTP, Blazor, Identity, мониторинг и композицию
  процесса.

Текущий remote boundary поддерживает общий статус RasGate/RAC, snapshot и
административные операции с кластерами, а также snapshot и детальное чтение
информационных баз в рамках кластера. Полный snapshot коллекции может удалить
отсутствующие строки shadow state; адресное live-чтение обновляет только
запрошенный ресурс. Каждая публикация удалённых данных защищена ревизией
конфигурации RasGate. Фоновый мониторинг обновляет только общий статус
RasGate/RAC; shadow state кластеров и информационных баз изменяется через
live-чтение или явные команды обновления.

## API

Версионированный HTTP API находится под `/api/v1` и возвращает общий envelope
`ApiResponse<T>`. Контроллеры API аутентифицируют пользовательский
`X-Api-Key`. Изменение конфигурации RasGate и удалённые операции с кластерами
дополнительно требуют policy `ManageRasGates`, которая сейчас назначается
администраторам. Shadow-запросы не обращаются к RasGate. Live-чтение и явные
команды обновления ставят удалённую работу в очередь, публикуют проверенный
результат в shadow state и ожидают внутрипроцессный task handle перед ответом.

Глобальный поиск по RasGate, кластерам и информационным базам работает только с
сохранённым состоянием. Результаты поиска кластеров и баз содержат контекст
родительских сущностей и могут быть ограничены соответствующими идентификаторами
родителей.

Статус Gate в shadow state содержит имя и версию RasGate, а также доступность и
версию RAC. Состояние принимает значения `Unknown`, `Offline`, `Degraded` или
`Ready`. Доступный RasGate с недоступным или не проверенным RAC считается
деградировавшим, а не полностью готовым.

## Внутренняя документация

- [Карта кода и execution flows](docs/code-map.md)
- [Процесс релиза](docs/releasing.md)
- [Движок фоновых задач](src/RasHub.BackgroundTasks/README.md)
- [Наборы тестов](tests/README.md)
- [Граница совместимости RAC](docs/rac-compatibility.md)

## Связанные проекты

RasHub входит в [Ras Ecosystem](https://github.com/RasEcosystem):

- [RasGate](https://github.com/RasEcosystem/ras-gate) — лёгкий сервис, который
  предоставляет RasHub контролируемое выполнение команд RAC через HTTP;
- [RasStudio Mono](https://github.com/RasEcosystem/ras-studio-mono) —
  экспериментальный монолитный веб-клиент для администрирования инфраструктуры
  1С:Предприятия через RasHub.

## Лицензия

RasHub распространяется по [лицензии MIT](LICENSE).
