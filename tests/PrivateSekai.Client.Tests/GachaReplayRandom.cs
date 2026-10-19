extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Shared.Master;

// 按已观察到的单抽结果固定随机输入，只对拍该结果下的扣发与状态，不验证官方概率。
internal sealed class GachaReplayRandom(MasterData master) : Random
{
    private double rarityRoll;
    private long cardRoll;
    private long weightTotal;

    public void Prepare(JsonObject record)
    {
        if (record["httpStatus"]?.GetValue<int>() != 200) return;
        var id = int.Parse(record["args"]!["gachaId"]!.GetValue<string>());
        var gacha = master.GetTable<MasterGacha>("gachas", g => g.id).FindById(id)!;
        var prizes = record["response"]!["obtainPrizes"]!.AsArray();
        if (prizes.Count != 1) throw new InvalidOperationException("需要普通单抽样本。");
        var cardId = prizes[0]!["card"]!["resourceId"]!.GetValue<int>();
        var cards = master.GetTable<MasterCard>("cards", c => c.id);
        var rarity = cards.FindById(cardId)!.cardRarityType;
        var rates = gacha.gachaCardRarityRates.Where(r => r.lotteryType == "normal" && r.rate > 0 &&
            gacha.gachaDetails.Any(d => d.weight > 0 && cards.FindById(d.cardId)?.cardRarityType == r.cardRarityType)).ToArray();
        var rate = rates.Single(r => r.cardRarityType == rarity);
        rarityRoll = (rates.TakeWhile(r => r != rate).Sum(r => r.rate) + rate.rate / 2) / rates.Sum(r => r.rate);
        var details = gacha.gachaDetails.Where(d => d.weight > 0 && cards.FindById(d.cardId)?.cardRarityType == rarity).ToArray();
        var selected = details.Single(d => d.cardId == cardId);
        cardRoll = details.TakeWhile(d => d != selected).Sum(d => (long)d.weight);
        weightTotal = details.Sum(d => (long)d.weight);
    }

    public override double NextDouble() => rarityRoll;
    public override long NextInt64(long maxValue) => maxValue == weightTotal ? cardRoll :
        throw new InvalidOperationException("抽卡权重与样本对应的master不同。");
}
