using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace ShuttleManager.Shared.Models.Protocol
{
    /// <summary>
    /// Provides methods for building protocol packets according to the new shuttle communication protocol
    /// </summary>
    public static class PacketBuilder
    {
        /// <summary>
        /// Creates a command packet with no arguments (MSG_CMD_SIMPLE)
        /// </summary>
        public static byte[] BuildSimpleCommand(CmdType cmdType, byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_CMD_SIMPLE,
                TargetID = targetId,
                Seq = seq,
                Length = 1 // Only cmdType byte
            };

            var packet = new SimpleCmdPacket { CmdType = (byte)cmdType };
            
            // Calculate total size: header + payload + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            int payloadSize = Marshal.SizeOf<SimpleCmdPacket>();
            byte[] buffer = new byte[headerSize + payloadSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Copy payload
            IntPtr payloadPtr = Marshal.AllocHGlobal(payloadSize);
            try
            {
                Marshal.StructureToPtr(packet, payloadPtr, false);
                Marshal.Copy(payloadPtr, buffer, headerSize, payloadSize);
            }
            finally
            {
                Marshal.FreeHGlobal(payloadPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize + payloadSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a command packet with argument (MSG_CMD_WITH_ARG)
        /// </summary>
        public static byte[] BuildCommandWithArg(CmdType cmdType, int arg, byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_CMD_WITH_ARG,
                TargetID = targetId,
                Seq = seq,
                Length = 5 // cmdType (1) + arg (4)
            };

            var packet = new ParamCmdPacket { CmdType = (byte)cmdType, Arg = arg };
            
            // Calculate total size: header + payload + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            int payloadSize = Marshal.SizeOf<ParamCmdPacket>();
            byte[] buffer = new byte[headerSize + payloadSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Copy payload
            IntPtr payloadPtr = Marshal.AllocHGlobal(payloadSize);
            try
            {
                Marshal.StructureToPtr(packet, payloadPtr, false);
                Marshal.Copy(payloadPtr, buffer, headerSize, payloadSize);
            }
            finally
            {
                Marshal.FreeHGlobal(payloadPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize + payloadSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a configuration set packet (MSG_CONFIG_SET)
        /// </summary>
        public static byte[] BuildConfigSet(ConfigParamID paramId, int value, byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_CONFIG_SET,
                TargetID = targetId,
                Seq = seq,
                Length = 5 // value (4) + paramID (1)
            };

            var packet = new ConfigPacket { ParamID = (byte)paramId, Value = value };
            
            // Calculate total size: header + payload + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            int payloadSize = Marshal.SizeOf<ConfigPacket>();
            byte[] buffer = new byte[headerSize + payloadSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Copy payload
            IntPtr payloadPtr = Marshal.AllocHGlobal(payloadSize);
            try
            {
                Marshal.StructureToPtr(packet, payloadPtr, false);
                Marshal.Copy(payloadPtr, buffer, headerSize, payloadSize);
            }
            finally
            {
                Marshal.FreeHGlobal(payloadPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize + payloadSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a configuration get packet (MSG_CONFIG_GET)
        /// </summary>
        public static byte[] BuildConfigGet(ConfigParamID paramId, byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_CONFIG_GET,
                TargetID = targetId,
                Seq = seq,
                Length = 1 // Only paramID
            };

            var packet = new ConfigPacket { ParamID = (byte)paramId, Value = 0 }; // Value ignored for GET
            
            // Calculate total size: header + payload + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            int payloadSize = 1; // Only paramID byte needed
            byte[] buffer = new byte[headerSize + payloadSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Copy payload (just the paramID)
            buffer[headerSize] = (byte)paramId;
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize + payloadSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a request telemetry packet (MSG_REQ_HEARTBEAT)
        /// </summary>
        public static byte[] BuildRequestTelemetry(byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_REQ_HEARTBEAT,
                TargetID = targetId,
                Seq = seq,
                Length = 0 // No payload
            };

            // Calculate total size: header + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            byte[] buffer = new byte[headerSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a request sensors packet (MSG_REQ_SENSORS)
        /// </summary>
        public static byte[] BuildRequestSensors(byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_REQ_SENSORS,
                TargetID = targetId,
                Seq = seq,
                Length = 0 // No payload
            };

            // Calculate total size: header + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            byte[] buffer = new byte[headerSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a request stats packet (MSG_REQ_STATS)
        /// </summary>
        public static byte[] BuildRequestStats(byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_REQ_STATS,
                TargetID = targetId,
                Seq = seq,
                Length = 0 // No payload
            };

            // Calculate total size: header + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            byte[] buffer = new byte[headerSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a full config sync request packet (MSG_CONFIG_SYNC_REQ)
        /// </summary>
        public static byte[] BuildConfigSyncRequest(byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_CONFIG_SYNC_REQ,
                TargetID = targetId,
                Seq = seq,
                Length = 0 // No payload
            };

            // Calculate total size: header + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            byte[] buffer = new byte[headerSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize);
            
            return buffer;
        }

        /// <summary>
        /// Creates a datetime sync packet (MSG_SET_DATETIME)
        /// </summary>
        public static byte[] BuildDateTimeSync(DateTime dateTime, byte targetId = ProtocolConstants.TARGET_ID_NONE, byte seq = 0)
        {
            var header = new FrameHeader
            {
                Sync1 = ProtocolConstants.PROTOCOL_SYNC_1_V2,
                Sync2 = ProtocolConstants.PROTOCOL_SYNC_2_V2,
                MsgID = (byte)MsgID.MSG_SET_DATETIME,
                TargetID = targetId,
                Seq = seq,
                Length = 6 // year, month, day, hour, minute, second
            };

            var packet = new DateTimePacket
            {
                Year = (byte)(dateTime.Year - 2000),
                Month = (byte)dateTime.Month,
                Day = (byte)dateTime.Day,
                Hour = (byte)dateTime.Hour,
                Minute = (byte)dateTime.Minute,
                Second = (byte)dateTime.Second
            };

            // Calculate total size: header + payload + CRC
            int headerSize = Marshal.SizeOf<FrameHeader>();
            int payloadSize = Marshal.SizeOf<DateTimePacket>();
            byte[] buffer = new byte[headerSize + payloadSize + 2]; // +2 for CRC
            
            // Copy header
            IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.StructureToPtr(header, headerPtr, false);
                Marshal.Copy(headerPtr, buffer, 0, headerSize);
            }
            finally
            {
                Marshal.FreeHGlobal(headerPtr);
            }
            
            // Copy payload
            IntPtr payloadPtr = Marshal.AllocHGlobal(payloadSize);
            try
            {
                Marshal.StructureToPtr(packet, payloadPtr, false);
                Marshal.Copy(payloadPtr, buffer, headerSize, payloadSize);
            }
            finally
            {
                Marshal.FreeHGlobal(payloadPtr);
            }
            
            // Calculate and append CRC
            ProtocolUtils.AppendCRC(buffer.AsSpan(), headerSize + payloadSize);
            
            return buffer;
        }
    }
}