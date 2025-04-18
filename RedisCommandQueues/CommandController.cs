using Microsoft.AspNetCore.Mvc;

namespace RedisCommandQueues;

[ApiController]
[Route("api/[controller]")]
public class CommandController : ControllerBase
{
    private readonly IOSRedisCommandQueueService _commandService;

    public CommandController(IOSRedisCommandQueueService commandService)
    {
        _commandService = commandService;
    }

    // Добавление команды в очередь
    [HttpPost("{clientId}/add")]
    public async Task<IActionResult> AddCommand(string clientId, [FromBody] string commandData)
    {
        await _commandService.AddCommandAsync(clientId, commandData);
        return Ok();
    }

    // Получение команды
    [HttpGet("{clientId}/get")]
    public async Task<IActionResult> GetCommand(string clientId)
    {
        var command = await _commandService.GetCommandAsync(clientId);
        return command != null ? Ok(command) : NoContent();
    }

    // Подтверждение обработки команды
    [HttpPost("{clientId}/ack")]
    public async Task<IActionResult> AcknowledgeCommand(string clientId, [FromBody] string commandId)
    {
        await _commandService.ConfirmProcessingAsync(clientId, commandId, false);
        return Ok();
    }
   
}