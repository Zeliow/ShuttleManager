using System.Runtime.InteropServices;

namespace ShuttleManager.Shared.Models.Protocol;

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

    /// <summary>
    /// Heartbeat
    /// </summary>
    MSG_HEARTBEAT = 0x01,

    /// <summary>
    /// Sensors data
    /// </summary>
    MSG_SENSORS = 0x02,

    /// <summary>
    /// Statistics
    /// </summary>
    MSG_STATS = 0x03,

    /// <summary>
    /// Request heartbeat
    /// </summary>
    MSG_REQ_HEARTBEAT = 0x04,

    /// <summary>
    /// Request sensors
    /// </summary>
    MSG_REQ_SENSORS = 0x05,

    /// <summary>
    /// Request stats
    /// </summary>
    MSG_REQ_STATS = 0x06,

    // Asynchronous

    /// <summary>
    /// Log message
    /// </summary>
    MSG_LOG = 0x10,

    // Configuration

    /// <summary>
    /// Set configuration
    /// </summary>
    MSG_CONFIG_SET = 0x20,

    /// <summary>
    /// Get configuration
    /// </summary>
    MSG_CONFIG_GET = 0x21,

    /// <summary>
    /// Configuration reply
    /// </summary>
    MSG_CONFIG_REP = 0x22,

    /// <summary>
    /// Config sync request
    /// </summary>
    MSG_CONFIG_SYNC_REQ = 0x23,

    /// <summary>
    /// Config sync push
    /// </summary>
    MSG_CONFIG_SYNC_PUSH = 0x24,

    /// <summary>
    /// Config sync reply
    /// </summary>
    MSG_CONFIG_SYNC_REP = 0x25,

    // Action Commands (Split for bandwidth efficiency)

    /// <summary>
    /// Simple command
    /// </summary>
    MSG_CMD_SIMPLE = 0x30,

    /// <summary>
    /// Command with argument
    /// </summary>
    MSG_CMD_WITH_ARG = 0x31,

    /// <summary>
    /// Set date/time
    /// </summary>
    MSG_SET_DATETIME = 0x32,

    /// <summary>
    /// Acknowledgment
    /// </summary>
    MSG_ACK = 0x33,
}

// --- Enums ---
public enum LogLevel : byte
{
    /// <summary>
    /// Information
    /// </summary>
    LOG_INFO = 0,

    /// <summary>
    /// Warning
    /// </summary>
    LOG_WARN = 1,

    /// <summary>
    /// Error
    /// </summary>
    LOG_ERROR = 2,

    /// <summary>
    /// Debug
    /// </summary>
    LOG_DEBUG = 3,
}

[Flags]
public enum ShuttleFault : ushort
{
    /// <summary>
    /// No fault
    /// </summary>
    FAULT_NONE = 0x0000,

    /// <summary>
    /// ToF channel front fault
    /// </summary>
    FAULT_TOF_CH_F = 1 << 1,

    /// <summary>
    /// ToF channel rear fault
    /// </summary>
    FAULT_TOF_CH_R = 1 << 2,

    /// <summary>
    /// ToF pallet front fault
    /// </summary>
    FAULT_TOF_PAL_F = 1 << 3,

    /// <summary>
    /// ToF pallet rear fault
    /// </summary>
    FAULT_TOF_PAL_R = 1 << 4,

    /// <summary>
    /// Lifter timeout
    /// </summary>
    FAULT_LIFTER_TIMEOUT = 1 << 9,

    /// <summary>
    /// Motor stall
    /// </summary>
    FAULT_MOTOR_STALL = 1 << 10,

    /// <summary>
    /// Low battery
    /// </summary>
    FAULT_LOW_BATTERY = 1 << 11,

    /// <summary>
    /// Crash bumper triggered
    /// </summary>
    FAULT_CRASH_BUMPER = 1 << 12,

    /// <summary>
    /// Move timeout
    /// </summary>
    FAULT_MOVE_TIMEOUT = 1 << 13,
}

public enum CmdType : byte
{
    // -- 0x00 Block: Lifecycle & State --

    /// <summary>
    /// Stop
    /// </summary>
    CMD_STOP = 0x00,

    /// <summary>
    /// Stop manual
    /// </summary>
    CMD_STOP_MANUAL = 0x01,

    /// <summary>
    /// System reset
    /// </summary>
    CMD_SYSTEM_RESET = 0x02,

    /// <summary>
    /// Reset error
    /// </summary>
    CMD_RESET_ERROR = 0x03,

    /// <summary>
    /// Manual mode
    /// </summary>
    CMD_MANUAL_MODE = 0x04,

    /// <summary>
    /// Log mode
    /// </summary>
    CMD_LOG_MODE = 0x05,

    /// <summary>
    /// Demo mode
    /// </summary>
    CMD_DEMO = 0x06,

    /// <summary>
    /// Home
    /// </summary>
    CMD_HOME = 0x07,

    // -- 0x10 Block: Core Movement --

    /// <summary>
    /// Move right manual
    /// </summary>
    CMD_MOVE_RIGHT_MAN = 0x10,

    /// <summary>
    /// Move left manual
    /// </summary>
    CMD_MOVE_LEFT_MAN = 0x11,

    /// <summary>
    /// Move distance right
    /// </summary>
    CMD_MOVE_DIST_R = 0x12,

    /// <summary>
    /// Move distance forward
    /// </summary>
    CMD_MOVE_DIST_F = 0x13,

    /// <summary>
    /// Lift up
    /// </summary>
    CMD_LIFT_UP = 0x14,

    /// <summary>
    /// Lift down
    /// </summary>
    CMD_LIFT_DOWN = 0x15,

    /// <summary>
    /// Calibrate
    /// </summary>
    CMD_CALIBRATE = 0x16,

    // -- 0x20 Block: Auto Operations --

    /// <summary>
    /// Load
    /// </summary>
    CMD_LOAD = 0x20,

    /// <summary>
    /// Unload
    /// </summary>
    CMD_UNLOAD = 0x21,

    /// <summary>
    /// Long load
    /// </summary>
    CMD_LONG_LOAD = 0x22,

    /// <summary>
    /// Long unload
    /// </summary>
    CMD_LONG_UNLOAD = 0x23,

    /// <summary>
    /// Long unload with quantity
    /// </summary>
    CMD_LONG_UNLOAD_QTY = 0x24,

    /// <summary>
    /// Compact forward
    /// </summary>
    CMD_COMPACT_F = 0x25,

    /// <summary>
    /// Compact reverse
    /// </summary>
    CMD_COMPACT_R = 0x26,

    /// <summary>
    /// Count pallets
    /// </summary>
    CMD_COUNT_PALLETS = 0x27,

    /// <summary>
    /// Evacuate on
    /// </summary>
    CMD_EVACUATE_ON = 0x28,

    // -- 0x30 Block: Configuration Updates --

    /// <summary>
    /// Save to EEPROM
    /// </summary>
    CMD_SAVE_EEPROM = 0x30,

    /// <summary>
    /// Get configuration
    /// </summary>
    CMD_GET_CONFIG = 0x31,

    /// <summary>
    /// Firmware update
    /// </summary>
    CMD_FIRMWARE_UPDATE = 0x32,
}

public enum AckResult : byte
{
    /// <summary>
    /// OK
    /// </summary>
    ACK_OK = 0,

    /// <summary>
    /// Rejected
    /// </summary>
    ACK_REJECTED = 1,

    /// <summary>
    /// Busy
    /// </summary>
    ACK_BUSY = 2,

    /// <summary>
    /// Bad environment
    /// </summary>
    ACK_BAD_ENVIRONMENT = 3,

    /// <summary>
    /// Error state
    /// </summary>
    ACK_ERROR_STATE = 4,
}

public enum ShuttleState : byte
{
    /// <summary>
    /// Idle
    /// </summary>
    STATE_IDLE = 0,

    /// <summary>
    /// Manual mode
    /// </summary>
    STATE_MANUAL = 1,

    /// <summary>
    /// Load pallet
    /// </summary>
    STATE_LOAD = 2,

    /// <summary>
    /// Unload pallet
    /// </summary>
    STATE_UNLOAD = 3,

    /// <summary>
    /// Compact pallets
    /// </summary>
    STATE_COMPACT = 4,

    /// <summary>
    /// Evacuation mode
    /// </summary>
    STATE_EVACUATE = 5,

    /// <summary>
    /// Demo mode
    /// </summary>
    STATE_DEMO = 6,

    /// <summary>
    /// Count pallets
    /// </summary>
    STATE_COUNT_PALLETS = 7,

    /// <summary>
    /// Error state
    /// </summary>
    STATE_ERROR = 8,

    /// <summary>
    /// Waiting
    /// </summary>
    STATE_WAITING = 9,

    /// <summary>
    /// Long load operation
    /// </summary>
    STATE_LONG_LOAD = 10,

    /// <summary>
    /// Long unload operation
    /// </summary>
    STATE_LONG_UNLOAD = 11,

    /// <summary>
    /// Long unload with quantity
    /// </summary>
    STATE_LONG_UNLOAD_QTY = 12,

    /// <summary>
    /// Move forward
    /// </summary>
    STATE_MOVE_FWD = 13,

    /// <summary>
    /// Move reverse
    /// </summary>
    STATE_MOVE_REV = 14,

    /// <summary>
    /// Lift up
    /// </summary>
    STATE_LIFT_UP = 15,

    /// <summary>
    /// Lift down
    /// </summary>
    STATE_LIFT_DOWN = 16,

    /// <summary>
    /// Homing
    /// </summary>
    STATE_HOME = 17,

    /// <summary>
    /// Calibration
    /// </summary>
    STATE_CALIBRATE = 18,
}

public enum ConfigParamID : byte
{
    /// <summary>
    /// dNN
    /// </summary>
    CFG_SHUTTLE_NUM = 1,

    /// <summary>
    /// dDm
    /// </summary>
    CFG_INTER_PALLET = 2,

    /// <summary>
    /// dSl
    /// </summary>
    CFG_SHUTTLE_LEN = 3,

    /// <summary>
    /// dSp
    /// </summary>
    CFG_MAX_SPEED = 4,

    /// <summary>
    /// dBc
    /// </summary>
    CFG_MIN_BATT = 5,

    /// <summary>
    /// dWt
    /// </summary>
    CFG_WAIT_TIME = 6,

    /// <summary>
    /// dMo
    /// </summary>
    CFG_MPR_OFFSET = 7,

    /// <summary>
    /// dMc
    /// </summary>
    CFG_CHNL_OFFSET = 8,

    /// <summary>
    /// dFIFO_ / dLIFO_
    /// </summary>
    CFG_FIFO_LIFO = 9,

    /// <summary>
    /// dRevOn / dReOff
    /// </summary>
    CFG_REVERSE_MODE = 10,
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
    public ushort WaringCode;
    public ushort CurrentPosition;  // mm
    public ushort Speed;
    public ushort BatteryVoltage_mV; // 12500 = 12.5V
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