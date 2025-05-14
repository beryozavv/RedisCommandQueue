using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace RedisCommandQueues.Tests;

public class RedisCommandQueueTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer;
    private IServiceProvider _serviceProvider = null!;
    private ICommandQueueService _commandQueueService = null!;

    public RedisCommandQueueTests()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .WithPortBinding(6379, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);

        services.AddConfigurationDeliveryInfrastructure();

        _serviceProvider = services.BuildServiceProvider();
        _commandQueueService = _serviceProvider.GetRequiredService<ICommandQueueService>();
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.StopAsync();
    }

    [Fact]
    public async Task AddConfirmCommand()
    {
        // Arrange
        string deviceId = "test-device-1";
        var command = new CommandItem(1, Guid.NewGuid(), new XDocument(new XElement("plist")).ToString());

        // Act
        await _commandQueueService.AddCommand(deviceId, command);

        // Assert - We'll verify it was added by retrieving it
        var retrievedCommand = await _commandQueueService.GetCommand(deviceId);
        var isConfirm1 =
            await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand!.ScoreId!.Value, false);
        var retrievedAfterConfirm = await _commandQueueService.GetCommand(deviceId);
        var isConfirm2 =
            await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand.ScoreId!.Value, false);

        Assert.NotNull(retrievedCommand);
        Assert.Equal(command.Id, retrievedCommand.Id);
        Assert.Equal(command.BatchId, retrievedCommand.BatchId);
        Assert.NotNull(retrievedCommand.ScoreId); // Score should be assigned
        Assert.True(isConfirm1);
        Assert.False(isConfirm2);
        Assert.Null(retrievedAfterConfirm);
    }

    [Fact]
    public async Task AddConfirmMultipleCommands()
    {
        // Arrange
        string deviceId = "test-device-2";
        var command1 = new CommandItem(1, Guid.NewGuid(),
            new XDocument(new XElement("plist", new XAttribute("version", "1.0"))).ToString());
        var command2 = new CommandItem(2, Guid.NewGuid(),
            new XDocument(new XElement("plist", new XAttribute("version", "2.0"))).ToString());

        // Add commands to queue
        await _commandQueueService.AddCommand(deviceId, command1);
        await _commandQueueService.AddCommand(deviceId, command2);

        // Act
        var retrievedCommand1 = await _commandQueueService.GetCommand(deviceId);
        var retrievedCommand11 = await _commandQueueService.GetCommand(deviceId);
        await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand1!.ScoreId!.Value, false);
        var retrievedCommand2 = await _commandQueueService.GetCommand(deviceId);
        await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand2!.ScoreId!.Value, false);
        var retrievedCommand3 = await _commandQueueService.GetCommand(deviceId);
        var isSecondConfirm =
            await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand2.ScoreId!.Value, false);

        // Assert
        Assert.NotNull(retrievedCommand1);
        Assert.NotNull(retrievedCommand11);
        Assert.Equal(retrievedCommand1.Id, retrievedCommand11.Id); // Should return first command (lowest score);
        Assert.Equal(command1.Id, retrievedCommand1.Id); // Should return first command (lowest score)
        Assert.Equal(command1.BatchId, retrievedCommand1.BatchId);
        Assert.NotNull(retrievedCommand1.ScoreId);

        Assert.NotNull(retrievedCommand2);
        Assert.Equal(command2.Id, retrievedCommand2.Id); // Should return first command (lowest score)
        Assert.Equal(command2.BatchId, retrievedCommand2.BatchId);
        Assert.NotNull(retrievedCommand2.ScoreId);
        Assert.True(retrievedCommand2.ScoreId > retrievedCommand1.ScoreId);

        Assert.False(isSecondConfirm);
        Assert.Null(retrievedCommand3); // Should return null when queue is empty
    }

    [Fact]
    public async Task AddCommandTwicePreservesIdempotency()
    {
        // Arrange
        string deviceId = "test-device-5";
        var command = new CommandItem(1, Guid.NewGuid(), new XDocument(new XElement("plist")).ToString());

        // Act - Add the same command twice
        var isAddedFirst = await _commandQueueService.AddCommand(deviceId, command);
        var retrievedCommandFirst = await _commandQueueService.GetCommand(deviceId);
        Assert.True(isAddedFirst);
        Assert.NotNull(retrievedCommandFirst);
        var isAddedSecond = await _commandQueueService.AddCommand(deviceId, command); //just increase a command score
        Assert.False(isAddedSecond);

        // Assert - Should only be one command in queue
        var retrievedCommandSecond = await _commandQueueService.GetCommand(deviceId);
        Assert.NotNull(retrievedCommandSecond);

        Assert.Equal(retrievedCommandFirst.Id, retrievedCommandSecond.Id);
        Assert.Equal(retrievedCommandFirst.BatchId, retrievedCommandSecond.BatchId);
        Assert.NotEqual(retrievedCommandFirst.ScoreId, retrievedCommandSecond.ScoreId);
        Assert.True(retrievedCommandSecond.ScoreId > retrievedCommandFirst.ScoreId);

        // Confirm and check that the queue is now empty (only one command was there)
        var isConfirmedFirst =
            await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommandFirst.ScoreId!.Value, false);
        Assert.False(isConfirmedFirst);
        var isConfirmedSecond =
            await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommandSecond.ScoreId!.Value, false);
        Assert.True(isConfirmedSecond);
        var afterConfirmation = await _commandQueueService.GetCommand(deviceId);
        Assert.Null(afterConfirmation);
    }

    [Fact]
    public async Task AddCommandsBatchTwicePreservesIdempotency()
    {
        // Arrange
        string deviceId = "test-device-7";

        var commandsBatch = new List<CommandItem>
        {
            new(1, Guid.NewGuid(), new XDocument(new XElement("plist1")).ToString()),
            new(2, Guid.NewGuid(), new XDocument(new XElement("plist2")).ToString()),
            new(3, Guid.NewGuid(), new XDocument(new XElement("plist3")).ToString()),
            new(4, Guid.NewGuid(), new XDocument(new XElement("plist4")).ToString()),
            new(5, Guid.NewGuid(), new XDocument(new XElement("plist5")).ToString())
        };

        // part of batch
        for (int i = 0; i < 3; i++)
        {
            await _commandQueueService.AddCommand(deviceId, commandsBatch[i]);
        }

        var retrievedFirst = await _commandQueueService.GetCommand(deviceId);
        Assert.Equal(commandsBatch[0].Id, retrievedFirst!.Id);
        Assert.Equal(commandsBatch[0].BatchId, retrievedFirst.BatchId);
        Assert.Equal(retrievedFirst.ScoreId, retrievedFirst.Id);

        // full batch
        foreach (var commandItem in commandsBatch)
        {
            await _commandQueueService.AddCommand(deviceId, commandItem);
        }

        foreach (var commandItem in commandsBatch)
        {
            var retrievedCommand = await _commandQueueService.GetCommand(deviceId);
            var isConfirmed =
                await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand!.ScoreId!.Value, false);
            Assert.NotNull(retrievedCommand);
            Assert.True(isConfirmed);
            Assert.Equal(retrievedCommand.Id, commandItem.Id);
            Assert.Equal(retrievedCommand.BatchId, commandItem.BatchId);
            Assert.True(retrievedCommand.ScoreId > commandItem.Id);
        }
    }

    [Fact]
    public async Task AddMultipleCommandMaintainsOrder()
    {
        // Arrange
        string deviceId = "test-device-6";
        var batchId1 = Guid.NewGuid();
        var batchId2 = Guid.NewGuid();

        var command1 = new CommandItem(1, batchId1,
            new XDocument(new XElement("plist", new XAttribute("batch", "1"))).ToString());
        var command2 = new CommandItem(2, batchId1,
            new XDocument(new XElement("plist", new XAttribute("batch", "1"))).ToString());
        var command3 = new CommandItem(3, batchId2,
            new XDocument(new XElement("plist", new XAttribute("batch", "2"))).ToString());

        // Act
        await _commandQueueService.AddCommand(deviceId, command1);
        await _commandQueueService.AddCommand(deviceId, command2);
        await _commandQueueService.AddCommand(deviceId, command3);

        // Assert - Verify they come out in order (first in, first out)
        var first = await _commandQueueService.GetCommand(deviceId);
        Assert.NotNull(first);
        Assert.Equal(1, first.Id);

        // Confirm first command processed
        await _commandQueueService.ConfirmCommandProcessing(deviceId, first.ScoreId!.Value, false);

        // Get second command
        var second = await _commandQueueService.GetCommand(deviceId);
        Assert.NotNull(second);
        Assert.Equal(2, second.Id);
        Assert.Equal(second.BatchId, first.BatchId);

        // Confirm second command processed
        await _commandQueueService.ConfirmCommandProcessing(deviceId, second.ScoreId!.Value, false);

        // Get third command
        var third = await _commandQueueService.GetCommand(deviceId);
        Assert.NotNull(third);
        Assert.Equal(3, third.Id);
        Assert.NotEqual(third.BatchId, second.BatchId);
    }

    [Fact]
    public async Task ConfirmCancelledCommand()
    {
        // Arrange
        string deviceId = "test-device-4";
        var command = new CommandItem(1, Guid.NewGuid(), new XDocument(new XElement("plist")).ToString());

        // Add command to queue
        await _commandQueueService.AddCommand(deviceId, command);

        // Get command to retrieve its score
        var retrievedCommand = await _commandQueueService.GetCommand(deviceId);
        Assert.NotNull(retrievedCommand);
        Assert.NotNull(retrievedCommand.ScoreId);

        // Act
        await _commandQueueService.ConfirmCommandProcessing(deviceId, retrievedCommand.ScoreId!.Value, true);

        // Assert - Command should be removed from queue
        var afterConfirmation = await _commandQueueService.GetCommand(deviceId);
        Assert.Null(afterConfirmation);
    }

    [Fact]
    public async Task GetCommandWhenQueueEmpty()
    {
        // Arrange
        string deviceId = "test-device-empty";

        // Act
        var retrievedCommand = await _commandQueueService.GetCommand(deviceId);

        // Assert
        Assert.Null(retrievedCommand);
    }
}