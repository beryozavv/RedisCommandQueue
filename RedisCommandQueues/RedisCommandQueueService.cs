using System.Text.Json;
using StackExchange.Redis;

namespace RedisCommandQueues;

/// <summary>
/// Очереди команд для устройств на основе Redis.SortedSet
/// </summary>
/// <remarks>
/// Важно учесть идемпотентность методов и работу в конкурентной среде
/// </remarks>
internal class RedisCommandQueueService : ICommandQueueService
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
    public async Task<bool> AddCommand(string deviceId, CommandItem command)
    {
        var score = await _database.StringIncrementAsync(GetCounterKey(deviceId)).ConfigureAwait(false);
        var serializedCommand = JsonSerializer.Serialize(command);
        var result = await _database.SortedSetAddAsync(GetSortedSetKey(deviceId), serializedCommand, score, When.Always)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Получение команды устройством
    /// </summary>
    /// <param name="deviceId"></param>
    /// <returns></returns>
    public async Task<CommandItem?> GetCommand(string deviceId)
    {
        var items = await _database.SortedSetRangeByRankWithScoresAsync(
            GetSortedSetKey(deviceId), 0, 0).ConfigureAwait(false);

        if (items.Length > 0)
        {
            var command = JsonSerializer.Deserialize<CommandItem>(items[0].Element!);
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
    public async Task<bool> ConfirmCommandProcessing(string deviceId, double scoreId, bool isCancelled)
    {
        var removedCount =
            await _database.SortedSetRemoveRangeByScoreAsync(GetSortedSetKey(deviceId), scoreId, scoreId)
                .ConfigureAwait(false);
        if (removedCount != 1)
        {
            // лог removedCount todo
        }

        if (isCancelled)
        {
            // лог todo
        }

        return removedCount == 1;
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