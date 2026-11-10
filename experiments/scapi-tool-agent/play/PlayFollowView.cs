#nullable enable

using Engine;
using Engine.Input;
using Game;

internal sealed class PlayFollowView : GameWidget
{
    private readonly ComponentPlayer player;
    private readonly GamesWidget surface;
    private readonly LabelWidget status;
    private readonly Action<object> log;
    private double nextRefresh;

    public PlayFollowView(ComponentPlayer player, Action<object> log) : base(player.PlayerData, 0)
    {
        this.player = player;
        this.log = log;
        surface = player.PlayerData.SubsystemGameWidgets.GamesWidget;
        Target = player;
        IsHitTestVisible = false;
        ViewWidget.IsHitTestVisible = false;
        WidgetsHierarchyInput = new WidgetInput(WidgetInputDevice.None);
        ActiveCamera = FindCamera<TppCamera>();
        status = new LabelWidget
        {
            IsHitTestVisible = false,
            HorizontalAlignment = WidgetAlignment.Near,
            VerticalAlignment = WidgetAlignment.Near,
            Margin = new Vector2(18f, 14f),
            FontScale = 0.85f,
            DropShadow = true
        };
        GuiWidget.Children.Add(status);
        // 只挂到画面层，避免镜头位置进入生物生成或角色输入。
        surface.Children.Add(this);
        log(new { kind = "play_watch", visible = true, actor = player.PlayerData.Name });
    }

    public override void Update()
    {
        if (Keyboard.IsKeyDownOnce(Key.F8))
        {
            ViewWidget.IsVisible = !ViewWidget.IsVisible;
            nextRefresh = 0;
            log(new { kind = "play_watch", visible = ViewWidget.IsVisible, actor = player.PlayerData.Name });
        }
        if (ViewWidget.IsVisible) ActiveCamera.Update(Time.FrameDuration);
        if (Time.RealTime < nextRefresh) return;
        nextRefresh = Time.RealTime + 0.25;
        var vitals = player.ComponentVitalStats;
        var health = player.ComponentHealth;
        status.Text = ViewWidget.IsVisible
            ? $"{player.PlayerData.Name}  生存跟拍   F8 隐藏\n生命 {health.Health:P0}   饱食 {vitals.Food:P0}   精力 {vitals.Sleep:P0}   体温 {vitals.Temperature:F1}"
            : "跟拍画面已隐藏   F8 显示";
    }

    public void Detach()
    {
        if (ParentWidget == surface) surface.Children.Remove(this);
        Dispose();
    }
}
