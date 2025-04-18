using Microsoft.AspNetCore.Mvc;

namespace RedisCommandQueues;

[ApiController]
[Route("api/[controller]")]
public class CommandController : ControllerBase
{
    private readonly RedisCommandQueueService _commandService;

    public CommandController(RedisCommandQueueService commandService)
    {
        _commandService = commandService;
    }

    // Добавление команды в очередь
    [HttpPost("{clientId}/add")]
    public async Task<IActionResult> AddCommand(string clientId, [FromBody] CommandEntry commandData)
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
    public async Task<IActionResult> AcknowledgeCommand(string clientId, [FromBody] double scoreId)
    {
        await _commandService.ConfirmProcessingAsync(clientId, scoreId, false);
        return Ok();
    }
   
}