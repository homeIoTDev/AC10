using System;

namespace HeatingDaemon;

public interface IHeatingService
{
    public bool RequestElsterValue(ushort senderCanId, ushort receiverCanId, ushort elster_idx, out ElsterValue? returnElsterValue);
    public void ProcessCanFrame(CanFrame frame);
    public void ScanElsterModules(ushort senderCanId = 0xFFF);
    public void PrintPassiveElsterTelegramList();
}
