using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiSiteHousingLayoutData
{
    [Key("mysekaiLayoutType")] public string? mysekaiLayoutType;
    [Key("mysekaiFixtures")] public List<UserMysekaiFixtureLayout>? mysekaiFixtures;
    [Key("mysekaiCustomFixturePhotos")] public List<UserMysekaiCustomFixturePhotoLayout>? mysekaiCustomFixturePhotos;
    [Key("mysekaiCanvases")] public List<UserMysekaiCanvasLayout>? mysekaiCanvases;
    [Key("mysekaiCustomFixtureCollections")] public List<UserMysekaiCustomFixtureCollectionLayout>? mysekaiCustomFixtureCollections;
    [Key("mysekaiCustomFixturePenlights")] public List<UserMysekaiCustomFixturePenlightLayout>? mysekaiCustomFixturePenlights;
    [Key("mysekaiCustomFixtureHonors")] public List<UserMysekaiCustomFixtureHonorLayout>? mysekaiCustomFixtureHonors;
    [Key("mysekaiCustomFixtureBondsHonors")] public List<UserMysekaiCustomFixtureBondsHonorLayout>? mysekaiCustomFixtureBondsHonors;
    [Key("mysekaiCustomFixtureRecordJackets")] public List<UserMysekaiCustomFixtureRecordJacketLayout>? mysekaiCustomFixtureRecordJackets;
    [Key("mysekaiGrowingPlants")] public List<UserMysekaiGrowingPlantLayout>? mysekaiGrowingPlants;
}
