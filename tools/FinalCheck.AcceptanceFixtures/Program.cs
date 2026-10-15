using System.Text.Json;
using FinalCheck.AcceptanceFixtures;

try
{
    if (args.Length != 2 || args[0] is not ("generate" or "verify"))
    {
        Console.WriteLine("{\"success\":false,\"error\":\"Usage: generate|verify <absolute-directory>\"}");
        return 2;
    }

    var manifest = args[0] == "generate"
        ? AcceptanceFixtureCatalog.Generate(args[1])
        : AcceptanceFixtureCatalog.Verify(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { success = true, action = args[0], manifest }));
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
    ArgumentException or InvalidDataException or JsonException)
{
    // Never print arbitrary file contents, machine paths or a stack trace.
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = exception.GetType().Name }));
    return 1;
}
