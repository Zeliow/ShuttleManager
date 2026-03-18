namespace ShuttleManager.Shared.Services.Enums;

public enum ShuttleCommand
{
    /// <summary>
    /// Stop
    /// </summary>
    Stop,

    /// <summary>
    /// Load
    /// </summary>
    Load,

    /// <summary>
    /// Long load
    /// </summary>
    LongLoad,

    /// <summary>
    /// Unload
    /// </summary>
    Unload,

    /// <summary>
    /// Long unload (CMD_LONG_UNLOAD)
    /// </summary>
    LongUnload,

    /// <summary>
    /// Demo mode (CMD_DEMO)
    /// </summary>
    Demo,

    /// <summary>
    /// Reset error (CMD_RESET_ERROR)
    /// </summary>
    Reset,

    /// <summary>
    /// Save config to EEPROM (CMD_SAVE_EEPROM)
    /// </summary>
    SaveConfig,

    /// <summary>
    /// Calibrate (CMD_CALIBRATE)
    /// </summary>
    Calibrate,

    /// <summary>
    /// Home (CMD_HOME)
    /// </summary>
    Home,

    /// <summary>
    /// Compact forward (CMD_COMPACT_F)
    /// </summary>
    SealForward,

    /// <summary>
    /// Compact reverse (CMD_COMPACT_R)
    /// </summary>
    SealBackward,

    /// <summary>
    /// Lift up (CMD_LIFT_UP)
    /// </summary>
    LiftUp,

    /// <summary>
    /// Lift down (CMD_LIFT_DOWN)
    /// </summary>
    LiftDown,

    /// <summary>
    /// System reset / reboot (CMD_SYSTEM_RESET)
    /// </summary>
    SystemReset,

    /// <summary>
    /// Move backward / left
    /// </summary>
    Left,

    /// <summary>
    /// Move forward / right
    /// </summary>
    Right,

    /// <summary>
    /// Move distance forward (CMD_MOVE_DIST_F, with parameter)
    /// </summary>
    MoveDistanceForward,

    /// <summary>
    /// Move distance backward (CMD_MOVE_DIST_R, with parameter)
    /// </summary>
    MoveDistanceBackward,

    /// <summary>
    /// Legacy firmware updater (FRM)
    /// </summary>
    FRM,

    /// <summary>
    /// Legacy firmware updater (UPD)
    /// </summary>
    UPD,

    /// <summary>
    /// Legacy manual command passthrough
    /// </summary>
    ManualCommand,
}