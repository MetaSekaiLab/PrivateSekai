namespace PrivateSekai.Shared.Users;

/// <summary>读写均隔离可变引用，Save 原子替换完整用户状态。</summary>
public interface IUserStore
{
    long ReserveId();
    long[] GetUserIds();
    UserState? Read(long userId);
    void Save(long userId, UserState state);
}
