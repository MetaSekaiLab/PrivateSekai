using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserLiveCharacterArchiveVoice
{
    [Key("characterArchiveVoiceGroupIds")] public List<int>? characterArchiveVoiceGroupIds;
}
