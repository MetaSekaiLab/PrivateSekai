using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using MessagePack;
using PrivateSekai.Config;
using PrivateSekai.Protocol;

namespace PrivateSekai.Transport;

public static class PrskCrypto
{
    private const int StreamBufferSize = 64 * 1024;

    public static byte[] EncryptAesCbc(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = ServerConfig.AesKey;
        aes.IV  = ServerConfig.AesIv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] DecryptAesCbc(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = ServerConfig.AesKey;
        aes.IV  = ServerConfig.AesIv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>
    /// T → MessagePack bytes → AES-CBC encrypt
    /// </summary>
    public static byte[] PrskEnc<T>(T data) => EncryptAesCbc(DumpSerializer.Serialize(data));

    public static bool TryDecryptAesCbc(byte[] data, out byte[] decrypted)
    {
        try
        {
            decrypted = DecryptAesCbc(data);
            return true;
        }
        catch (CryptographicException)
        {
            decrypted = [];
            return false;
        }
    }

    /// <summary>
    /// AES-CBC decrypt → MessagePack bytes → T
    /// </summary>
    public static T PrskDec<T>(byte[] data) => DumpSerializer.Deserialize<T>(DecryptAesCbc(data));

    /// <summary>
    /// JSON stream → MessagePack bytes (Float32) → AES-CBC stream.
    /// 用于大型 SuiteMasterFile，避免同时持有 UTF-16 JSON、msgpack 和 encrypted 三份大对象。
    /// </summary>
    public static void PrskEncJsonStreamToStream(Stream jsonStream, Stream encryptedDestination)
    {
        if (!jsonStream.CanSeek)
            throw new ArgumentException("JSON stream must be seekable", nameof(jsonStream));

        var containerCounts = CountJsonContainers(jsonStream);
        jsonStream.Position = 0;

        using var aes = Aes.Create();
        aes.Key = ServerConfig.AesKey;
        aes.IV = ServerConfig.AesIv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(encryptedDestination, encryptor, CryptoStreamMode.Write, leaveOpen: true);
        using var bufferWriter = new StreamBufferWriter(cryptoStream, StreamBufferSize);
        var writer = new MessagePackWriter(bufferWriter);

        WriteJsonStreamAsMessagePack(jsonStream, containerCounts, ref writer);
        writer.Flush();
        bufferWriter.Flush();
        cryptoStream.FlushFinalBlock();
    }

    private static IReadOnlyList<int> CountJsonContainers(Stream jsonStream)
    {
        jsonStream.Position = 0;
        var containerCounts = new List<int>();
        var stack = new Stack<JsonContainerCounter>();
        var state = new JsonReaderState();
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        var bytesInBuffer = 0;

        try
        {
            while (true)
            {
                if (bytesInBuffer == buffer.Length)
                    GrowBuffer(ref buffer, bytesInBuffer);

                var bytesRead = jsonStream.Read(buffer.AsSpan(bytesInBuffer));
                var isFinalBlock = bytesRead == 0;
                bytesInBuffer += bytesRead;

                var reader = new Utf8JsonReader(buffer.AsSpan(0, bytesInBuffer), isFinalBlock, state);
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                        case JsonTokenType.StartArray:
                        {
                            IncrementArrayValue(stack);
                            var id = containerCounts.Count;
                            containerCounts.Add(0);
                            stack.Push(new JsonContainerCounter(reader.TokenType, id));
                            break;
                        }
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                        {
                            var current = stack.Pop();
                            containerCounts[current.Id] = current.Count;
                            break;
                        }
                        case JsonTokenType.PropertyName:
                            IncrementObjectProperty(stack);
                            break;
                        case JsonTokenType.String:
                        case JsonTokenType.Number:
                        case JsonTokenType.True:
                        case JsonTokenType.False:
                        case JsonTokenType.Null:
                            IncrementArrayValue(stack);
                            break;
                    }
                }

                var consumed = (int)reader.BytesConsumed;
                state = reader.CurrentState;
                bytesInBuffer -= consumed;
                if (bytesInBuffer > 0)
                    buffer.AsSpan(consumed, bytesInBuffer).CopyTo(buffer);

                if (isFinalBlock)
                    break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (stack.Count != 0)
            throw new JsonException("Unexpected end of JSON while counting containers");

        return containerCounts;
    }

    private static void WriteJsonStreamAsMessagePack(
        Stream jsonStream,
        IReadOnlyList<int> containerCounts,
        ref MessagePackWriter writer)
    {
        var state = new JsonReaderState();
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        var bytesInBuffer = 0;
        var containerIndex = 0;

        try
        {
            while (true)
            {
                if (bytesInBuffer == buffer.Length)
                    GrowBuffer(ref buffer, bytesInBuffer);

                var bytesRead = jsonStream.Read(buffer.AsSpan(bytesInBuffer));
                var isFinalBlock = bytesRead == 0;
                bytesInBuffer += bytesRead;

                var reader = new Utf8JsonReader(buffer.AsSpan(0, bytesInBuffer), isFinalBlock, state);
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            writer.WriteMapHeader(containerCounts[containerIndex++]);
                            break;
                        case JsonTokenType.StartArray:
                            writer.WriteArrayHeader(containerCounts[containerIndex++]);
                            break;
                        case JsonTokenType.PropertyName:
                        case JsonTokenType.String:
                            writer.Write(reader.GetString());
                            break;
                        case JsonTokenType.Number:
                            if (reader.TryGetInt64(out var longVal))
                                writer.Write(longVal);
                            else
                                writer.Write((float)reader.GetDouble());
                            break;
                        case JsonTokenType.True:
                            writer.Write(true);
                            break;
                        case JsonTokenType.False:
                            writer.Write(false);
                            break;
                        case JsonTokenType.Null:
                            writer.WriteNil();
                            break;
                    }
                }

                var consumed = (int)reader.BytesConsumed;
                state = reader.CurrentState;
                bytesInBuffer -= consumed;
                if (bytesInBuffer > 0)
                    buffer.AsSpan(consumed, bytesInBuffer).CopyTo(buffer);

                if (isFinalBlock)
                    break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (containerIndex != containerCounts.Count)
            throw new JsonException("JSON container count mismatch while writing MessagePack");
    }

    private static void IncrementArrayValue(Stack<JsonContainerCounter> stack)
    {
        if (stack.Count == 0 || stack.Peek().TokenType != JsonTokenType.StartArray)
            return;

        var current = stack.Pop();
        current.Count++;
        stack.Push(current);
    }

    private static void IncrementObjectProperty(Stack<JsonContainerCounter> stack)
    {
        if (stack.Count == 0 || stack.Peek().TokenType != JsonTokenType.StartObject)
            throw new JsonException("Property name found outside JSON object");

        var current = stack.Pop();
        current.Count++;
        stack.Push(current);
    }

    private static void GrowBuffer(ref byte[] buffer, int bytesInBuffer)
    {
        var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
        buffer.AsSpan(0, bytesInBuffer).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = newBuffer;
    }

    private struct JsonContainerCounter
    {
        public JsonContainerCounter(JsonTokenType tokenType, int id)
        {
            TokenType = tokenType;
            Id = id;
            Count = 0;
        }

        public JsonTokenType TokenType { get; }
        public int Id { get; }
        public int Count { get; set; }
    }

    private sealed class StreamBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly Stream _stream;
        private byte[] _buffer;
        private int _index;

        public StreamBufferWriter(Stream stream, int bufferSize)
        {
            _stream = stream;
            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        }

        public void Advance(int count)
        {
            if (count < 0 || _index + count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            _index += count;
            if (_index == _buffer.Length)
                Flush();
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_index);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_index);
        }

        public void Flush()
        {
            if (_index == 0)
                return;

            _stream.Write(_buffer, 0, _index);
            _index = 0;
        }

        public void Dispose()
        {
            Flush();
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint <= 0)
                sizeHint = 1;

            if (_buffer.Length - _index >= sizeHint)
                return;

            Flush();
            if (_buffer.Length >= sizeHint)
                return;

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
        }
    }
}
