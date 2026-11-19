using System.Buffers.Binary;

namespace SonoHUD.Core;

public readonly record struct PcmFormat(int SampleRate, int Channels, int Bits, bool FloatingPoint, int BlockAlign)
{
    public int BytesPerSample => Bits / 8;
    public void Validate()
    {
        if (SampleRate is < 8000 or > 384000 || Channels is < 1 or > 32 ||
            (FloatingPoint ? Bits is not (32 or 64) : Bits is not (8 or 16 or 24 or 32)) || BlockAlign < Channels * BytesPerSample || BlockAlign > 4096)
            throw new NotSupportedException("지원하지 않는 출력 PCM 형식입니다.");
    }
}

public static class PcmDecoder
{
    public static int Decode(ReadOnlySpan<byte> bytes, Span<float> output, PcmFormat format, bool silent = false)
    {
        format.Validate(); int frames = Math.Min(bytes.Length / format.BlockAlign, output.Length / format.Channels);
        if (silent) { output[..(frames * format.Channels)].Clear(); return frames; }
        for (int frame = 0; frame < frames; frame++)
        for (int channel = 0; channel < format.Channels; channel++)
        {
            var sample = bytes.Slice(frame * format.BlockAlign + channel * format.BytesPerSample, format.BytesPerSample);
            double value;
            if (format.FloatingPoint)
                value = format.Bits == 32 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample)) : BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(sample));
            else value = format.Bits switch
            {
                8 => (sample[0] - 128) / 128.0,
                16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                24 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608.0,
                32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0,
                _ => 0
            };
            output[frame * format.Channels + channel] = double.IsFinite(value) ? (float)Math.Clamp(value, -1, 1) : 0;
        }
        return frames;
    }
}
