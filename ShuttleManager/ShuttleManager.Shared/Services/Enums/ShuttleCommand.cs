namespace ShuttleManager.Shared.Services.Enums
{
    public enum ShuttleCommand
    {
        Stop,
        Load,
        LongLoad,
        Unload,
        LongUnload,           // CMD_LONG_UNLOAD
        Demo,                 // CMD_DEMO
        Reset,                // CMD_RESET_ERROR
        SaveConfig,           // CMD_SAVE_EEPROM
        Calibrate,            // CMD_CALIBRATE
        Home,                 // CMD_HOME
        SealForward,          // CMD_COMPACT_F
        SealBackward,         // CMD_COMPACT_R
        LiftUp,               // CMD_LIFT_UP ?? UP
        LiftDown,             // CMD_LIFT_DOWN ?? DOWN
        SystemReset,          // CMD_SYSTEM_RESET ?? reboot?
        Left,                 //    Назад
        Right,                //    вперёд
        MoveDistanceForward,  // CMD_MOVE_DIST_F (с параметром)
        MoveDistanceBackward,  // CMD_MOVE_DIST_R (с параметром)
    }
}