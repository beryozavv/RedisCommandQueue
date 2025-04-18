namespace RedisCommandQueues;

public record CommandEntry
{
    public int Id { get; init; }
    public int BatchId { get; init; }
    public double? ScoreId { get; set; }
    public string Data { get; init; }
}