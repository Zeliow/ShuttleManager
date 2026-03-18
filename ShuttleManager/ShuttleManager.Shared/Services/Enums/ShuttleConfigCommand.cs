namespace ShuttleManager.Shared.Services.Enums
{
    public enum ShuttleConfigCommand
    {
        MoveDistanceForward,  // CMD_MOVE_DIST_F (с параметром)
        MoveDistanceBackward,  // CMD_MOVE_DIST_R (с параметром)
        ReverseMode,          // CFG_REVERSE_MODE
        MaxSpeed,             // CFG_MAX_SPEED
        MinBattery,           // CFG_MIN_BATT
        InterPalletDistance,  // CFG_INTER_PALLET
        DistOfEdge,        // CFG_CHNL_OFFSET
        ShuttleLength,        // CFG_SHUTTLE_LEN
        ShuttleNumber,         // CFG_SHUTTLE_NUM
        DT,                    // DataTime set
    }
}