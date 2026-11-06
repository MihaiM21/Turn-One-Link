using System.Buffers.Binary;
using Turn_One_Link.Services.F1;
using static Turn_One_Link.Services.F1.F1Packets;

namespace Turn_One_Link.Tests.F1;

/// <summary>
/// Builds spec-shaped F1 25 packets field by field at the documented byte offsets. Not captured
/// from the game — but the parser reads by the same offsets, so a mismatch here means a mismatch
/// against the spec table we derived them from, which is exactly what the tests should catch.
/// </summary>
public static class F1PacketBuilder
{
    public static byte[] Packet(PacketId id, ulong sessionUid = 42, uint overallFrame = 1000, byte playerCar = 3, ushort format = Format2025)
    {
        var size = ExpectedSize(id);
        var p = new byte[size];
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), format);
        p[2] = 25; p[3] = 1; p[4] = 0; p[5] = 1;
        p[6] = (byte)id;
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(7), sessionUid);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(15), 123.4f);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(19), overallFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(23), overallFrame);
        p[27] = playerCar;
        p[28] = 255;
        return p;
    }

    public static byte[] Session(sbyte trackId = 10, ushort trackLengthM = 7004, byte sessionType = 1, ulong uid = 42)
    {
        var p = Packet(PacketId.Session, uid);
        var o = HeaderSize;
        p[o + 0] = 1; p[o + 1] = 30; p[o + 2] = 25; p[o + 3] = 44;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 4), trackLengthM);
        p[o + 6] = sessionType; p[o + 7] = (byte)trackId; p[o + 8] = 0;
        return p;
    }

    public static byte[] LapData(byte car, byte lapNum, float lapDistance, uint currentLapMs, byte sector = 0, byte invalid = 0, byte pit = 0, uint lastLapMs = 0, ulong uid = 42, uint frame = 1000)
    {
        var p = Packet(PacketId.LapData, uid, frame, car);
        var o = HeaderSize + car * LapDataSize;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o + 0), lastLapMs);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o + 4), currentLapMs);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 8), 31_500); p[o + 10] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 11), 2_100); p[o + 13] = 1;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 20), lapDistance);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 24), lapDistance + 7004f * (lapNum - 1));
        p[o + 32] = 5; p[o + 33] = lapNum; p[o + 34] = pit; p[o + 35] = 0; p[o + 36] = sector; p[o + 37] = invalid; p[o + 38] = 0;
        p[o + 44] = 1; p[o + 45] = 2;
        return p;
    }

    public static byte[] CarTelemetry(byte car, ushort speed = 250, float throttle = 1f, float brake = 0f, float steer = 0.1f, sbyte gear = 7, ushort rpm = 11_000, byte drs = 1, ulong uid = 42, uint frame = 1000)
    {
        var p = Packet(PacketId.CarTelemetry, uid, frame, car);
        var o = HeaderSize + car * CarTelemetrySize;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 0), speed);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 2), throttle);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 6), steer);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 10), brake);
        p[o + 14] = 20; p[o + 15] = (byte)gear;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 16), rpm);
        p[o + 18] = drs;
        // brake temps RL RR FL FR
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 22), 400);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 24), 410);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 26), 520);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 28), 530);
        // inner temps RL RR FL FR
        p[o + 34] = 90; p[o + 35] = 91; p[o + 36] = 100; p[o + 37] = 101;
        // pressures RL RR FL FR
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 40), 21.5f);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 44), 21.6f);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 48), 23.5f);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 52), 23.6f);
        return p;
    }

    public static byte[] Motion(byte car, float x = 100f, float y = 5f, float z = -200f, float gLat = 2.5f, float gLong = -1.2f, float yaw = 0.7f, ulong uid = 42)
    {
        var p = Packet(PacketId.Motion, uid, 1000, car);
        var o = HeaderSize + car * CarMotionSize;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 0), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 8), z);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 36), gLat);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 40), gLong);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 48), yaw);
        return p;
    }

    public static byte[] CarStatus(byte car, float fuel = 55.5f, byte compound = 16, float ers = 3_500_000f, byte ersMode = 2, ulong uid = 42)
    {
        var p = Packet(PacketId.CarStatus, uid, 1000, car);
        var o = HeaderSize + car * CarStatusSize;
        p[o + 0] = 1; p[o + 1] = 1; p[o + 3] = 56;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 5), fuel);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 9), 110f);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 17), 13_000);
        p[o + 25] = compound; p[o + 26] = 17; p[o + 27] = 4;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 37), ers);
        p[o + 41] = ersMode;
        return p;
    }

    public static byte[] SessionHistory(byte car, int numLaps, (uint lapMs, int s1, int s2, int s3, bool valid)[] laps, ulong uid = 42)
    {
        var p = Packet(PacketId.SessionHistory, uid, 1000, car);
        p[HeaderSize] = car;
        p[HeaderSize + 1] = (byte)numLaps;
        for (var i = 0; i < laps.Length; i++)
        {
            var o = HeaderSize + 7 + i * LapHistorySize;
            var (lapMs, s1, s2, s3, valid) = laps[i];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o), lapMs);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 4), (ushort)(s1 % 60_000)); p[o + 6] = (byte)(s1 / 60_000);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 7), (ushort)(s2 % 60_000)); p[o + 9] = (byte)(s2 / 60_000);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + 10), (ushort)(s3 % 60_000)); p[o + 12] = (byte)(s3 / 60_000);
            p[o + 13] = (byte)(valid ? 0x0F : 0x0E);
        }
        return p;
    }

    public static byte[] Event(string code, ulong uid = 42)
    {
        var p = Packet(PacketId.Event, uid);
        System.Text.Encoding.ASCII.GetBytes(code).CopyTo(p, HeaderSize);
        return p;
    }
}
