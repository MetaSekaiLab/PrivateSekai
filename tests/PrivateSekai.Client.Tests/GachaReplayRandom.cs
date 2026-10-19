extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Shared.Master;

// 按已观察到的抽取顺序固定随机输入，只对拍该结果下的扣发与状态，不验证官方概率。
internal sealed class GachaReplayRandom(MasterData master) : Random
{
    private readonly Queue<(double Rarity, long Card, long Total)> rolls = new();

    public void Prepare(JsonObject record)
    {
        if (rolls.Count != 0) throw new InvalidOperationException("上一样本的随机输入未全部使用。");
        if (record["httpStatus"]?.GetValue<int>() != 200) return;
        var id = int.Parse(record["args"]!["gachaId"]!.GetValue<string>());
        var gacha = master.GetTable<MasterGacha>("gachas", g => g.id).FindById(id)!;
        var prizes = record["response"]!["obtainPrizes"]!.AsArray();
        var cards = master.GetTable<MasterCard>("cards", c => c.id);
        var rates = gacha.gachaCardRarityRates.Where(r => r.lotteryType == "normal" && r.rate > 0 &&
            gacha.gachaDetails.Any(d => d.weight > 0 && cards.FindById(d.cardId)?.cardRarityType == r.cardRarityType)).ToArray();
        foreach (var prize in prizes)
        {
            if (prize!["gachaLotteryType"]?.GetValue<string>() != "normal")
                throw new InvalidOperationException("需要普通抽取结果样本。");
            var cardId = prize["card"]!["resourceId"]!.GetValue<int>();
            var rarity = cards.FindById(cardId)!.cardRarityType;
            var rate = rates.Single(r => r.cardRarityType == rarity);
            var rarityRoll = (rates.TakeWhile(r => r != rate).Sum(r => r.rate) + rate.rate / 2) / rates.Sum(r => r.rate);
            var details = gacha.gachaDetails.Where(d => d.weight > 0 && cards.FindById(d.cardId)?.cardRarityType == rarity).ToArray();
            var selected = details.Single(d => d.cardId == cardId);
            rolls.Enqueue((rarityRoll, details.TakeWhile(d => d != selected).Sum(d => (long)d.weight), details.Sum(d => (long)d.weight)));
        }
    }

    public override double NextDouble() => rolls.Peek().Rarity;
    public override long NextInt64(long maxValue)
    {
        var roll = rolls.Dequeue();
        return maxValue == roll.Total ? roll.Card : throw new InvalidOperationException("抽卡权重与样本对应的master不同。");
    }
}
