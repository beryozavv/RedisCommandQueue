namespace RedisCommandQueues;

public interface ICommandQueueService
{
    /// <summary>
    /// Добавление команды в очередь для устройства на стороне MDM
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="command"></param>
    /// <returns>Успешно ли добавление</returns>
    /// <remarks>Важно учесть идемпотентность метода!
    /// Метод вызывается в цикле для батча. При повторном вызове для команды она не добавится повторно,
    /// но ее score инкрементируется, что не ломает общую логику</remarks>
    Task<bool> AddCommand(string deviceId, CommandItem command);

    /// <summary>
    /// Получение команды устройством
    /// </summary>
    /// <param name="deviceId"></param>
    /// <returns>Актуальная команда</returns>
    Task<CommandItem?> GetCommand(string deviceId);

    /// <summary>
    /// Подтверждение обработки устройством
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="scoreId"></param>
    /// <param name="isCancelled"></param>
    /// <returns>Успешно ли подтверждение, то есть удален ли элемент</returns>
    Task<bool> ConfirmCommandProcessing(string deviceId, double scoreId, bool isCancelled);
}