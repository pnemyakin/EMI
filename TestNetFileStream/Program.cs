using EMI;
using EMI.Network.NetTCPV3;
using EMI.NetStream;
using System.Diagnostics;

Client client;

static void Client_Disconnected(string error)
{
    Console.WriteLine("Client_Disconnected => " + error);
}

// client
if (args.Length == 0 || args[0].Trim().ToLower() == "client")
{
    client = new(NetTCPV3Service.Service);
    client.Disconnected += Client_Disconnected;

reconect:
    Console.WriteLine("Попытка подключиться...");
    var status = await client.Connect("127.0.0.1#25566", default);
    if (status == false)
    {
        Console.WriteLine("Не удалось подключиться...");
        await Task.Delay(1000);
        goto reconect;
    }

    Console.WriteLine("Успех, нажмите чтобы продолжить");
    Console.ReadLine();
    Console.WriteLine("Попытка скачать файл");

    var f = new FileInfo("downloaded.png");
    if (f.Exists) f.Delete();

    using var file = File.Create("downloaded.png");
    var stopwatch = Stopwatch.StartNew();
    int lastPercent = -1;

    bool found = await FileDownloader.Download(
        client,
        hostId: 0,
        fileName: "test_file",
        destination: file,
        progress: info =>
        {
            int p = (int)info.Percent / 5;
            if (p != lastPercent)
            {
                lastPercent = p;
                Console.WriteLine("===============\n" + info.ToString());
            }
        },
        bufferSize: 1024 * 1024);

    stopwatch.Stop();

    if (found)
    {
        double mbps = file.Length / 1024.0 / 1024.0 / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"Файл загружен! {mbps:F2} MB/s");
    }
    else
    {
        Console.WriteLine("Файл не найден на сервере.");
    }
}
// server
else
{
    Server server = new Server(NetTCPV3Service.Service);
    Console.WriteLine("Нажмите кнопку чтобы запустить сервер");
    Console.ReadLine();

    server.Start("any#25566");
    Console.WriteLine("Ожидание клиента");

rep:
    try
    {
        client = await server.Accept();
        client.Disconnected += Client_Disconnected;
        Console.WriteLine("Клиент подключён");

        using var host = new FilesHost(client, 0, (string name) =>
        {
            Console.WriteLine("Запрос файла: " + name);
            if (name == "test_file")
            {
                return File.OpenRead("test.png");
            }
            return null;
        });

        Console.WriteLine("Нажмите Enter для завершения...");
        Console.ReadLine();
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
        goto rep;
    }
}