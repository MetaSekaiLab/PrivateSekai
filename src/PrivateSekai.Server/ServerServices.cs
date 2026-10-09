using System;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Accounts;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Characters;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Modules.Home;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Live;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Music;
using PrivateSekai.Modules.Mysekai;
using PrivateSekai.Modules.Presents;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Modules.Story;
using PrivateSekai.Modules.Tutorial;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using PrivateSekai.Transport;

namespace PrivateSekai;

public static class ServerServices
{
    public static IServiceCollection AddPrivateSekai(this IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IUserStore>(_ => new SqliteUserStore(ServerConfig.UserDatabasePath));
        services.AddSingleton<UserLocks>();
        services.AddSingleton<AccountTemplates>();
        services.AddSingleton(_ => new CustomProfileThumbnailStore(ServerConfig.UserDatabasePath));
        services.AddScoped<UserSession>();
        services.AddScoped<UserOperation>();
        services.AddScoped<ResourceService>();

        services.AddSingleton(_ => new MasterData(ServerConfig.MasterCache, ServerConfig.SekaiMasterDbDiffPath));
        services.AddSingleton<CardMasterQueries>();
        services.AddSingleton<CharacterMasterQueries>();
        services.AddScoped<CharacterService>();
        services.AddSingleton<GachaMasterQueries>();
        services.AddSingleton<LiveMasterQueries>();
        services.AddSingleton<MissionMasterQueries>();
        services.AddSingleton<ResourceMasterQueries>();
        services.AddSingleton<ShopMasterQueries>();
        services.AddSingleton<StoryMasterQueries>();
        services.AddSingleton<LoginBonusMasterQueries>();

        services.AddSingleton<IResourceHandler, CurrencyResourceHandler>();
        services.AddSingleton<IResourceHandler, InventoryResourceHandler>();
        services.AddSingleton<IResourceHandler, CardResourceHandler>();
        services.AddSingleton<IResourceHandler, GachaResourceHandler>();
        services.AddSingleton<IResourceHandler, MusicResourceHandler>();
        services.AddSingleton<IResourceHandler, ProfileResourceHandler>();
        services.AddSingleton<IResourceHandler, HonorResourceHandler>();
        services.AddSingleton<IResourceHandler, MysekaiResourceHandler>();
        services.AddSingleton<IResourceHandler, AreaItemResourceHandler>();

        services.AddScoped<InheritService>();
        services.AddScoped<CardService>();
        services.AddScoped<GachaService>();
        services.AddScoped<HomeService>();
        services.AddScoped<FriendMasterQueries>();
        services.AddScoped<FriendService>();
        services.AddScoped<FriendQueries>();
        services.AddScoped<LoginBonusService>();
        services.AddScoped<LoginBonusStatusFilter>();
        services.AddScoped<LiveService>();
        services.AddScoped<BoostService>();
        services.AddScoped<DeckService>();
        services.AddScoped<MusicMyListService>();
        services.AddScoped<ChallengeLiveService>();
        services.AddScoped<MissionService>();
        services.AddScoped<IUserRefreshHandler>(services => services.GetRequiredService<MissionService>());
        services.AddScoped<PresentService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<ProfileHonorService>();
        services.AddScoped<MusicVideoService>();
        services.AddScoped<CostumeService>();
        services.AddScoped<CostumeMasterQueries>();
        services.AddScoped<ShopService>();
        services.AddScoped<StoryService>();
        services.AddScoped<StoryBookmarkService>();
        services.AddScoped<StoryFavoriteService>();
        services.AddScoped<TutorialService>();

        services.AddControllers(options =>
            options.InputFormatters.Insert(0, new PrskMessagePackInputFormatter()));
        return services;
    }
}
