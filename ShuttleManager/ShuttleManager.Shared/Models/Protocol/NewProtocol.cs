using System.Runtime.InteropServices;
using System.Buffers.Binary;

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
        MSG_ACK = 0x33  // Shuttle -> Pult/Display: Command acknowledgment
    }

    // --- Enums ---
    public enum LogLevel : byte
    {
        LOG_INFO = 0, 
        LOG_WARN = 1, 
        LOG_ERROR = 2, 
        LOG_DEBUG = 3
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
        CMD_MOVE_DIST_R = 0x12, // Requires MSG_CMD_WITH_ARG
        CMD_MOVE_DIST_F = 0x13, // Requires MSG_CMD_WITH_ARG
        CMD_LIFT_UP = 0x14,
        CMD_LIFT_DOWN = 0x15,
        CMD_CALIBRATE = 0x16,

        // -- 0x20 Block: Auto Operations --
        CMD_LOAD = 0x20,
        CMD_UNLOAD = 0x21,
        CMD_LONG_LOAD = 0x22,
        CMD_LONG_UNLOAD = 0x23,
        CMD_LONG_UNLOAD_QTY = 0x24, // Requires MSG_CMD_WITH_ARG
        CMD_COMPACT_F = 0x25,
        CMD_COMPACT_R = 0x26,
        CMD_COUNT_PALLETS = 0x27,
        CMD_EVACUATE_ON = 0x28,

        // -- 0x30 Block: Configuration Updates --
        CMD_SAVE_EEPROM = 0x30,
        CMD_GET_CONFIG = 0x31,
        CMD_FIRMWARE_UPDATE = 0x32
    }

    public enum ConfigParamID : byte
    {
        CFG_SHUTTLE_NUM = 1,
        CFG_INTER_PALLET = 2,
        CFG_SHUTTLE_LEN = 3,
        CFG_MAX_SPEED = 4,
        CFG_MIN_BATT = 5,
        CFG_WAIT_TIME = 6,
        CFG_MPR_OFFSET = 7,
        CFG_CHNL_OFFSET = 8,
        CFG_FIFO_LIFO = 9,
        CFG_REVERSE_MODE = 10
    }

    public enum AckResult : byte
    {
        ACK_OK = 0, // Command accepted and will execute
        ACK_REJECTED = 1, // Generic rejection (reserved)
        ACK_BUSY = 2, // Shuttle is already executing another operation
        ACK_BAD_ENVIRONMENT = 3, // Shuttle not in valid physical state (e.g., not in channel)
        ACK_ERROR_STATE = 4  // Shuttle is in error mode, only overrides accepted
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
        STATE_MOVE_FWD = 13,  // Move by distance forward
        STATE_MOVE_REV = 14,  // Move by distance reverse
        STATE_LIFT_UP = 15,
        STATE_LIFT_DOWN = 16,
        STATE_HOME = 17,
        STATE_CALIBRATE = 18
    }

    // --- Structs ---
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FrameHeader
    {
        public byte Sync1;      // Always 0xBB
        public byte Sync2;      // Always 0xCC
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
        public ShuttleState ShuttleStatus; // Current high-level operation
        public byte BatteryCharge;      // %
        public byte ShuttleNumber;    
        public byte PalleteCount;     
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
        public ushort HardwareFlags;    // Bitmask for discretes
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

    // Used with MSG_CONFIG_SET / MSG_CONFIG_GET / MSG_CONFIG_REP (5 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ConfigPacket
    {
        public int Value;             
        public byte ParamID;           
    }

    // Used with MSG_CONFIG_SYNC_REQ / MSG_CONFIG_SYNC_PUSH / MSG_CONFIG_SYNC_REP
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
    public struct LogPacket
    {
        public byte LogLevel;                  
        // char message[MAX_LOG_STRING_LEN];  // This will be handled separately in C#
    }

    // Used with MSG_ACK (2 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AckPacket
    {
        public byte RefSeq;  // Sequence number of the command being ACK'd
        public AckResult Result; // Reason code for the ACK response
    }

    // --- Utility Classes ---
    public static class ProtocolUtils
    {
        public static ushort UpdateCRC16(ushort crc, byte data)
        {
            crc ^= (ushort)(data << 8);
            for (byte j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0) 
                    crc = (ushort)((crc << 1) ^ 0x1021);
                else 
                    crc <<= 1;
            }
            return crc;
        }

        public static ushort CalcCRC16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc = UpdateCRC16(crc, data[offset + i]);
            }
            return crc;
        }

        public static ushort CalcCRC16(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
            {
                crc = UpdateCRC16(crc, b);
            }
            return crc;
        }

        public static void AppendCRC(Span<byte> buffer, int lengthWithoutCRC)
        {
            ushort crc = CalcCRC16(buffer.Slice(0, lengthWithoutCRC));
            buffer[lengthWithoutCRC] = (byte)(crc & 0xFF);         // LSB
            buffer[lengthWithoutCRC + 1] = (byte)((crc >> 8) & 0xFF);  // MSB
        }
    }

    public class ProtocolParser
    {
        public enum State
        {
            STATE_WAIT_SYNC1,
            STATE_WAIT_SYNC2,
            STATE_READ_HEADER,
            STATE_READ_PAYLOAD,
            STATE_READ_CRC
        }

        public bool CrcError { get; private set; }
        private State _state;
        private uint _lastRxTime = 0;
        private const uint RX_TIMEOUT_MS = 100;
        
        private byte[] _rxBuffer = new byte[64];
        private ushort _rxIndex;
        private byte _payloadLen;

        public ProtocolParser()
        {
            Reset();
        }

        public void Reset()
        {
            _state = State.STATE_WAIT_SYNC1;
            _rxIndex = 0;
            _payloadLen = 0;
            CrcError = false;
        }

        // Returns FrameHeader if a complete and valid frame was parsed, null otherwise
        public FrameHeader? Feed(byte data, uint currentMillis)
        {
            if (_state != State.STATE_WAIT_SYNC1 && (currentMillis - _lastRxTime > RX_TIMEOUT_MS))
            {
                Reset();
            }
            _lastRxTime = currentMillis;

            switch (_state)
            {
                case State.STATE_WAIT_SYNC1:
                    CrcError = false;
                    if (data == ProtocolConstants.PROTOCOL_SYNC_1_V2)
                    {
                        _rxBuffer[0] = data;
                        _state = State.STATE_WAIT_SYNC2;
                    }
                    break;

                case State.STATE_WAIT_SYNC2:
                    if (data == ProtocolConstants.PROTOCOL_SYNC_2_V2)
                    {
                        _rxBuffer[1] = data;
                        _rxIndex = 2;
                        _state = State.STATE_READ_HEADER;
                    }
                    else if (data == ProtocolConstants.PROTOCOL_SYNC_1_V2)
                    {
                        _state = State.STATE_WAIT_SYNC2;
                    }
                    else
                    {
                        _state = State.STATE_WAIT_SYNC1;
                    }
                    break;

                case State.STATE_READ_HEADER:
                    _rxBuffer[_rxIndex++] = data;
                    if (_rxIndex >= Marshal.SizeOf<FrameHeader>())
                    {
                        FrameHeader header = ReadFrameHeader(_rxBuffer, 0);
                        _payloadLen = header.Length;

                        if (_payloadLen > _rxBuffer.Length - Marshal.SizeOf<FrameHeader>() - 2)
                        {
                            _state = State.STATE_WAIT_SYNC1;
                            _rxIndex = 0;
                        }
                        else if (_payloadLen == 0)
                        {
                            _state = State.STATE_READ_CRC;
                        }
                        else
                        {
                            _state = State.STATE_READ_PAYLOAD;
                        }
                    }
                    break;

                case State.STATE_READ_PAYLOAD:
                    _rxBuffer[_rxIndex++] = data;
                    if (_rxIndex >= Marshal.SizeOf<FrameHeader>() + _payloadLen)
                    {
                        _state = State.STATE_READ_CRC;
                    }
                    break;

                case State.STATE_READ_CRC:
                    _rxBuffer[_rxIndex++] = data;
                    if (_rxIndex >= Marshal.SizeOf<FrameHeader>() + _payloadLen + 2)
                    {
                        int totalLen = Marshal.SizeOf<FrameHeader>() + _payloadLen;

                        ushort receivedCRC = (ushort)(_rxBuffer[totalLen] | (_rxBuffer[totalLen + 1] << 8));
                        ushort calculatedCRC = ProtocolUtils.CalcCRC16(_rxBuffer, 0, totalLen);

                        _state = State.STATE_WAIT_SYNC1;

                        if (receivedCRC == calculatedCRC)
                        {
                            return ReadFrameHeader(_rxBuffer, 0);
                        }
                        else
                        {
                            CrcError = true;
                            return null;
                        }
                    }
                    break;
            }
            return null;
        }

        private FrameHeader ReadFrameHeader(byte[] buffer, int offset)
        {
            FrameHeader header = new FrameHeader
            {
                Sync1 = buffer[offset],
                Sync2 = buffer[offset + 1],
                MsgID = buffer[offset + 2],
                TargetID = buffer[offset + 3],
                Seq = buffer[offset + 4],
                Length = buffer[offset + 5]
            };
            return header;
        }

        // Method to extract payload data from the buffer when a complete frame is received
        public byte[] GetPayload()
        {
            if (_state == State.STATE_WAIT_SYNC1 && _rxIndex > 0)
            {
                int payloadStart = Marshal.SizeOf<FrameHeader>();
                int payloadEnd = payloadStart + _payloadLen;
                byte[] payload = new byte[_payloadLen];
                Array.Copy(_rxBuffer, payloadStart, payload, 0, _payloadLen);
                return payload;
            }
            return new byte[0];
        }
    }
}