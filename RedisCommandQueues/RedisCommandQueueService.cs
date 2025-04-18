using System.Text.Json;
using StackExchange.Redis;

namespace RedisCommandQueues;

/// <summary>
/// Важно учесть идемпотентность методов и, возможно, работу в конкурентной среде 
/// </summary>
public class RedisCommandQueueService
{
    private readonly IDatabase _database;

    const string Counter = "score";
    const string CommandsQueue = "commands";

    public RedisCommandQueueService(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    /// <summary>
    /// Добавление команды в очередь для устройства на стороне MDM 
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="command"></param>
    public async Task AddCommandAsync(string deviceId, CommandEntry command)
    {
        var score = await _database.StringIncrementAsync(GetCounterKey(deviceId)).ConfigureAwait(false);
        var serializedCommand = JsonSerializer.Serialize(command);
        await _database.SortedSetAddAsync(GetSortedSetKey(deviceId), serializedCommand, score, When.NotExists)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Получение команды устройством 
    /// </summary>
    /// <param name="deviceId"></param>
    /// <returns></returns>
    public async Task<CommandEntry?> GetCommandAsync(string deviceId)
    {
        var items = await _database.SortedSetRangeByRankWithScoresAsync(
            GetSortedSetKey(deviceId), 0, 0).ConfigureAwait(false);

        if (items.Length > 0)
        {
            var command = JsonSerializer.Deserialize<CommandEntry>(items[0].Element!);
            command!.ScoreId = items[0].Score;
            return command;
        }

        return null;
    }

    /// <summary>
    /// Подтверждение обработки устройством
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="scoreId"></param>
    /// <param name="isCancelled"></param>
    public async Task ConfirmProcessingAsync(string deviceId, double scoreId, bool isCancelled)
    {
        var removedCount =
            await _database.SortedSetRemoveRangeByScoreAsync(GetSortedSetKey(deviceId), scoreId, scoreId);
        if (removedCount != 1)
        {
            // лог removedCount todo
        }

        if (isCancelled)
        {
            // лог todo
        }
    }

    private static string GetCounterKey(string deviceId)
    {
        return $"{Counter}:{deviceId}";
    }

    private static string GetSortedSetKey(string deviceId)
    {
        return $"{CommandsQueue}:{deviceId}";
    }
}