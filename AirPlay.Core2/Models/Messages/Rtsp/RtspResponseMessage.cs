namespace AirPlay.Core2.Models.Messages.Rtsp;

public partial class RtspResponseMessage : IDisposable
{
    private readonly MemoryStream _memoryStream = new();

    public StatusCode Status { get; set; } = StatusCode.OK;

    public RtspHeadersCollection Headers { get; set; } = [];

    public void Dispose() => _memoryStream.Dispose();

    public async Task WriteAsync(byte[] buffer, int index, int count, CancellationToken cancellationToken = default)
        => await _memoryStream.WriteAsync(buffer.AsMemory(index, count), cancellationToken);

    public Task<byte[]> ReadToEndAsync() => Task.FromResult(_memoryStream.ToArray());
}

partial class RtspResponseMessage 
{
    public enum StatusCode
    {
        OK = 200,
        NOCONTENT = 201,
        BADREQUEST = 400,
        UNAUTHORIZED = 401,
        FORBIDDEN = 403,
        INTERNALSERVERERROR = 500
    }
}