namespace RedisCommandQueues;

public record CommandItem(int Id, Guid BatchId, string PlistData)
{
    public double? ScoreId { get; set; }
}