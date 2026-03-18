namespace ShuttleManager.Shared.Services.Enums;

public enum ShuttleConfigCommand
{
    /// <summary>
    /// Move distance forward (CMD_MOVE_DIST_F, with parameter)
    /// </summary>
    MoveDistanceForward,

    /// <summary>
    /// Move distance backward (CMD_MOVE_DIST_R, with parameter)
    /// </summary>
    MoveDistanceBackward,

    /// <summary>
    /// Reverse mode (CFG_REVERSE_MODE)
    /// </summary>
    ReverseMode,

    /// <summary>
    /// Max speed (CFG_MAX_SPEED)
    /// </summary>
    MaxSpeed,

    /// <summary>
    /// Min battery level (CFG_MIN_BATT)
    /// </summary>
    MinBattery,

    /// <summary>
    /// Inter-pallet distance (CFG_INTER_PALLET)
    /// </summary>
    InterPalletDistance,

    /// <summary>
    /// Channel edge offset (CFG_CHNL_OFFSET)
    /// </summary>
    DistOfEdge,

    /// <summary>
    /// Shuttle length (CFG_SHUTTLE_LEN)
    /// </summary>
    ShuttleLength,

    /// <summary>
    /// Shuttle number / ID (CFG_SHUTTLE_NUM)
    /// </summary>
    ShuttleNumber,

    /// <summary>
    /// Set date/time
    /// </summary>
    DT,
}