extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Story;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using EmptyResponse = PrivateSekai.Models.EmptyResponse;

namespace PrivateSekai.Tests;

internal static class StoryCollectionChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/story-collection-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "configs.json"), """
                [{"configKey":"story_episode_bookmark_count","value":"20"},
                 {"configKey":"story_episode_bookmark_name_max_length","value":"20"},
                 {"configKey":"story_favorite_count_limit","value":"10"}]
                """);
            File.WriteAllText(Path.Combine(directory, "unitStoryEpisodeGroups.json"), """[{"id":1}]""");
            File.WriteAllText(Path.Combine(directory, "eventStories.json"), """[{"id":1},{"id":2}]""");
            File.WriteAllText(Path.Combine(directory, "unitStories.json"),
                """
                [{"chapters":[{"episodes":[{"id":11,"rewardResourceBoxIds":[1]},
                {"id":14,"unitStoryEpisodeGroupId":3,"episodeNo":1,"releaseConditionId":1,"rewardResourceBoxIds":[1]},
                {"id":80,"unitStoryEpisodeGroupId":3,"episodeNo":2,"releaseConditionId":4},
                {"id":81,"unitStoryEpisodeGroupId":3,"episodeNo":3,"releaseConditionId":3},
                {"id":90,"unitStoryEpisodeGroupId":4,"episodeNo":2,"releaseConditionId":4},
                {"id":15,"releaseConditionId":1,"andReleaseConditionId":2,"rewardResourceBoxIds":[1]}]}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "releaseConditions.json"),
                """
                [{"id":1,"releaseConditionType":"none"},{"id":2,"releaseConditionType":"user_rank"},
                {"id":3,"releaseConditionType":"unit_story","releaseConditionTypeId":14},
                {"id":4,"releaseConditionType":"unit_story","releaseConditionTypeId":99}]
                """);
            File.WriteAllText(Path.Combine(directory, "specialStories.json"),
                """[{"id":1,"episodes":[{"id":12,"rewardResourceBoxIds":[1]},{"id":13,"rewardResourceBoxIds":[99]}]}]""");
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"),
                """[{"id":1,"resourceBoxPurpose":"episode_reward","details":[{"resourceType":"material","resourceId":10,"resourceQuantity":3}]}]""");
            var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(master).AddSingleton<IUserStore>(store).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var bookmarks = scope.ServiceProvider.GetRequiredService<StoryBookmarkService>();
            var favorites = scope.ServiceProvider.GetRequiredService<StoryFavoriteService>();
            var stories = scope.ServiceProvider.GetRequiredService<StoryService>();
            store.Save(1, TestUsers.Create(1));
            store.Save(2, TestUsers.Create(2));
            // 只用于验证字节存储，不作为真实截图。
            byte[] thumbnail = [0xff, 0xd8, 0xff, 0xd9];
            PostStoryEpisodeBookmarkRequest Request(string name = "书签") => new()
            {
                name = name, bookmarkNameEditStatus = "edit", thumbnail = Convert.ToBase64String(thumbnail)
            };
            var encoded = operations.Execute(1, () => new PostStoryEpisodeBookmarkResponse
            {
                userStoryEpisodeBookmark = bookmarks.Add("event_story", 1, 10, 0, Request()),
                updatedResources = user.BuildRefresh()
            });
            var added = DumpSerializer.Deserialize<PostStoryEpisodeBookmarkResponse>(encoded);
            Check.That(added.userStoryEpisodeBookmark.name == "书签" &&
                added.updatedResources.userBookmarkedStories.Single().storyType == "event_story",
                "创建书签返回实际协议对象及剧情汇总");
            Check.That(store.Read(1)!.Private.StoryBookmarks["event_story"].Single().Thumbnail.SequenceEqual(thumbnail) &&
                store.Read(2)!.Private.StoryBookmarks.Count == 0, "书签图片随用户隔离存储并可往返复制");
            var image = new StoryBookmarkController(operations, user, bookmarks).Thumbnail(1,
                added.userStoryEpisodeBookmark.thumbnailPath.Split('/').Last()) as FileContentResult;
            Check.That(image?.ContentType == "image/jpeg" && image.FileContents.SequenceEqual(thumbnail),
                "书签缩略图路径可读取原始 JPEG 字节");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                bookmarks.Add("event_story", 1, 10, 0, Request())), "同一对白不可重复添加书签");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                bookmarks.Add("event_story", 1, 10, 1, Request(new string('字', 21)))), "书签名称使用 master 长度限制");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                bookmarks.Rename("event_story", 1, 10, 0, new() { name = "失败修改", bookmarkNameEditStatus = "edit" });
                return new BrokenResponse();
            }), "书签名称修改编码失败时回滚");
            Check.That(store.Read(1)!.Private.StoryBookmarks["event_story"].Single().Bookmark.name == "书签",
                "失败修改不污染持久书签");
            var renamed = DumpSerializer.Deserialize<UserStoryEpisodeBookmark>(operations.Execute(1, () =>
                bookmarks.Rename("event_story", 1, 10, 0, new() { name = "新名称", bookmarkNameEditStatus = "edit" })));
            Check.That(renamed.name == "新名称" && renamed.createdAt == added.userStoryEpisodeBookmark.createdAt,
                "修改书签返回裸对象并保留创建时间");
            operations.Execute(1, () =>
            {
                for (var talk = 1; talk < 20; talk++) bookmarks.Add("event_story", 1, 10, talk, Request());
                bookmarks.Add("unit_story", 1, 10, 0, Request());
                return user.BuildRefresh();
            });
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                bookmarks.Add("event_story", 1, 10, 20, Request())), "每个剧情最多 20 条书签，类型之间独立");
            operations.Execute(1, () =>
            {
                bookmarks.RecordClick("unit_story", 1, 10, 0);
                bookmarks.Delete("unit_story", 1, 10, 0);
                return user.BuildRefresh();
            });
            Check.That(store.Read(1)!.Data.userBookmarkedStories.Single().storyType == "event_story",
                "最后一条书签删除后同步移除剧情汇总");
            new StoryBookmarkController(operations, user, bookmarks).Click(1, "event_story", 1, 10, 0);
            Check.That(store.Read(1)!.Private.StoryBookmarks["event_story"].First().ClickCount == 1,
                "点击书签的空响应可编码并提交");

            operations.Execute(1, () =>
            {
                favorites.Set(1, "event_story", 1);
                favorites.Set(10, "unit_story", 1);
                favorites.Set(1, "event_story", 2);
                return user.BuildRefresh();
            });
            Check.That(store.Read(1)!.Data.userStoryFavorites.Length == 2 &&
                store.Read(1)!.Data.userStoryFavorites.Single(f => f.shareNo == 1).storyId == 2,
                "收藏指定槽位可替换，首末槽位可用");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
            {
                favorites.Set(0, "event_story", 1);
                return user.BuildRefresh();
            }), "收藏槽位不接受零");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
            {
                favorites.Set(11, "event_story", 1);
                return user.BuildRefresh();
            }), "收藏槽位不超过 master 上限");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
            {
                favorites.Set(2, "event_story", 99);
                return user.BuildRefresh();
            }), "收藏拒绝不存在的剧情");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                favorites.Delete(1);
                return new BrokenResponse();
            }), "收藏删除编码失败时回滚");
            Check.That(store.Read(1)!.Data.userStoryFavorites.Length == 2, "收藏失败删除不丢失原槽位");
            var deleted = DumpSerializer.Deserialize<SuiteUserCommonResponse>(operations.Execute(1, () =>
            {
                favorites.Delete(1);
                return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
            }));
            Check.That(deleted.updatedResources.userStoryFavorites.Single().shareNo == 10,
                "删除收藏返回剩余槽位列表");
            operations.Execute(1, () =>
            {
                user.Data.userUnitEpisodeStatuses = [new() { episodeId = 11, status = "released" },
                    new() { episodeId = 14, status = "unreleased" }, new() { episodeId = 15, status = "unreleased" },
                    new() { episodeId = 81, status = "can_not_read" }];
                user.Data.userSpecialEpisodeStatuses = [new() { episodeId = 12, status = "unreleased" },
                    new() { episodeId = 13, status = "released" }];
                return new EmptyResponse();
            });
            Check.Throws<ArgumentException>(() => operations.Execute(1, () => stories.CompleteStoryEpisode("special_story", 12)),
                "锁定剧情不可领取首读奖励");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () => stories.CompleteStoryEpisode("unit_story", 15)),
                "无主条件的剧情仍须核对附加解锁条件");
            operations.Execute(1, () =>
            {
                user.Data.userReleaseConditions = [new() { releaseConditionId = 2 }];
                return new EmptyResponse();
            });
            var conditional = DumpSerializer.Deserialize<UserResource[]>(operations.Execute(1, () => stories.CompleteStoryEpisode("unit_story", 15)));
            Check.That(conditional.Single().quantity == 3 &&
                store.Read(1)!.Data.userUnitEpisodeStatuses.Single(s => s.episodeId == 15).status == "already_read",
                "已有附加条件达成记录时允许主线首读");
            Check.Throws<InvalidOperationException>(() => operations.Execute(1, () => stories.CompleteStoryEpisode("special_story", 13)),
                "剧情缺少奖励盒时不猜测奖励");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                stories.CompleteStoryEpisode("unit_story", 11);
                return new BrokenResponse();
            }), "剧情首读奖励与状态同时回滚");
            Check.That(store.Read(1)!.Data.userUnitEpisodeStatuses.Single(s => s.episodeId == 11).status == "released", "首读失败保留可领取状态");
            var reward = DumpSerializer.Deserialize<UserResource[]>(operations.Execute(1, () => stories.CompleteStoryEpisode("unit_story", 11)));
            var repeat = DumpSerializer.Deserialize<UserResource[]>(operations.Execute(1, () => stories.CompleteStoryEpisode("unit_story", 11)));
            Check.That(reward.Single().quantity == 3 && repeat.Length == 0 &&
                store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 10).quantity == 6,
                "主线剧情读取嵌套 master 奖励且重复请求不再发奖");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                stories.CompleteStoryEpisode("unit_story", 14);
                return new BrokenResponse();
            }), "章节展开和解锁条件随响应编码失败回滚");
            Check.That(!store.Read(1)!.Data.userReleaseConditions.Any(c => c.releaseConditionId == 3) &&
                !store.Read(1)!.Data.userUnitEpisodeStatuses.Any(s => s.episodeId == 80) &&
                store.Read(1)!.Data.userUnitEpisodeStatuses.Single(s => s.episodeId == 81).status == "can_not_read",
                "失败首读不留下解锁条件或新增章节");
            var opening = DumpSerializer.Deserialize<UserResource[]>(operations.Execute(1, () => stories.CompleteStoryEpisode("unit_story", 14)));
            var openingRepeat = DumpSerializer.Deserialize<UserResource[]>(operations.Execute(1, () => stories.CompleteStoryEpisode("unit_story", 14)));
            Check.That(opening.Single().quantity == 3 && openingRepeat.Length == 0 &&
                store.Read(1)!.Data.userUnitEpisodeStatuses.Single(s => s.episodeId == 14).status == "already_read" &&
                store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 10).quantity == 9,
                "无解锁条件的 unreleased 主线可首读且奖励只发一次");
            Check.That(store.Read(1)!.Data.userReleaseConditions.Count(c => c.releaseConditionId == 3) == 1 &&
                store.Read(1)!.Data.userReleaseConditions.Single(c => c.releaseConditionId == 3).createdAt > 0 &&
                store.Read(1)!.Data.userUnitEpisodeStatuses.Single(s => s.episodeId == 81).status == "unreleased",
                "首读写入唯一达成条件并解锁已展开章节");
            Check.That(store.Read(1)!.Data.userUnitEpisodeStatuses.Single(s => s.episodeId == 80).status == "can_not_read" &&
                !store.Read(1)!.Data.userUnitEpisodeStatuses.Any(s => s.episodeId == 90),
                "下一话按组内序号展开并保留锁定，不依赖连续 ID 或混入其他分组");
            Console.WriteLine("剧情收藏：书签、缩略图、收藏槽位和失败回滚检查通过。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
