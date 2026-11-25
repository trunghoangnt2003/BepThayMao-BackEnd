using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Bepthaymao.Infrastructure.Messaging;

/// <summary>
/// Kết nối RabbitMQ dùng chung của tiến trình: mở lười, khai báo topology một lần, tự tạo lại khi kết nối đóng.
/// Channel luôn tạo riêng cho từng lần dùng với publisher confirm bật, nên mọi publish đều chờ broker xác nhận.
/// </summary>
public sealed class RabbitBroker(IConfiguration configuration) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IChannel> CreateConfirmChannelAsync(CancellationToken token)
    {
        IConnection connection = await ConnectAsync(token);
        return await connection.CreateChannelAsync(new(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), token);
    }

    private async Task<IConnection> ConnectAsync(CancellationToken token)
    {
        if (_connection is { IsOpen: true } open) return open;
        await _gate.WaitAsync(token);
        try
        {
            if (_connection is { IsOpen: true } current) return current;
            if (_connection is not null) await _connection.DisposeAsync();
            _connection = null;
            ConnectionFactory factory = new()
            {
                Uri = new(configuration.GetConnectionString("RabbitMq") ?? throw new InvalidOperationException("Thiếu ConnectionStrings:RabbitMq")),
                AutomaticRecoveryEnabled = true,
                ClientProvidedName = "bepthaymao-messaging"
            };
            IConnection connection = await factory.CreateConnectionAsync(token);
            try
            {
                await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: token);
                await MessagingTopology.DeclareAsync(channel, token);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
            _connection = connection;
            return connection;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
