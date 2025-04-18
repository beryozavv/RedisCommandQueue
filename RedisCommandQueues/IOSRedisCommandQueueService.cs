using System.Text.Json;
using StackExchange.Redis;

namespace RedisCommandQueues;

/// <summary>
/// Важно учесть идемпотентность методов и, возможно, работу в конкурентной среде 
/// </summary>
public class IOSRedisCommandQueueService
{
    private readonly IDatabase _database;

    const string ProcessingQueue = "processing";
    const string CommandsQueue = "commands";

    public IOSRedisCommandQueueService(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    
    /// <summary>
    /// Добавление команды в очередь commands для устройства на стороне MDM 
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="command"></param>
    public async Task AddCommandAsync(string deviceId, string command)
    {
        // id = md5(command). если такой id уже есть в коллекции, то не добавляем и пишем в лог
        // Ключ для очереди входящих команд
        string commandsKey = GetCommandsKey(deviceId);
        await _database.ListLeftPushAsync(commandsKey, command);
    }

    
    /// <summary>
    /// Получение команды устройством 
    /// </summary>
    /// <param name="deviceId"></param>
    /// <returns></returns>
    public async Task<string?> GetCommandAsync(string deviceId)
    {
        var processingKey = GetProcessingKey(deviceId);
        var cmd = await _database.ListGetByIndexAsync(processingKey, -1); // брать последний элемент
        if (cmd.HasValue)
        {
            return cmd;
        }

        var commandsKey = GetCommandsKey(deviceId);
        cmd = await _database.ListRightPopLeftPushAsync(commandsKey, processingKey);
        return cmd;
    }

    /// <summary>
    /// Подтверждение обработки устройством
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="commandId"></param>
    /// <param name="isCancelled"></param>
    public async Task ConfirmProcessingAsync(string deviceId, string commandId, bool isCancelled)
    {
        string processingKey = GetProcessingKey(deviceId);
        var procList = await _database.ListRangeAsync(processingKey);
        foreach (var command in procList)
        {
            var entry = JsonSerializer.Deserialize<CommandEntry>(command.ToString());
            if (entry?.Id == commandId)
            {
                await _database.ListRemoveAsync(processingKey, command, count: 1);
                if (isCancelled)
                {
                    // запись в БД через очередь? todo
                }
            }
        }
    }

    private static string GetProcessingKey(string deviceId)
    {
        return $"{ProcessingQueue}:{deviceId}";
    }

    private static string GetCommandsKey(string deviceId)
    {
        return $"{CommandsQueue}:{deviceId}";
    }
}