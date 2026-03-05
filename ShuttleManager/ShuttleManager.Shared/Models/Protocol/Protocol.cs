using System.Runtime.InteropServices;

namespace ShuttleManager.Shared.Models.Protocol
{
    // --- Protocol Constants V2 ---
    public static class ProtocolConstants
    {
        public const byte PROTOCOL_SYNC_1_V2 = 0xBB;
        public const byte PROTOCOL_SYNC_2_V2 = 0xCC;
        public const byte PROTOCOL_VER = 2;
        
        public const byte TARGET_ID_NONE = 0x00;
        public const byte TARGET_ID_BROADCAST = 0xFF;
        
        public const byte MAX_LOG_STRING_LEN = 55;
        public const byte LOG_MAX_PRINTABLE_CHARS = MAX_LOG_STRING_LEN - 1;
    }

    // --- Message IDs ---
    public enum MsgID : byte
    {
        // Routine Telemetry (Push/Pull)
        MSG_HEARTBEAT = 0x01,
        MSG_SENSORS = 0x02,
        MSG_STATS = 0x03,
        MSG_REQ_HEARTBEAT = 0x04,
        MSG_REQ_SENSORS = 0x05,
        MSG_REQ_STATS = 0x06,
        
        // Asynchronous
        MSG_LOG = 0x10,
        
        // Configuration
        MSG_CONFIG_SET = 0x20,
        MSG_CONFIG_GET = 0x21,
        MSG_CONFIG_REP = 0x22,
        MSG_CONFIG_SYNC_REQ = 0x23,
        MSG_CONFIG_SYNC_PUSH = 0x24,
        MSG_CONFIG_SYNC_REP = 0x25,

        // Action Commands (Split for bandwidth efficiency)
        MSG_CMD_SIMPLE = 0x30,
        MSG_CMD_WITH_ARG = 0x31,
        MSG_SET_DATETIME = 0x32,
        MSG_ACK = 0x33
    }

    // --- Enums ---
    public enum LogLevel : byte
    {
        LOG_INFO = 0, LOG_WARN = 1, LOG_ERROR = 2, LOG_DEBUG = 3
    }

    public enum CmdType : byte
    {
        // -- 0x00 Block: Lifecycle & State --
        CMD_STOP = 0x00,
        CMD_STOP_MANUAL = 0x01,
        CMD_SYSTEM_RESET = 0x02,
        CMD_RESET_ERROR = 0x03,
        CMD_MANUAL_MODE = 0x04,
        CMD_LOG_MODE = 0x05,
        CMD_DEMO = 0x06,
        CMD_HOME = 0x07,

        // -- 0x10 Block: Core Movement --
        CMD_MOVE_RIGHT_MAN = 0x10,
        CMD_MOVE_LEFT_MAN = 0x11,
        CMD_MOVE_DIST_R = 0x12,
        CMD_MOVE_DIST_F = 0x13,
        CMD_LIFT_UP = 0x14,
        CMD_LIFT_DOWN = 0x15,
        CMD_CALIBRATE = 0x16,

        // -- 0x20 Block: Auto Operations --
        CMD_LOAD = 0x20,
        CMD_UNLOAD = 0x21,
        CMD_LONG_LOAD = 0x22,
        CMD_LONG_UNLOAD = 0x23,
        CMD_LONG_UNLOAD_QTY = 0x24,
        CMD_COMPACT_F = 0x25,
        CMD_COMPACT_R = 0x26,
        CMD_COUNT_PALLETS = 0x27,
        CMD_EVACUATE_ON = 0x28,

        // -- 0x30 Block: Configuration Updates --
        CMD_SAVE_EEPROM = 0x30,
        CMD_GET_CONFIG = 0x31,
        CMD_FIRMWARE_UPDATE = 0x32
    }

    public enum AckResult : byte
    {
        ACK_OK = 0,
        ACK_REJECTED = 1,
        ACK_BUSY = 2,
        ACK_BAD_ENVIRONMENT = 3,
        ACK_ERROR_STATE = 4
    }

    public enum ShuttleState : byte
    {
        STATE_IDLE = 0,
        STATE_MANUAL = 1,
        STATE_LOAD = 2,
        STATE_UNLOAD = 3,
        STATE_COMPACT = 4,
        STATE_EVACUATE = 5,
        STATE_DEMO = 6,
        STATE_COUNT_PALLETS = 7,
        STATE_ERROR = 8,
        STATE_WAITING = 9,
        STATE_LONG_LOAD = 10,
        STATE_LONG_UNLOAD = 11,
        STATE_LONG_UNLOAD_QTY = 12,
        STATE_MOVE_FWD = 13,
        STATE_MOVE_REV = 14,
        STATE_LIFT_UP = 15,
        STATE_LIFT_DOWN = 16,
        STATE_HOME = 17,
        STATE_CALIBRATE = 18
    }

    public enum ConfigParamID : byte
    {
        CFG_SHUTTLE_NUM = 1,   // "dNN"
        CFG_INTER_PALLET = 2,   // "dDm"
        CFG_SHUTTLE_LEN = 3,   // "dSl"
        CFG_MAX_SPEED = 4,   // "dSp"
        CFG_MIN_BATT = 5,   // "dBc"
        CFG_WAIT_TIME = 6,   // "dWt"
        CFG_MPR_OFFSET = 7,   // "dMo"
        CFG_CHNL_OFFSET = 8,   // "dMc"
        CFG_FIFO_LIFO = 9,   // "dFIFO_" / "dLIFO_"
        CFG_REVERSE_MODE = 10   // "dRevOn" / "dReOff"
    }

    // --- Structs ---
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FrameHeader
    {
        public byte Sync1;      // Always 0xBB (PROTOCOL_SYNC_1_V2)
        public byte Sync2;      // Always 0xCC (PROTOCOL_SYNC_2_V2)
        public byte MsgID;      // Identifies the Payload struct (MsgID enum)
        public byte TargetID;   // Routing identifier
        public byte Seq;        // Rolling sequence counter (0-255)
        public byte Length;     // Length of Payload ONLY (excludes header and CRC)
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetryPacket
    {
        public ushort ErrorCode;
        public ushort CurrentPosition;  // mm
        public ushort Speed;
        public ushort BatteryVoltage_mV;// 12500 = 12.5V
        public ushort StateFlags;       // Bit 0: lifterUp, 1: motorStart, 2: reverse, 3: inv, 4: inChnl, 5: fifoLifo
        public ShuttleState ShuttleStatus;
        public byte BatteryCharge;    // %
        public byte ShuttleNumber;
        public byte PalletCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SensorPacket
    {
        public ushort DistanceF;
        public ushort DistanceR;
        public ushort DistancePltF;
        public ushort DistancePltR;
        public ushort Angle;
        public short LifterCurrent;
        public short Temperature_dC;   // 255 = 25.5C
        public ushort HardwareFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StatsPacket
    {
        public uint TotalDist;
        public uint LoadCounter;
        public uint UnloadCounter;
        public uint CompactCounter;
        public uint LiftUpCounter;
        public uint LiftDownCounter;
        public uint LifetimePalletsDetected;
        public uint TotalUptimeMinutes;
        public ushort MotorStallCount;
        public ushort LifterOverloadCount;
        public ushort CrashCount;
        public ushort WatchdogResets;
        public ushort LowBatteryEvents;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FullConfigPacket
    {
        public ushort InterPallet;
        public ushort ShuttleLen;
        public ushort MaxSpeed;
        public ushort WaitTime;
        public short MprOffset;
        public short ChnlOffset;
        public byte ShuttleNumber;
        public byte MinBatt;
        public byte FifoLifo;
        public byte ReverseMode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ConfigPacket
    {
        public int Value;
        public byte ParamID;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SimpleCmdPacket
    {
        public byte CmdType;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ParamCmdPacket
    {
        public int Arg;
        public byte CmdType;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DateTimePacket
    {
        public byte Year;    // Offset from 2000
        public byte Month;
        public byte Day;
        public byte Hour;
        public byte Minute;
        public byte Second;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LogPacket
    {
        public byte LogLevel;
        // char message[MAX_LOG_STRING_LEN]; // Null terminated via vsnprintf
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AckPacket
    {
        public byte RefSeq;
        public AckResult Result;
    }
}