using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CorePoints.Caching.Infrastructure;

/// <summary>
/// Manages the Redis ConnectionMultiplexer lifecycle as a singleton.
/// Reads the endpoint from AWS SSM Parameter Store at initialization.
/// </summary>
public sealed class RedisConnectionManager : IDisposable
{
    private readonly ILogger<RedisConnectionManager> _logger;
    private readonly CacheOptions _options;
    private readonly Lazy<Task<IConnectionMultiplexer>> _connectionLazy;
    private bool _disposed;

    public RedisConnectionManager(
        IOptions<CacheOptions> options,
        ILogger<RedisConnectionManager> logger,
        IAmazonSimpleSystemsManagement? ssmClient = null)
    {
        _logger = logger;
        _options = options.Value;
        _connectionLazy = new Lazy<Task<IConnectionMultiplexer>>(() => CreateConnectionAsync(ssmClient));
    }

    /// <summary>
    /// Gets the shared ConnectionMultiplexer instance.
    /// </summary>
    public async Task<IConnectionMultiplexer> GetConnectionAsync()
    {
        return await _connectionLazy.Value;
    }

    private async Task<IConnectionMultiplexer> CreateConnectionAsync(IAmazonSimpleSystemsManagement? ssmClient)
    {
        var endpoint = await ResolveEndpointAsync(ssmClient);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Redis endpoint is empty. Connection will fail and circuit breaker will handle degradation.");
            endpoint = "localhost:6379";
        }

        var configOptions = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = _options.ConnectTimeoutMs,
            SyncTimeout = _options.SyncTimeoutMs,
            EndPoints = { endpoint }
        };

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(configOptions);

        multiplexer.ConnectionFailed += (sender, args) =>
        {
            _logger.LogError(
                "Redis connection failed. Endpoint: {Endpoint}, FailureType: {FailureType}, Exception: {Exception}",
                args.EndPoint,
                args.FailureType,
                args.Exception?.Message);
        };

        multiplexer.ConnectionRestored += (sender, args) =>
        {
            _logger.LogInformation(
                "Redis connection restored. Endpoint: {Endpoint}, FailureType: {FailureType}",
                args.EndPoint,
                args.FailureType);
        };

        _logger.LogInformation("Redis ConnectionMultiplexer created for endpoint: {Endpoint}", endpoint);
        return multiplexer;
    }

    private async Task<string> ResolveEndpointAsync(IAmazonSimpleSystemsManagement? ssmClient)
    {
        // First try SSM if client is available
        if (ssmClient is not null)
        {
            try
            {
                var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development";
                var parameterName = $"/{environment}/redis/primary_endpoint";

                var response = await ssmClient.GetParameterAsync(new GetParameterRequest
                {
                    Name = parameterName,
                    WithDecryption = true
                });

                if (!string.IsNullOrWhiteSpace(response.Parameter?.Value))
                {
                    _logger.LogInformation("Redis endpoint resolved from SSM parameter: {ParameterName}", parameterName);
                    return response.Parameter.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read Redis endpoint from SSM. Falling back to configuration.");
            }
        }

        // Fall back to configuration
        return _options.RedisEndpoint;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connectionLazy.IsValueCreated && _connectionLazy.Value.IsCompleted)
        {
            _connectionLazy.Value.Result.Dispose();
        }
    }
}
