# Руководство по использованию

## Подключение библиотеки

EMI собирается под `netstandard2.1`. Добавьте ссылку на проект `EMI` и один из транспортных модулей:

```xml
<ProjectReference Include="..\EMI\EMI.csproj" />
<ProjectReference Include="..\EMI.NetTCPV3\EMI.NetTCPV3.csproj" />
```

Для UDP замените `EMI.NetTCPV3` на `EMI.NetUDP`.

## Создание сервера

```csharp
using EMI;
using EMI.NetTCPV3;

var server = new Server(NetTCPV3Service.Service);
server.Start("127.0.0.1#9000");

while (server.IsRun)
{
    Client client = await server.Accept();
    Console.WriteLine($"Подключён: {client.RemoteAddress}");
}
```

Формат адреса: `IP#Port`. Разделитель `#`, не `:`.

Остановка сервера:

```csharp
server.Stop();
```

## Создание клиента

```csharp
using EMI;
using EMI.NetTCPV3;

var client = new Client(NetTCPV3Service.Service);
bool connected = await client.Connect("127.0.0.1#9000", CancellationToken.None);

if (connected)
{
    Console.WriteLine("Подключено");
}
```

Отключение:

```csharp
client.Disconnect("причина");
```

Событие отключения:

```csharp
client.Disconnected += (reason) =>
{
    Console.WriteLine($"Отключён: {reason}");
};
```

## Регистрация RPC-методов

Метод на сервере, который смогут вызвать все клиенты:

```csharp
var indicator = new Indicator.Func<string>("SendMessage");
server.RPC.RegisterMethod((string msg) =>
{
    Console.WriteLine($"Сообщение: {msg}");
}, indicator);
```

Метод на клиенте, который сможет вызвать сервер:

```csharp
var indicator = new Indicator.Func<int, int>("UpdateScore");
client.LocalRPC.RegisterMethod((int playerId, int score) =>
{
    Console.WriteLine($"Игрок {playerId}: {score} очков");
}, indicator);
```

Метод с возвращаемым значением:

```csharp
var indicator = new Indicator.FuncOut<DateTime>("GetServerTime");
server.RPC.RegisterMethod(() => DateTime.UtcNow, indicator);
```

Снятие регистрации:

```csharp
IRPCRemoveHandle handle = server.RPC.RegisterMethod(...);
handle.Remove();
```

Групповое снятие:

```csharp
var group = new RemoveHandleGroup();
group.Add(server.RPC.RegisterMethod(...));
group.Add(server.RPC.RegisterMethod(...));
group.RemoveAll();
```

## Вызов удалённых методов

Вызов без ожидания результата (гарантированная доставка):

```csharp
var indicator = new Indicator.Func<string>("SendMessage");
await indicator.RCall("Привет!", client, RCType.Guaranteed);
```

Вызов без гарантии доставки (подходит для частых обновлений позиции):

```csharp
await indicator.RCall("данные", client, RCType.Fast);
```

Вызов с получением результата:

```csharp
var indicator = new Indicator.FuncOut<DateTime>("GetServerTime");
DateTime time = await indicator.RCall(client, RCType.ReturnWait);
```

## Типы вызовов (RCType)

`Fast` : отправить и забыть. Пакет может не дойти. Подходит для потоковых данных (позиции, повороты), где следующий пакет сделает предыдущий неактуальным.

`Guaranteed` : гарантированная доставка без ожидания ответа. Для событий, которые должны дойти (чат, команды).

`ReturnWait` : гарантированная доставка с ожиданием возвращаемого значения. Для запросов (получить время, проверить авторизацию).

`Forwarding` : сервер перенаправляет вызов указанным клиентам. Для relay-сценариев.

`FastForwarding` : то же, но без гарантии доставки.

## Перенаправление вызовов

Сервер может перенаправлять вызовы от одного клиента другим:

```csharp
var indicator = new Indicator.Func<string>("BroadcastMessage");
server.RPC.RegisterForwarding(indicator, (Client sender) => server.ServerClients);
```

В этом примере любое сообщение, отправленное клиентом с типом `RCType.Forwarding`, будет передано всем остальным подключённым клиентам.

## Сжатие

Включается до запуска сервера или подключения клиента (настройки должны совпадать):

```csharp
server.UseCompression = true;   // LZ4
client.UseCompression = true;
```

## Шифрование

EMI использует AES-256-GCM. Ключ **никогда не передаётся по сети открытым текстом** — его
согласует выбранная стратегия обмена ключами (`IKeyExchange`). Уровень защиты выбирается
под вашу инфраструктуру и модель угроз — см. [лестницу уровней](security.md).

Базовое включение (уровень 2, RSA key-transport — защита от пассивной прослушки):

```csharp
// Сервер
server.UseEncryption = true;    // при Start() создаётся RSA-стратегия по умолчанию
server.Start("127.0.0.1#9000");

// Клиент
client.UseEncryption = true;    // получит и проверит ключ в рукопожатии
await client.Connect("127.0.0.1#9000", token);
```

Защита от активного MITM (уровень 3, пиннинг публичного ключа сервера):

```csharp
// Сервер: стабильная RSA-пара, публичный ключ раздаётся клиентам
var kex = RsaKeyExchange.CreateServer();
byte[] serverPublicKey = kex.ExportPublicKey();   // встроить в билд клиента
server.UseEncryption = true;
server.KeyExchange = kex;

// Клиент: сверяет ключ сервера с закреплённым
client.UseEncryption = true;
client.KeyExchange = RsaKeyExchange.CreateClientPinned(serverPublicKey);
```

Pre-Shared Key (уровень 1, ключ роздан вне сети, по проводу не идёт вообще):

```csharp
byte[] psk = /* 32 байта, розданы обеим сторонам безопасно */;
server.UseEncryption = true;
server.KeyExchange = new PreSharedKeyExchange(psk);
client.UseEncryption = true;
client.KeyExchange = new PreSharedKeyExchange(psk);
```

Подробнее о выборе уровня, гарантиях и ограничениях — [docs/security.md](security.md).

## Поток исполнения RPC (Unity и другие движки)

По умолчанию входящие RPC-обработчики выполняются на пуле потоков (`ThreadPoolDispatcher`) —
максимальный параллелизм, порядок не гарантируется. Это поведение как раньше, менять ничего не нужно.

Движки вроде Unity требуют, чтобы колбэки исполнялись в главном потоке. Задайте диспетчер:

```csharp
// Unity: складываем вызовы в очередь и качаем помпу в главном потоке
var pump = new PumpDispatcher();
client.Dispatcher = pump;          // или server.Dispatcher — наследуется всеми клиентами

void Update() => pump.Pump();      // RPC-обработчики выполнятся здесь, в main-потоке (FIFO)
```

Готовые стратегии:

| Диспетчер | Где исполняет | Порядок |
|---|---|---|
| `ThreadPoolDispatcher` (по умолчанию) | пул потоков | без гарантий |
| `PumpDispatcher` | поток, вызвавший `Pump()` | FIFO |
| `SynchronizationContextDispatcher` | захваченный контекст (Unity/WPF main) | FIFO |
| `InlineDispatcher` | сетевой поток приёма, синхронно | последовательный |

`Dispatcher` — это `IRpcDispatcher` с единственным методом `Post(Action call)`. Реализовав
его самостоятельно, можно задать любую модель: ограниченный параллелизм, приоритетную очередь,
маршрутизацию тяжёлых вызовов на пул, а лёгких — в main-поток и т.п. Выбор потоков и порядка —
это свойство диспетчера, а не библиотеки.

Десериализация аргументов и отправка результата остаются на сетевом потоке — в диспетчер
уходит только вызов пользовательского обработчика, поэтому main-поток не грузится парсингом и I/O.

Бюджет кадра: `Pump()` исполняет всё накопленное, `Pump(int max)` — не более N вызовов,
`Pump(TimeSpan budget)` — пока не истечёт время, `Pump(int max, TimeSpan budget)` — комбинированно.
Бюджет проверяется между вызовами (не прерывает уже запущенный обработчик), но хотя бы один
вызов исполняется всегда.

**Async-обработчики (`async Task` / `async Task<T>`) не блокируют поток диспетчера.** Регистрация
таких методов идёт через неблокирующий путь: обработчик выполняется до `await`, продолжение
возвращается в тот же диспетчер (`ConfigureAwait(true)`), ответ отправляется по завершении `Task`.
На `PumpDispatcher` продолжение после `await` достаётся следующему `Pump()` — кадр не стопорится
и дедлока на main-потоке нет.

> **Headless:** там `AcceptPacket` исполняется синхронно на вашем потоке — для main-потока
> движка просто вызывайте его из главного цикла. Отдельный диспетчер не нужен.

Можно добавить свой middleware:

```csharp
server.Middlewares.Add(new MyCustomMiddleware());
```

## SyncInterface (интерфейсы как RPC-контракт)

Самый удобный способ работы с RPC. Определяете обычный C#-интерфейс, а библиотека генерирует прокси через IL-emit.

Определение контракта:

```csharp
public interface IGameAPI
{
    void SendChat(string message);

    [OnlyServer]
    int GetPlayerCount();

    [RCTypeOption(RCType.Fast)]
    void UpdatePosition(float x, float y, float z);

    Task<bool> AuthenticateAsync(string token);
}
```

На сервере: регистрация реализации:

```csharp
var sync = new SyncInterface<IGameAPI>("GameAPI");
sync.RegisterClass(server, new GameAPIImplementation());
```

На клиенте: получение прокси:

```csharp
var sync = new SyncInterface<IGameAPI>("GameAPI");
IGameAPI api = sync.NewIndicator(client);

api.SendChat("Привет");
bool ok = await api.AuthenticateAsync("my-token");
```

Атрибуты:

`[OnlyServer]` : метод вызывается только клиентом на сервере.

`[OnlyClient]` : метод вызывается только сервером на клиенте.

`[RCTypeOption(RCType.Fast)]` : переопределение типа доставки для конкретного метода.

`[AsyncCompile]` : пометка для асинхронной компиляции.

## Работа с UDP

Замена TCP на UDP не требует изменения кода RPC. Достаточно передать другой сервис:

```csharp
var server = new Server(NetUDPService.Service);
server.Start("127.0.0.1#9000");

var client = new Client(NetUDPService.Service);
await client.Connect("127.0.0.1#9000", token);
```

UDP-транспорт автоматически обеспечивает:
надёжную доставку (для `Guaranteed` и `ReturnWait`),
фрагментацию крупных сообщений,
heartbeat и определение разрыва связи.

## Удалённые потоки (NetStream)

Сервер выставляет файл:

```csharp
var host = new FilesHost(server, "files");
// теперь клиенты могут запрашивать файлы по имени
```

Клиент скачивает:

```csharp
using var localFile = File.Create("local.bin");
bool ok = await FileDownloader.Download(
    client,
    hostId: "files",
    fileName: "data.bin",
    output: localFile,
    onProgress: info => Console.WriteLine($"{info.Progress}%"),
    bufferSize: 4096
);
```

Низкоуровневый доступ к удалённому потоку:

```csharp
// Сервер
var streamHost = new NetStreamHost(stream, client, "myStream");

// Клиент
var remoteStream = new NetStreamRemote(client, "myStream");
byte[] buf = new byte[1024];
int read = remoteStream.Read(buf, 0, buf.Length);
```

`NetStreamRemote` реализует `System.IO.Stream` и поддерживает все стандартные операции: `Read`, `Write`, `Seek`, `SetLength`, `Close`.

## Headless (пользовательский транспорт)

Для интеграции с Steam P2P или другими сетевыми API, где у вас уже есть канал связи:

```csharp
var handler = new HeadlessHandler();

// Регистрация RPC как обычно
handler.RPC.RegisterMethod((string msg) => { ... }, indicator);

// Установка делегата отправки
handler.SendPacket = (byte[] data) =>
{
    SteamNetworking.SendPacket(peerId, data);
};

// При получении данных от Steam
handler.AcceptPacket(receivedBytes);
```

## Настройки клиента

```csharp
client.PingTimeout = TimeSpan.FromMinutes(2);       // таймаут отключения при потере связи
client.PingPollingInterval = TimeSpan.FromSeconds(10); // частота пинга
client.MaxPacketAcceptSize = 10 * 1024 * 1024;       // максимальный размер пакета (10 МБ)
```

Доступные метрики:

```csharp
TimeSpan ping = client.Ping;              // текущий RTT
double speed = client.SendByteSpeed;       // скорость отправки (байт/сек)
double delivered = client.DeliveredRate;   // доля доставленных пакетов (0.0 .. 1.0)
```

## Отладчик

Для использования встроенного отладчика:

1. Соберите `EMI` в конфигурации DEBUG.
2. Создайте `EDebuggerServer` и передайте ему рабочий сервер:

```csharp
var debugServer = new EDebuggerServer(server);
```

3. Запустите приложение `EMI.Debugger` и подключитесь к серверу отладки.

Отладчик показывает состояние сервера и клиентов, зарегистрированные RPC-методы, пул NGC и поток логов.

## Логирование

В DEBUG-сборке работает система логирования:

```csharp
Logger.OnMessage += (LogMessage msg) =>
{
    Console.WriteLine($"[{msg.Type}] {msg.Text}");
};
```

Типы: `Message`, `Warning`, `Error`, `CriticalError`.

В Release-сборке логирование отключено и не добавляет накладных расходов.
