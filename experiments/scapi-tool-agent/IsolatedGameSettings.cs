internal static class IsolatedGameSettings
{
    public static void PrepareWindows(string root)
    {
        if (!OperatingSystem.IsWindows()) return;
        // 隔离游戏场使用本场设置，默认静音并关闭系统文件关联。
        var settings = Directory.CreateDirectory(Path.Combine(root, "doc"));
        using var stream = new FileStream(Path.Combine(settings.FullName, "Settings.xml"), FileMode.CreateNew);
        using var writer = new StreamWriter(stream);
        writer.Write("""
            <Settings>
              <Value Name="FileAssociationEnabled" Type="bool" Value="False" />
              <Value Name="SoundsVolume" Type="float" Value="0" />
              <Value Name="MusicVolume" Type="float" Value="0" />
            </Settings>
            """);
    }
}
