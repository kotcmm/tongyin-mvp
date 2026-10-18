namespace Tongyin.Application.Works;

/// <summary>将运营人员核实的真实 GLB 模型安全保存和撤销的基础设施端口。</summary>
public interface IModelUploadStore
{
    /// <summary>验证 glTF 2.0 格式后保存不超过 100 MiB 的二进制模型，返回文件名。</summary>
    Task<string> SaveGlbAsync(Stream source, long length, CancellationToken cancellationToken = default);

    /// <summary>当数据库拒绝关联本次上传时删除新保存的孤立模型文件。</summary>
    void Delete(string fileName);
}
