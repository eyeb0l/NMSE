using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for the account reward consistency checker and its redeemed/known pairing.
/// </summary>
[Collection("MutableStaticDatabases")]
public class AccountConsistencyTests
{
    [Fact]
    public void StaleKnownSpecial_IsReportedAsAmbiguousWhenAccountUnlocked()
    {
        var jsonDir = FindResourceJsonDir();
        if (jsonDir == null) return;

        var db = LoadDatabase(jsonDir);
        var save = PlayerSave();
        Add(save, "KnownSpecials", "^SWARM_GUN");
        var account = new JsonObject();
        Add(account, "UnlockedSeasonRewards", "^SWARM_GUN");

        var issues = AccountLogic.CheckConsistencyStructured(save, db, account);

        var issue = Assert.Single(issues, i => i.CurrentArray == "KnownSpecials" && i.Id == "^SWARM_GUN");
        Assert.Equal("RedeemedSeasonRewards", issue.MissingArray);
        Assert.True(issue.IsAmbiguous);
    }

    [Fact]
    public void StaleKnownProduct_IsReportedForBuildingReward()
    {
        var jsonDir = FindResourceJsonDir();
        if (jsonDir == null) return;

        var db = LoadDatabase(jsonDir);
        var reward = RewardDatabase.SeasonRewards.FirstOrDefault(r =>
            string.Equals(r.ProductId, "EXPD_FLAG_06E", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Id, "^EXPD_FLAG_06E", StringComparison.OrdinalIgnoreCase));
        if (reward == null) return;

        var save = PlayerSave();
        Add(save, "KnownProducts", "^EXPD_FLAG_06E");

        var issues = AccountLogic.CheckConsistencyStructured(save, db);

        Assert.Contains(issues, i =>
            i.CurrentArray == "KnownProducts"
            && i.Id == "^EXPD_FLAG_06E"
            && i.MissingArray == "RedeemedSeasonRewards");
    }

    [Fact]
    public void EntitlementKnownSpecial_IsNotReportedAsStale()
    {
        var jsonDir = FindResourceJsonDir();
        if (jsonDir == null) return;

        var db = LoadDatabase(jsonDir);
        var entitlement = RewardDatabase.EntitlementRewards
            .FirstOrDefault(r => !string.IsNullOrEmpty(r.ProductId));
        if (entitlement == null) return;

        var save = PlayerSave();
        Add(save, "KnownSpecials", "^" + entitlement.ProductId);

        var issues = AccountLogic.CheckConsistencyStructured(save, db);

        Assert.DoesNotContain(issues, i =>
            i.CurrentArray == "KnownSpecials"
            && string.Equals(i.Id, "^" + entitlement.ProductId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RedeemedSeasonRewardWithoutKnownSpecial_IsReported()
    {
        var jsonDir = FindResourceJsonDir();
        if (jsonDir == null) return;

        var db = LoadDatabase(jsonDir);
        var save = PlayerSave();
        Add(save, "RedeemedSeasonRewards", "^SWARM_GUN");

        var issues = AccountLogic.CheckConsistencyStructured(save, db);

        Assert.Contains(issues, i =>
            i.CurrentArray == "RedeemedSeasonRewards"
            && i.MissingArray == "KnownSpecials"
            && i.Id == "^SWARM_GUN");
    }

    [Theory]
    [InlineData("RedeemedSeasonRewards", "^EXPD_FLAG_06E")]
    [InlineData("RedeemedTwitchRewards", "^TWITCH_210")]
    public void SharedRewardProduct_AcceptsEitherRedemptionSource(string array, string rewardId)
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, "KnownSpecials", "^EXPD_FLAG_06E");
        Add(save, "KnownProducts", "^EXPD_FLAG_06E");
        Add(save, array, rewardId);

        Assert.Empty(AccountLogic.CheckConsistencyStructured(save, db));
    }

    [Fact]
    public void ReverseTwitchRepair_WritesRewardIdAndClearsTheIssue()
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, "KnownSpecials", "^S_POSTER29");
        Add(save, "KnownProducts", "^S_POSTER29");
        var issue = Assert.Single(AccountLogic.CheckConsistencyStructured(save, db),
            issue => issue.CurrentArray == "KnownProducts");

        AccountLogic.ResolveConsistencyIssue(save, issue, addToMissing: true);

        Assert.Equal(new[] { "^TWITCH_406" }, Values(save, "RedeemedTwitchRewards"));
        Assert.Empty(AccountLogic.CheckConsistencyStructured(save, db));
    }

    [Fact]
    public void ReverseTwitchRemoval_RemovesProductIdFromKnownArray()
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, "KnownProducts", "^S_POSTER29");
        var issue = Assert.Single(AccountLogic.CheckConsistencyStructured(save, db));

        AccountLogic.ResolveConsistencyIssue(save, issue, addToMissing: false);

        Assert.Empty(Values(save, "KnownProducts"));
        Assert.Empty(Values(save, "RedeemedTwitchRewards"));
        Assert.Empty(AccountLogic.CheckConsistencyStructured(save, db));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForwardTwitchRepair_UsesTheIdBelongingToTheTargetArray(bool add)
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, "KnownSpecials", "^S_POSTER29");
        Add(save, "RedeemedTwitchRewards", "^TWITCH_406");
        var issue = Assert.Single(AccountLogic.CheckConsistencyStructured(save, db),
            issue => issue.MissingArray == "KnownProducts");

        AccountLogic.ResolveConsistencyIssue(save, issue, addToMissing: add);

        Assert.Equal(add ? new[] { "^S_POSTER29" } : Array.Empty<string>(), Values(save, "KnownProducts"));
        Assert.Equal(add ? new[] { "^TWITCH_406" } : Array.Empty<string>(), Values(save, "RedeemedTwitchRewards"));
    }

    [Theory]
    [InlineData("^S_POSTER29", "UnlockedTwitchRewards", "^TWITCH_406", "KnownProducts")]
    [InlineData("^TGA_SHIP01", "UnlockedPlatformRewards", "^TGA_SHIP1", "KnownSpecials")]
    [InlineData("^EXPD_FLAG_06E", "UnlockedTwitchRewards", "^TWITCH_210", "KnownProducts")]
    public void ReverseIssue_UsesCategoryRewardIdForAccountUnlocks(string product, string unlockedArray, string reward, string knownArray)
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, knownArray, product);
        var account = new JsonObject();
        Add(account, unlockedArray, reward);

        var issue = Assert.Single(AccountLogic.CheckConsistencyStructured(save, db, account));
        Assert.True(issue.IsAmbiguous);
        string redeemedArray = unlockedArray.Replace("Unlocked", "Redeemed");
        Assert.Equal(redeemedArray, issue.MissingArray);
        AccountLogic.ResolveConsistencyIssue(save, issue, addToMissing: true);
        Assert.Equal(new[] { reward }, Values(save, redeemedArray));
    }

    [Fact]
    public void ProductIdIsAnUnlockOnlyInUnlockedSpecials()
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, "KnownProducts", "^S_POSTER29");
        var account = new JsonObject();
        Add(account, "UnlockedTwitchRewards", "^S_POSTER29");
        Assert.False(Assert.Single(AccountLogic.CheckConsistencyStructured(save, db, account)).IsAmbiguous);

        Add(account, "UnlockedSpecials", "S_POSTER29");
        Assert.True(Assert.Single(AccountLogic.CheckConsistencyStructured(save, db, account)).IsAmbiguous);
    }

    [Theory]
    [InlineData("^TGA_SHIP1", "^TGA_SHIP01")]
    [InlineData("^SW_PREORDER", "^SWITCH_SHIP01")]
    [InlineData("^SW_PREORDER2", "^SWITCH_GUN01")]
    public void PlatformRedemptionWithoutKnownProduct_IsReportedAndRepairable(string rewardId, string productId)
    {
        var db = LoadRequiredDatabase();
        var save = PlayerSave();
        Add(save, "RedeemedPlatformRewards", rewardId);
        var issue = Assert.Single(AccountLogic.CheckConsistencyStructured(save, db),
            issue => issue.MissingArray == "KnownSpecials");
        Assert.Equal(productId, issue.Id);
        Assert.Equal(rewardId, issue.RewardId);
        Assert.Equal("RedeemedPlatformRewards", issue.CurrentArray);

        AccountLogic.ResolveConsistencyIssue(save, issue, addToMissing: true);

        Assert.Contains(productId, Values(save, "KnownSpecials"));
        Assert.DoesNotContain(rewardId, Values(save, "KnownSpecials"));
        Assert.Empty(AccountLogic.CheckConsistencyStructured(save, db));
    }

    private static string[] Values(JsonObject save, string arrayName)
    {
        var array = save.GetObject("PlayerStateData")!.GetArray(arrayName);
        return array == null ? [] : Enumerable.Range(0, array.Length).Select(i => array.GetString(i)!).ToArray();
    }

    private static GameItemDatabase LoadRequiredDatabase() => LoadDatabase(
        FindResourceJsonDir() ?? throw new DirectoryNotFoundException("Test Resources/json directory is missing."));

    private static GameItemDatabase LoadDatabase(string jsonDir)
    {
        RewardDatabase.LoadFromJsonDirectory(jsonDir);
        var db = new GameItemDatabase();
        db.LoadItemsFromJsonDirectory(jsonDir);
        return db;
    }

    private static JsonObject PlayerSave()
    {
        var save = new JsonObject();
        save.Set("PlayerStateData", new JsonObject());
        return save;
    }

    private static void Add(JsonObject save, string arrayName, string id)
    {
        var player = save.GetObject("PlayerStateData") ?? save;
        var array = player.GetArray(arrayName);
        if (array == null)
        {
            array = new JsonArray();
            player.Set(arrayName, array);
        }
        array.Add(id);
    }

    private static string? FindResourceJsonDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Resources", "json");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
