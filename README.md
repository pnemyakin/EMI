# EMI

Сетевая библиотека на C# для построения клиент-серверных приложений реального времени. Предоставляет высокоуровневую систему удалённого вызова процедур (RPC) поверх сменяемых транспортных протоколов, с бинарной сериализацией, шифрованием, сжатием и встроенной диагностикой.

Библиотека создавалась для задач, где нужно вызывать методы на удалённой машине так, как если бы они были локальными, без ручной работы с сокетами и форматами пакетов.

## Возможности

**RPC** : регистрация и вызов удалённых методов с поддержкой до 20 параметров и возвращаемых значений. Гарантированная и негарантированная доставка, серверное перенаправление вызовов между клиентами.

**Транспорт** : подключаемый через интерфейсы `INetworkService` / `INetworkClient` / `INetworkServer`. Из коробки работают TCP (NetTCPV3) и UDP (NetUDP) с собственным слоем надёжной доставки, фрагментацией и подтверждениями.

**Middleware** : конвейер обработки пакетов. Встроены LZ4-сжатие и AES-256-GCM шифрование с автоматическим обменом ключами.

**SyncInterface** : кодогенерация через IL-emit. Обычный C#-интерфейс превращается в двусторонний RPC-контракт. Поддержка `async Task<T>`, атрибуты `[OnlyServer]`, `[OnlyClient]`, `[RCTypeOption]`.

**NetStream** : прозрачный доступ к `System.IO.Stream` по сети. Чтение, запись, позиционирование удалённых файлов. Высокоуровневый `FileDownloader` с отчётом о прогрессе.

**NGC** : пул массивов байт с автоочисткой для снижения нагрузки на сборщик мусора.

**Headless** : обработчик RPC без встроенного транспорта, для интеграции с пользовательскими каналами (Steam P2P и подобное).

**Debugger** : WPF-приложение для подключения к работающему серверу. Просмотр состояния клиентов, зарегистрированных RPC-методов, пула NGC, логов в реальном времени.

## Быстрый старт

Сервер:

```csharp
var server = new Server(NetTCPV3Service.Service);
server.UseEncryption = true;
server.UseCompression = true;

var indicator = new Indicator.Func<string>("ChatMessage");
server.RPC.RegisterMethod((string msg) => Console.WriteLine(msg), indicator);

server.Start("127.0.0.1#9000");

while (server.IsRun)
{
    Client client = await server.Accept();
}
```

Клиент:

```csharp
var client = new Client(NetTCPV3Service.Service);
client.UseEncryption = true;
client.UseCompression = true;

await client.Connect("127.0.0.1#9000", CancellationToken.None);

var indicator = new Indicator.Func<string>("ChatMessage");
await indicator.RCall("Привет!", client, RCType.Guaranteed);
```

## Структура проекта

| Модуль | Назначение | Фреймворк |
|---|---|---|
| EMI | Ядро: RPC, индикаторы, NGC, middleware, headless | netstandard2.1 |
| EMI.NetTCPV3 | TCP-транспорт с сегментацией пакетов | netstandard2.1 |
| EMI.NetUDP | UDP-транспорт с надёжной доставкой | netstandard2.1 |
| EMI.SyncInterface | IL-emit компилятор интерфейсов в RPC | netstandard2.1 |
| EMI.NetStream | Удалённые потоки и загрузка файлов | net4.8 |
| EMI.DebugServer | Серверный мост для отладчика | net9.0 |
| EMI.Debugger | WPF-отладчик | net9.0-windows |

## Зависимости для сборки

Проект использует библиотеку **[SmartPackager](https://github.com/pnemyakin/SmartPackager)** для бинарной сериализации. При клонировании репозитория необходимо также клонировать SmartPackager в соседний каталог:

```
Projects/
├── EMI/          ← этот репозиторий
└── SmartPackager/ ← https://github.com/pnemyakin/SmartPackager
```

## Документация

Подробное описание архитектуры, API и примеры использования находятся в каталоге [docs](docs/).

## Лицензия

Смотри [LICENSE.txt](LICENSE.txt).
