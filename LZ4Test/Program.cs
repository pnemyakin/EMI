// Quick LZ4Codec round-trip test
using EMI.Network;
using EMI.NGC;

Console.WriteLine("=== LZ4Codec Round-trip Test ===\n");

// Test 1: small data
Test("Small (10 bytes)", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

// Test 2: 35353 bytes of realistic file path + hash data
var data = BuildFileListData(190);
Console.WriteLine($"Generated test data: {data.Length} bytes");
Test($"FileList ({data.Length} bytes)", data);

// Test 3: Offset != 0 case (what ProccesAccept does for ping)
TestWithOffset("Ping with Offset=1", new byte[] { 2, 100, 200, 0, 0, 0, 0, 0, 0 }, offset: 1);

static byte[] BuildFileListData(int fileCount)
{
    var ms = new System.IO.MemoryStream();
    var bw = new System.IO.BinaryWriter(ms);
    for (int i = 0; i < fileCount; i++)
    {
        string path = $"client_windows/Data/Level_{i:D3}/Asset_{i:D4}.bundle";
        string hash = $"{Guid.NewGuid():N}";
        bw.Write(path);
        bw.Write(hash);
        bw.Write((long)(1024 * 1024 + i * 1337));
    }
    return ms.ToArray();
}

static void Test(string name, byte[] original)
{
    var mw = new LZ4Middleware();
    var input = new EasyArray(original);

    var compressed = mw.ProcessOutgoing(input);
    Console.WriteLine($"[{name}]");
    Console.WriteLine($"  Original:   {original.Length} bytes, Offset={input.Offset}");
    Console.WriteLine($"  Compressed: {compressed.Length} bytes, Bytes.Length={compressed.Bytes?.Length}");
    Console.WriteLine($"  First 20 compressed bytes: [{string.Join(",", compressed.Bytes.Take(Math.Min(20, compressed.Length)))}]");

    // Also test raw LZ4Codec directly to see what compressedSize is reported
    // (ProcessOutgoing returns Length=compressed size including 5-byte header)
    byte flag = compressed.Bytes[0];
    Console.WriteLine($"  Flag: {flag} ({(flag == 0 ? "RAW" : flag == 1 ? "COMPRESSED" : "UNKNOWN")})");
    if (flag == 1) {
        int storedOrigSize = compressed.Bytes[1] | (compressed.Bytes[2] << 8) | (compressed.Bytes[3] << 16) | (compressed.Bytes[4] << 24);
        Console.WriteLine($"  Stored original size: {storedOrigSize}");
    }

    try
    {
        var decompressed = mw.ProcessIncoming(compressed);
        Console.WriteLine($"  Decompressed: {decompressed.Length} bytes");
        bool match = decompressed.Length == original.Length;
        if (match)
        {
            for (int i = 0; i < original.Length; i++)
            {
                if (decompressed.Bytes[i] != original[i]) { match = false; break; }
            }
        }
        Console.WriteLine($"  Match: {(match ? "OK ✓" : "FAIL ✗")}");
        decompressed.Dispose();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR: {ex.Message}");
    }
    compressed.Dispose();
    Console.WriteLine();
}

static void TestWithOffset(string name, byte[] original, int offset)
{
    var mw = new LZ4Middleware();
    var input = new EasyArray(original);
    input.Offset = offset;

    var compressed = mw.ProcessOutgoing(input);
    Console.WriteLine($"[{name}]");
    Console.WriteLine($"  Original:    {original.Length} bytes, Offset={offset}");
    Console.WriteLine($"  Compressed:  {compressed.Length} bytes");

    try
    {
        var decompressed = mw.ProcessIncoming(compressed);
        Console.WriteLine($"  Decompressed: {decompressed.Length} bytes (expected {original.Length - offset})");
        decompressed.Dispose();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR: {ex.Message}");
    }
    compressed.Dispose();
    Console.WriteLine();
}
