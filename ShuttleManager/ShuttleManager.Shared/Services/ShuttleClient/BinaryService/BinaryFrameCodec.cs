using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Services.ShuttleClient.BinaryService;

/// <summary>Результат попытки разбора одного кадра из буфера.</summary>
public enum FrameParseResult
{
    /// <summary>Синхробайты не найдены — данных для разбора нет.</summary>
    NoSync,

    /// <summary>Найден заголовок кадра, но данных пока недостаточно.</summary>
    Incomplete,

    /// <summary>Кадр повреждён: некорректная длина payload или CRC. Пропускаем 2 байта для ресинхронизации.</summary>
    Invalid,

    /// <summary>Кадр разобран успешно.</summary>
    Ok,
}

/// <summary>Разобранный кадр протокола V2.</summary>
public readonly struct ParsedFrame
{
    public ParsedFrame(byte msgId, byte targetId, byte seq, ReadOnlyMemory<byte> payload, int nextOffset)
    {
        MsgId = msgId;
        TargetId = targetId;
        Seq = seq;
        Payload = payload;
        NextOffset = nextOffset;
    }

    public byte MsgId { get; }

    public byte TargetId { get; }

    public byte Seq { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Смещение в исходном буфере сразу после данного кадра.</summary>
    public int NextOffset { get; }
}

/// <summary>
/// Чистый кодек бинарного протокола V2: поиск синхропоследовательности,
/// разбор кадров с проверкой CRC16 CCITT и сборка исходящих кадров.
/// Не содержит состояния и зависимостей от транспорта — пригоден для unit-тестов.
/// </summary>
public static class BinaryFrameCodec
{
    public const int HeaderSize = 6;
    public const int CrcSize = 2;
    public const int MaxPayloadSize = 120;

    public static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }

        return crc;
    }

    /// <summary>Ищет синхропоследовательность 0xBB 0xCC начиная с <paramref name="startOffset"/>.</summary>
    public static bool TryFindSync(ReadOnlySpan<byte> data, int startOffset, out int syncIndex)
    {
        syncIndex = -1;
        for (int i = startOffset; i < data.Length - 1; i++)
        {
            if (data[i] == ProtocolConstants.PROTOCOL_SYNC_1_V2 && data[i + 1] == ProtocolConstants.PROTOCOL_SYNC_2_V2)
            {
                syncIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Пытается разобрать один кадр, начиная со смещения <paramref name="offset"/>.
    /// </summary>
    /// <param name="data">Входной буфер.</param>
    /// <param name="offset">Смещение, с которого начинать поиск кадра.</param>
    /// <param name="frame">Разобранный кадр (валиден только при результате <see cref="FrameParseResult.Ok"/>).</param>
    /// <param name="nextOffset">
    /// Смещение для следующего вызова: -1 при <see cref="FrameParseResult.NoSync"/>,
    /// иначе — позиция, с которой нужно продолжить разбор (или сохранить недокачанный остаток).
    /// </param>
    public static FrameParseResult TryParseFrame(
        ReadOnlySpan<byte> data,
        int offset,
        out ParsedFrame frame,
        out int nextOffset)
    {
        frame = default;
        nextOffset = -1;

        if (!TryFindSync(data, offset, out int syncIndex))
        {
            return FrameParseResult.NoSync;
        }

        if (data.Length - syncIndex < HeaderSize)
        {
            nextOffset = syncIndex;
            return FrameParseResult.Incomplete;
        }

        byte msgId = data[syncIndex + 2];
        byte targetId = data[syncIndex + 3];
        byte seq = data[syncIndex + 4];
        int payloadLength = data[syncIndex + 5];

        if (payloadLength > MaxPayloadSize)
        {
            nextOffset = syncIndex + 2;
            return FrameParseResult.Invalid;
        }

        int totalFrameSize = HeaderSize + payloadLength + CrcSize;
        if (data.Length - syncIndex < totalFrameSize)
        {
            nextOffset = syncIndex;
            return FrameParseResult.Incomplete;
        }

        ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(syncIndex + HeaderSize + payloadLength, CrcSize));
        ushort calculatedCrc = Crc16Ccitt(data.Slice(syncIndex, HeaderSize + payloadLength));

        if (receivedCrc != calculatedCrc)
        {
            nextOffset = syncIndex + 2;
            return FrameParseResult.Invalid;
        }

        frame = new ParsedFrame(
            msgId,
            targetId,
            seq,
            data.Slice(syncIndex + HeaderSize, payloadLength).ToArray(),
            syncIndex + totalFrameSize);
        nextOffset = syncIndex + totalFrameSize;
        return FrameParseResult.Ok;
    }

    /// <summary>Собирает исходящий кадр протокола V2: заголовок + payload + CRC16 CCITT.</summary>
    public static byte[] BuildFrame<TPayload>(MsgID msgId, byte targetId, byte seq, in TPayload payload)
        where TPayload : struct
    {
        int payloadSize = Marshal.SizeOf<TPayload>();
        int frameSize = HeaderSize + payloadSize + CrcSize;

        byte[] frame = new byte[frameSize];

        frame[0] = ProtocolConstants.PROTOCOL_SYNC_1_V2;
        frame[1] = ProtocolConstants.PROTOCOL_SYNC_2_V2;
        frame[2] = (byte)msgId;
        frame[3] = targetId;
        frame[4] = seq;
        frame[5] = (byte)payloadSize;

        MemoryMarshal.Write(frame.AsSpan(HeaderSize, payloadSize), in payload);

        ushort crc = Crc16Ccitt(frame.AsSpan(0, HeaderSize + payloadSize));
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(HeaderSize + payloadSize, CrcSize),
            crc);

        return frame;
    }
}
