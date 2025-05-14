namespace RedisCommandQueues;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConfigurationDeliveryInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICommandQueueService, RedisCommandQueueService>();

        return services;
    }
}