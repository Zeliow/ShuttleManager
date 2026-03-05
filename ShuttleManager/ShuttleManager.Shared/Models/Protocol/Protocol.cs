using System.Runtime.InteropServices;

namespace ShuttleManager.Shared.Models.Protocol
{
    // --- Constants ---
    public static class ProtocolConstants
    {
        public const byte PROTOCOL_SYNC_1_V2 = 0xBB;
        public const byte PROTOCOL_SYNC_2_V2 = 0xCC;
        public const byte PROTOCOL_VER = 2;

        public const byte TARGET_ID_NONE = 0x00;       // Direct UART line
        public const byte TARGET_ID_BROADCAST = 0xFF;  // Global command

        public const byte MAX_LOG_STRING_LEN = 55;
        public const byte LOG_MAX_PRINTABLE_CHARS = MAX_LOG_STRING_LEN - 1;
    }

    // --- Message IDs ---
    public enum MsgID : byte
    {
        // Routine Telemetry (Push/Pull)
        MSG_HEARTBEAT = 0x01, // Pushed ONLY on request
        MSG_SENSORS = 0x02, // Pushed ONLY on request
        MSG_STATS = 0x03, // Pushed ONLY on request
        MSG_REQ_HEARTBEAT = 0x04, // Pult -> Shuttle: Request Heartbeat (Keep-Alive)
        MSG_REQ_SENSORS = 0x05, // Pult -> Shuttle: Request Sensors
        MSG_REQ_STATS = 0x06, // Pult -> Shuttle: Request Stats
        
        // Asynchronous
        MSG_LOG = 0x10, // Shuttle -> Display: Truncated vsnprintf string
        
        // Configuration
        MSG_CONFIG_SET = 0x20, // Pult/Display -> Shuttle: Set single EEPROM param
        MSG_CONFIG_GET = 0x21, // Pult/Display -> Shuttle: Request single param
        MSG_CONFIG_REP = 0x22, // Shuttle -> Pult/Display: Reply with single param
        MSG_CONFIG_SYNC_REQ = 0x23, // Pult/Display -> Shuttle: Request FullConfigPacket
        MSG_CONFIG_SYNC_PUSH = 0x24, // Pult/Display -> Shuttle: Send FullConfigPacket to save
        MSG_CONFIG_SYNC_REP = 0x25, // Shuttle -> Pult/Display: Reply with FullConfigPacket

        // Action Commands (Split for bandwidth efficiency)
        MSG_CMD_SIMPLE = 0x30, // Pult/Display -> Shuttle: 1-byte payload (No arguments)
        MSG_CMD_WITH_ARG = 0x31, // Pult/Display -> Shuttle: 5-byte payload (Cmd + int32_t arg)
        MSG_SET_DATETIME = 0x32, // Display -> Shuttle: RTC Sync (DateTimePacket)
        MSG_ACK = 0x33,  // Shuttle -> Pult/Display: Command acknowledgment

        // Old protocol compatibility
        MSG_COMMAND = 0x30  // Display -> Shuttle: Action command (Legacy)
    }

    // --- Enums ---
    public enum LogLevel : byte
    {
        LOG_INFO = 0, LOG_WARN = 1, LOG_ERROR = 2, LOG_DEBUG = 3
    }

    public enum CmdType : byte
    {
        CMD_STOP = 5,   // "dStop_"

        //CMD_STOP_MANUAL     = 55,  // "dStopM"
        //CMD_MOVE_RIGHT_MAN  = 1,   // "dRight"
        //CMD_MOVE_LEFT_MAN   = 2,   // "dLeft_"
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

        //CMD_TEST_SENSORS    = 17,  // "dDataP"
        //CMD_ERROR_REQ       = 19,  // "tError"
        //CMD_EVACUATE_ON     = 20,  // "dEvOn_"
        //CMD_EVACUATE_OFF    = 28,  // "dEvOff"
        CMD_LONG_LOAD = 21,  // "dLLoad"

        CMD_LONG_UNLOAD = 22,  // "dLUnld"
        CMD_LONG_UNLOAD_QTY = 23,  // "dQt"
        CMD_RESET_ERROR = 24,  // "dReset"

        //CMD_MANUAL_MODE     = 25,  // "dManua"
        //CMD_LOG_MODE        = 26,  // "dGetLg"
        CMD_HOME = 27,  // "dHome_"

        //CMD_PING            = 100, // "ngPing"
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

    // --- New Protocol Structs ---
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NewFrameHeader
    {
        public byte Sync1;      // Always 0xBB
        public byte Sync2;      // Always 0xCC
        public byte MsgID;      // Identifies the Payload struct (MsgID enum)
        public byte TargetID;   // Routing identifier
        public byte Seq;        // Rolling sequence counter (0-255)
        public byte Length;     // Length of Payload ONLY (excludes header and CRC)
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetryPacketNew
    {
        public ushort ErrorCode;        
        public ushort CurrentPosition;  // mm
        public ushort Speed;
        public ushort BatteryVoltage_mV;// 12500 = 12.5V
        public ushort StateFlags;       // Bit 0: lifterUp, 1: motorStart, 2: reverse, 3: inv, 4: inChnl, 5: fifoLifo
        public ShuttleState ShuttleStatus; // Current high-level operation
        public byte BatteryCharge;    // %
        public byte ShuttleNumber;    
        public byte PalleteCount;     
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SensorPacketNew
    {
        public ushort DistanceF;
        public ushort DistanceR;
        public ushort DistancePltF;
        public ushort DistancePltR;
        public ushort Angle;            
        public short LifterCurrent;
        public short Temperature_dC;   // 255 = 25.5C
        public ushort HardwareFlags;    // Bitmask for discretes
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StatsPacketNew
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

        public byte[] ToByteArray()
        {
            byte[] bytes = new byte[Marshal.SizeOf<FullConfigPacket>()];
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.StructureToPtr(this, ptr, false);
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static FullConfigPacket FromByteArray(byte[] bytes)
        {
            if (bytes.Length != Marshal.SizeOf<FullConfigPacket>())
                throw new ArgumentException("Invalid byte array length for FullConfigPacket");

            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                return (FullConfigPacket)Marshal.PtrToStructure(ptr, typeof(FullConfigPacket));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    // Used with MSG_CONFIG_SET / MSG_CONFIG_GET / MSG_CONFIG_REP (5 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ConfigPacketNew
    {
        public int Value;             
        public byte ParamID;           
    }

    // Used with MSG_CMD_SIMPLE (1 byte)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SimpleCmdPacket
    {
        public byte CmdType;           
    }

    // Used with MSG_CMD_WITH_ARG (5 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ParamCmdPacket
    {
        public int Arg;               
        public byte CmdType;           
    }

    // Used with MSG_SET_DATETIME (6 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DateTimePacket
    {
        public byte Year;              // Offset from 2000
        public byte Month;
        public byte Day;
        public byte Hour;
        public byte Minute;
        public byte Second;
    }

    // Used with MSG_LOG (Variable length up to MAX_LOG_STRING_LEN + 1)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LogPacketNew
    {
        public byte LogLevel;                  
        // char message[MAX_LOG_STRING_LEN];  // This will be handled separately in C#
    }

    // Used with MSG_ACK (2 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AckPacketNew
    {
        public byte RefSeq;  // Sequence number of the command being ACK'd
        public AckResult Result; // Reason code for the ACK response
    }
}