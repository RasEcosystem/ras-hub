[English](README.md) | [Русский](README.ru.md)

# RasHub

RasHub — центральный сервис управления для
[RasStudio Mono](https://github.com/RasEcosystem/ras-studio-mono). Он
предоставляет версионированный API, хранит локальное теневое состояние
инфраструктуры 1С:Предприятия и передаёт RAC-операции одному или нескольким
экземплярам [RasGate](https://github.com/RasEcosystem/ras-gate). Общие модели
API находятся в сабмодуле [`RasHub.Contracts`](src/RasHub.Contracts).

## Текущие возможности

- интерфейс администрирования на Blazor и HTTP API `/api/v1`;
- сохранение RasGate, RAS endpoints, кластеров, информационных баз и статусов;
- live-чтение, обновление shadow state и администрирование кластеров через RAC;
- версионные RAC-адаптеры с текущей минимальной версией `8.3.27.2214`;
- внутрипроцессные фоновые задачи и поддержка одной реплики RasHub.

Чтение shadow state не обращается к RasGate. Ресурсная операция адресуется
управляемому RAS endpoint и выполняется через назначенный ему активный RasGate.
Результат публикуется только при актуальных ревизиях endpoint и назначенного
Gate.

## Требования

- .NET SDK 10;
- Git и Make;
- Docker Engine с Compose v2.

Для удалённого управления также нужен RasGate с сетевым доступом к настроенным
RAS endpoints и совместимым RAC.

## Сборка и разработка

```bash
git submodule update --init --recursive
make build
make test
```

Команда `make dev-up` запускает PostgreSQL и Seq для работы приложения из IDE.
`make dev-stack-up` запускает весь контейнерный стек, а `make dev-down`
останавливает его. В окружении Development авторизованная документация API
доступна по адресу `/swagger`.

Список корневых команд выводит `make help`, а команд развёртывания и миграций —
`make -C deploy help`.

## Релизы

RasHub распространяется как Linux AMD64 контейнер с небольшим deployment
bundle. Перед подготовкой релизного тега выполните:

```bash
make release
```

Команда проверяет форматирование, выполняет Release-сборку без предупреждений,
запускает все тесты и проверяет deployment archive. Правила тегирования и
публикации описаны в [процедуре релиза](docs/releasing.md).

## Документация

- [Локальное развёртывание и запуск из исходников](deploy/README.md)
- [Развёртывание готового container bundle](deploy/README.release.md)
- [Карта кода и execution flows](docs/code-map.md)
- [Совместимость с RAC](docs/rac-compatibility.md)
- [Движок фоновых задач](src/RasHub.BackgroundTasks/README.md)
- [Наборы тестов](tests/README.md)

## Связанные проекты

RasHub входит в [Ras Ecosystem](https://github.com/RasEcosystem):

- [RasGate](https://github.com/RasEcosystem/ras-gate) выполняет контролируемые
  RAC-команды для RasHub;
- [RasStudio Mono](https://github.com/RasEcosystem/ras-studio-mono) — клиент
  администрирования, использующий API RasHub.

## Лицензия

RasHub распространяется по [лицензии MIT](LICENSE).
