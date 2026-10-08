extern alias game;

using game::Sekai;
using PrivateSekai.Modules.Missions;

namespace PrivateSekai.Modules.Live;

public sealed class MusicVideoService(LiveMasterQueries master, MissionService missions)
{
    public void Record(int musicId, UserMusicVideoRequest request)
    {
        if (request.musicPlayStatus == "end" && master.IsMusicVideoSelection(musicId, request.musicVocal, request.musicCategoryName))
            missions.RecordMusicVideoWatch();
    }
}
