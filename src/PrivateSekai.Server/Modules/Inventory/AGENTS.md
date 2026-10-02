# 库存

- 负责货币、材料、练习券和 boost 道具数量；`userGamedata` 中只修改对应货币字段。
- `ResourceMasterQueries` 提供资源箱和通用资源查询，不保存用户。
- 当前扣款保留旧实现的扣至零行为；余额不足策略应独立审计，不能在迁移时推断。
- 新资源类型需显式注册 Handler；未知类型或未实现方向会使整次用户操作失败。
- 资源映射来自现有实现和 master；验证见 `ResourceTests`。
