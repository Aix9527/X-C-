using System.Globalization;
using System.Windows.Data;
using XDiskInspector.Core;

namespace XDiskInspector.App;

public sealed class RecoveryTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is CleanupKind kind
            ? kind switch
            {
                CleanupKind.File => "直接删除，不进入回收站",
                CleanupKind.RecycleBin => "清空后无法从回收站恢复",
                _ => "不执行直接删除"
            }
            : "未知";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
