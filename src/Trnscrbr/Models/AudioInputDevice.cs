namespace Trnscrbr.Models;

public sealed record AudioInputDevice(int DeviceNumber, string Name, bool IsDefault)
{
    public override string ToString() => Name;
}
