using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BKE_MediaTools.Licensing
{
    internal sealed class EnterpriseSessionClient
    {
        private const string Schema = "bke.module-ipc.v1";
        private const int MaxMessageBytes = 16 * 1024;
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

        public async Task<bool> TryRedeemAsync(CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    GetPipeName(),
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectTimeout);
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

                var requestId = Guid.NewGuid().ToString("N");
                var payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema = Schema,
                    operation = "redeem",
                    request_id = requestId,
                });

                if (payload.Length > MaxMessageBytes)
                {
                    return false;
                }

                var header = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
                await pipe.WriteAsync(header, timeout.Token).ConfigureAwait(false);
                await pipe.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);

                await ReadExactlyAsync(pipe, header, timeout.Token).ConfigureAwait(false);
                var responseLength = BinaryPrimitives.ReadInt32BigEndian(header);
                if (responseLength < 2 || responseLength > MaxMessageBytes)
                {
                    return false;
                }

                var response = new byte[responseLength];
                await ReadExactlyAsync(pipe, response, timeout.Token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(response);
                var root = json.RootElement;

                if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != Schema ||
                    !root.TryGetProperty("request_id", out var echoed) || echoed.GetString() != requestId ||
                    !root.TryGetProperty("ok", out var ok) || !ok.GetBoolean() ||
                    !root.TryGetProperty("result", out var result) ||
                    !result.TryGetProperty("enterprise", out var enterprise))
                {
                    return false;
                }

                return enterprise.GetBoolean();
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        internal static string GetPipeName()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Windows user SID unavailable.");
            var digest = SHA256.HashData(Encoding.ASCII.GetBytes(sid));
            var suffix = Convert.ToHexString(digest).ToLowerInvariant()[..16];
            return $"bke-licensing-agent-{suffix}-module-v1";
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
        }
    }
}
