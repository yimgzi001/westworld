#nullable enable

using Game;

internal static class PlaySurvivalKnowledge
{
    public static object[] ForSituation(ComponentPlayer player, bool hasCamp, bool hasFurnace)
    {
        var notes = new List<object>();
        void Add(string topic, string advice) => notes.Add(new { topic, advice });
        Add("延续目标", "把长期意图保留在 goal，当前一至四步只是其中一段。已收集材料、放好的工具和剩余前置不要忘；只有新危险、前提失效或收益明显下降才改目标。");
        Add("食物账本", "比较当天消耗和次日启动成本。饱食高但没有可吃库存也不算供食稳定；一次南瓜或一次猎获只解决眼前，不等于建立了食物来源。");
        Add("时间取舍", "做任务要计入往返、制作、烤制和日落。先确保能活到下一次收获，再投入工具或建筑；不要为了补满目标库存无限追猎或绕路。");
        var vitals = player.ComponentVitalStats;
        var carry = player.ComponentMiner.Inventory;
        bool Has(Func<int, bool> predicate) => Enumerable.Range(0, carry.SlotsCount)
            .Any(slot => carry.GetSlotCount(slot) > 0 && predicate(carry.GetSlotValue(slot)));
        bool rawMeat = Has(PlaySurvivalFood.IsRawMeat);
        if (rawMeat)
            Add("已有生肉", hasFurnace
                ? "已有炉具和生肉时，优先比较返回烤制与继续追猎的收益。确认有燃料，取回熟食；饥饿时可把吃饭排在烹饪之后。"
                : "生肉尚不能算可靠口粮。看不到露天岩石时可选 quarry 编号，从已见土面浅挖阶梯找石，带一块样本回地面；再权衡制石斧、采八圆石、造炉和烤肉。浅探最深四格且不保证有石头，快饿死时先找现成食物。");
        else if (PlayFoodBudget.NeedsSupply(player))
            Add("寻找主食", "先比较已见成熟南瓜和有肉鸟的可达性。木矛投猎鸭、乌鸦、海鸥；不要为麻雀鸽子白耗时间。附近资源用尽后选新方向巡猎，避开反复受阻与猎食者区域。");
        else
            Add("巩固供食", "有一天余粮可投入营地与续种。保留一部分南瓜应急，另一部分制种；肉按近期消耗烤制，避免熟食腐败。幼苗尚未成熟时仍需狩猎与采集。");
        if (vitals.Temperature < 8f || vitals.Wetness > 0.2f || player.ComponentFlu.HasFlu)
            Add("湿冷恢复", "先离水到干地，结合实际变化决定回营、生火或取食。已有燃烧营火时可先返回、添一份柴，再选 warm_at_own_campfire 连续晾干回暖；不要反复重造。初放营火三档，雨中每五秒减一档，干燥时约三十秒减一档；熄灭后添柴不能复燃。");
        if (player.Entity.FindComponent<ComponentOnFire>() is { IsOnFire: true } or { TouchesFire: true })
            Add("着火处理", "自身正在着火或接触火焰，先脱离火源，停止制作和等待。原生水浸过半可灭火，但只能走已确认可进出的浅水；附近无安全水体时在远离火源的安全处让火自然熄灭，观察生命值。");
        else
            Add("用火距离", "营火放在屋外不易蔓延的位置，与身体、进出路径、木墙和树叶保持距离；在旁边取暖即可，不要踩火。回暖过程中掉血不是成功，要检查火焰并离开危险位置。");
        Add(hasCamp ? "固定家园" : "选址与过夜", hasCamp
            ? "白天从营地外出并保留返程时间。安全睡眠只度过夜晚，醒来要重新检查门外、库存和天气；门外有狼且睡不着时不能靠反复睡眠解决。"
            : "早期临时土坑能过夜；有余粮和白天后可选四十块土或木板的二乘二小屋。选干燥平地，靠木石水源但不贴窄岸；房子要有屋顶、可封门口和安全出口。");
        return notes.ToArray();
    }
}
