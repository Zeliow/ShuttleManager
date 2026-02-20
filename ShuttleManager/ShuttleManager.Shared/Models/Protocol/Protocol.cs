using System.Runtime.InteropServices;

namespace ShuttleManager.Shared.Models.Protocol
{
    // --- Message IDs ---
    public enum MsgID : byte
    {
        MSG_HEARTBEAT = 0x01, // High freq: Position, Speed, State
        MSG_SENSORS = 0x02, // Med freq: TOF, Encoders, Pallet sensors
        MSG_STATS = 0x03, // Low freq: Odometry, Cycles
        MSG_LOG = 0x10, // Async: Human readable strings with levels
        MSG_CONFIG_SET = 0x20, // Display -> Shuttle: Set EEPROM param
        MSG_CONFIG_GET = 0x21, // Display -> Shuttle: Request param
        MSG_CONFIG_REP = 0x22, // Shuttle -> Display: Reply with param
        MSG_COMMAND = 0x30, // Display -> Shuttle: Action command
        MSG_ACK = 0x31  // Shuttle -> Display: Command acknowledgment
    }

    // --- Enums ---
    public enum LogLevel : byte
    {
        LOG_INFO = 0, LOG_WARN = 1, LOG_ERROR = 2, LOG_DEBUG = 3
    }

    public enum CmdType : byte
    {
        CMD_STOP = 5,   // "dStop_"
        CMD_STOP_MANUAL = 55,  // "dStopM"
        CMD_MOVE_RIGHT_MAN = 1,   // "dRight"
        CMD_MOVE_LEFT_MAN = 2,   // "dLeft_"
        CMD_LIFT_UP = 3,   // "dUp___"
        CMD_LIFT_DOWN = 4,   // "dDown_"
        CMD_LOAD = 6,   // "dLoad_"
        CMD_UNLOAD = 7,   // "dUnld_"
        CMD_MOVE_DIST_R = 8,   // "dMr"
        CMD_MOVE_DIST_F = 9,   // "dMf"
        CMD_CALIBRATE = 10,  // "dClbr_"
        CMD_DEMO = 11,  // "dDemo_"
        CMD_COUNT_PALLETS = 12,  // "dGetQu"
        CMD_SAVE_EEPROM = 13,  // "dSaveC"
        CMD_COMPACT_F = 14,  // "dComFo"
        CMD_COMPACT_R = 15,  // "dComBa"
        CMD_GET_CONFIG = 16,  // "dSGet_" / "dSpGet"
        CMD_TEST_SENSORS = 17,  // "dDataP"
        CMD_ERROR_REQ = 19,  // "tError"
        CMD_EVACUATE_ON = 20,  // "dEvOn_"
        CMD_EVACUATE_OFF = 28,  // "dEvOff"
        CMD_LONG_LOAD = 21,  // "dLLoad"
        CMD_LONG_UNLOAD = 22,  // "dLUnld"
        CMD_LONG_UNLOAD_QTY = 23,  // "dQt"
        CMD_RESET_ERROR = 24,  // "dReset"
        CMD_MANUAL_MODE = 25,  // "dManua"
        CMD_LOG_MODE = 26,  // "dGetLg"
        CMD_HOME = 27,  // "dHome_"
        CMD_PING = 100, // "ngPing"
        CMD_FIRMWARE_UPDATE = 200, // "Firmware"
        CMD_SYSTEM_RESET = 201, // "Reboot__"
        CMD_SET_DATETIME = 202  // "DT"
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
        public byte Sync1;      // Always 0xAA
        public byte Sync2;      // Always 0x55
        public ushort Length;   // Length of Payload ONLY (excludes header and CRC)
        public byte Seq;        // Rolling sequence counter (0-255)
        public byte MsgID;      // Identifies the Payload struct
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetryPacket
    {
        public uint Timestamp;        // millis()
        public ushort ErrorCode;        // Replaces 16-byte errorStatus array
        public byte ShuttleStatus;    // Current status (0-27 mapping)
        public ushort CurrentPosition;  // mm
        public ushort Speed;            // Current speed %
        public byte BatteryCharge;    // %
        public float BatteryVoltage;   // Volts
        public ushort StateFlags;

        public uint ShuttleNumber;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SensorPacket
    {
        public ushort DistanceF;        // distance[1]
        public ushort DistanceR;        // distance[0]
        public ushort DistancePltF;     // distance[3]
        public ushort DistancePltR;     // distance[2]
        public ushort Angle;            // as5600.readAngle()
        public short LifterCurrent;    //
        public float Temperature;      // Chip temp
        public byte HardwareFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StatsPacket
    {
        public uint TotalDist;        //
        public uint LoadCounter;      //
        public uint UnloadCounter;    //
        public uint CompactCounter;   //
        public uint LiftUpCounter;    //
        public uint LiftDownCounter;  //
        public byte PalleteCount;     //
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LogPacket
    {
        public byte Level;             // LogLevel enum
        // char text[];            // Implicit payload data. Length = FrameHeader.length - 1
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ConfigPacket
    {
        public byte ParamID;           // ConfigParamID enum
        public int Value;             // Value to set / reported value
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandPacket
    {
        public byte CmdType;           // CmdType enum
        public int Arg1;              // Used for Distances (dMr, dMf), Qty (dQt)
        public int Arg2;              // Unused currently, reserved for future
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AckPacket
    {
        public byte RefSeq;            // Sequence number of the command being ACK'd
        public byte Result;            // 0 = Success/Accepted, 1 = Error, 2 = Busy
    }
}