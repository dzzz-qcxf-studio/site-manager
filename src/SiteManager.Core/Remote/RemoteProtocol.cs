using System.Text;
using System.Text.Json;

namespace SiteManager.Core.Remote;

public static class RemoteProtocol
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static T Parse<T>(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        RemoteEnvelope<T> envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RemoteEnvelope<T>>(response, SerializerOptions)
                ?? throw new RemoteProtocolException("Remote response was empty.");
        }
        catch (JsonException exception)
        {
            throw new RemoteProtocolException("Remote response was not valid JSON.", exception);
        }

        if (envelope.ProtocolVersion != Version)
        {
            throw new RemoteProtocolException($"Unsupported remote protocol version: {envelope.ProtocolVersion}.");
        }

        if (envelope.RequestId == Guid.Empty)
        {
            throw new RemoteProtocolException("Remote response did not contain a valid request ID.");
        }

        if (!envelope.Ok)
        {
            var error = envelope.Error
                ?? throw new RemoteProtocolException("Remote response failed without an error payload.");
            if (string.IsNullOrWhiteSpace(error.Code) || string.IsNullOrWhiteSpace(error.Message))
            {
                throw new RemoteProtocolException("Remote error payload was invalid.");
            }

            throw new RemoteCommandException(error.Code, error.Message, error.Retryable, envelope.RequestId);
        }

        if (envelope.Data is null)
        {
            throw new RemoteProtocolException("Remote response succeeded without a data payload.");
        }

        return envelope.Data;
    }

    public static string EncodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Convert.ToBase64String(StrictUtf8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string DecodeText(string encodedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedText);

        var base64 = encodedText
            .Replace('-', '+')
            .Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return StrictUtf8.GetString(Convert.FromBase64String(base64));
    }
}

public sealed class RemoteProtocolException : IOException
{
    public RemoteProtocolException(string message)
        : base(message)
    {
    }

    public RemoteProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RemoteCommandException : Exception
{
    public RemoteCommandException(string code, string message, bool retryable, Guid requestId)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
        RequestId = requestId;
    }

    public string Code { get; }

    public bool Retryable { get; }

    public Guid RequestId { get; }
}
